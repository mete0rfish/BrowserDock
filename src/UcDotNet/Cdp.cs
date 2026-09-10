using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using UcDotNet.Hosting;

namespace UcDotNet.Cdp;

internal sealed record CdpEvent(string Method, string? SessionId, JsonElement Parameters);
internal sealed class CdpConnection : IAsyncDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim sendGate = new(1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly Channel<CdpEvent> events = Channel.CreateBounded<CdpEvent>(new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true });
    private long sequence;
    private Task reader = Task.CompletedTask;
    private Task dispatcher = Task.CompletedTask;
    public event Action<CdpEvent>? Event;
    public bool Healthy => socket.State == WebSocketState.Open && !lifetime.IsCancellationRequested;
    internal void InterruptForTest() => Fail(new IOException("Injected socket interruption."));
    public async Task ConnectAsync(Uri endpoint, CancellationToken token)
    {
        if (endpoint.Scheme != "ws" || !endpoint.IsLoopback) throw new UcException(ErrorCategory.ConfigurationError, "CDP requires a loopback WebSocket endpoint.");
        socket.Options.Proxy = null;
        await socket.ConnectAsync(endpoint, token);
        reader = ReadAsync();
        dispatcher = DispatchAsync();
    }
    public async Task<JsonElement> SendAsync(string method, object? parameters, string? session, CancellationToken token)
    {
        if (!Healthy) throw new UcException(ErrorCategory.DevToolsEndpointFailure, "CDP connection is unavailable.");
        var id = Interlocked.Increment(ref sequence);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            using var cancellation = token.Register(() => completion.TrySetCanceled(token));
            var payload = new Dictionary<string, object?> { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new { } };
            if (session is not null) payload["sessionId"] = session;
            await sendGate.WaitAsync(token);
            try { await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload), WebSocketMessageType.Text, true, token); }
            finally { sendGate.Release(); }
            return await completion.Task;
        }
        finally { pending.TryRemove(id, out _); }
    }
    private async Task ReadAsync()
    {
        var buffer = new byte[16384];
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, lifetime.Token);
                    if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("CDP peer closed.");
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > 16 * 1024 * 1024) throw new UcException(ErrorCategory.ProtocolError, "CDP response exceeds the 16 MiB limit.");
                } while (!result.EndOfMessage);
                using var json = JsonDocument.Parse(message.ToArray());
                var root = json.RootElement;
                if (root.TryGetProperty("id", out var id))
                {
                    if (!pending.TryGetValue(id.GetInt64(), out var request)) continue;
                    if (root.TryGetProperty("error", out var error)) request.TrySetException(new UcException(ErrorCategory.ProtocolError, $"CDP command failed ({error.GetProperty("code").GetInt32()})."));
                    else request.TrySetResult(root.GetProperty("result").Clone());
                }
                else if (root.TryGetProperty("method", out var method))
                {
                    var item = new CdpEvent(method.GetString()!, root.TryGetProperty("sessionId", out var session) ? session.GetString() : null, root.GetProperty("params").Clone());
                    if (!events.Writer.TryWrite(item)) throw new UcException(ErrorCategory.ProtocolError, "CDP lifecycle event buffer overflow; connection must be recovered.");
                }
            }
        }
        catch (Exception e) { Fail(e); }
        finally { events.Writer.TryComplete(); }
    }
    private async Task DispatchAsync()
    {
        try { await foreach (var item in events.Reader.ReadAllAsync(lifetime.Token)) Event?.Invoke(item); }
        catch (Exception e) { Fail(e); }
    }
    private void Fail(Exception error)
    {
        lifetime.Cancel(); socket.Abort();
        foreach (var request in pending.Values) request.TrySetException(new UcException(ErrorCategory.DevToolsEndpointFailure, "CDP transport was interrupted.", error));
    }
    public async ValueTask DisposeAsync()
    {
        Fail(new ObjectDisposedException(nameof(CdpConnection)));
        await Task.WhenAll(reader, dispatcher).WaitAsync(TimeSpan.FromSeconds(2));
        socket.Dispose(); lifetime.Dispose();
    }
}

internal sealed record TargetEntry(TargetKey Key, string Id, string Url, string Title, string? Opener);
internal sealed record PageSession(TargetEntry Target, string SessionId);
internal sealed record PageResult(Uri Url, IReadOnlyList<string> Redirects, NavigationOutcome Outcome);
internal sealed class CdpController(Uri endpoint, IReadOnlyList<string> scripts, bool removeDiscoveredCdcProperties = false) : IAsyncDisposable
{
    private CdpConnection connection = new();
    private readonly ConcurrentDictionary<string, TargetEntry> targets = new();
    private readonly ConcurrentDictionary<string, string> sessions = new();
    private readonly ConcurrentDictionary<string, byte> crashed = new();
    private readonly Dictionary<string, HashSet<string>> registered = new(StringComparer.Ordinal);
    private readonly object registrySync = new();
    private readonly Dictionary<string, long> revisions = new(StringComparer.Ordinal);
    private long eventRevision;
    public bool Healthy => connection.Healthy;
    internal void InterruptForTest() => connection.InterruptForTest();
    public TargetKey? Controlled { get; private set; }
    public bool ExplicitSelection { get; private set; }
    public async Task InitializeAsync(CancellationToken token)
    {
        await connection.ConnectAsync(endpoint, token);
        connection.Event += OnEvent;
        await BrowserAsync("Browser.getVersion", null, token);
        await ValidateProtocolAsync(token);
        await BrowserAsync("Browser.setDownloadBehavior", new { behavior = "default", eventsEnabled = true }, token);
        await BrowserAsync("Target.setDiscoverTargets", new { discover = true }, token);
        await RefreshAsync(token);
        if (targets.IsEmpty) { var created = await BrowserAsync("Target.createTarget", new { url = "about:blank" }, token); await RefreshAsync(token); SelectById(created.GetProperty("targetId").GetString()!, false); }
        else if (Controlled is null && targets.Count == 1) SelectById(targets.Values.Single().Id, false);
        if (Controlled is not null) await SessionAsync(Controlled.Value, token);
    }
    private async Task ValidateProtocolAsync(CancellationToken token)
    {
        using var http = BrowserHosting.Http();
        using var protocol = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{endpoint.Port}/json/protocol", token));
        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var domain in protocol.RootElement.GetProperty("domains").EnumerateArray())
            if (domain.TryGetProperty("commands", out var commands))
                foreach (var command in commands.EnumerateArray()) available.Add(domain.GetProperty("domain").GetString() + "." + command.GetProperty("name").GetString());
        foreach (var method in new[] { "Browser.getVersion", "Browser.close", "Browser.setDownloadBehavior", "Target.setDiscoverTargets", "Target.getTargets", "Target.attachToTarget", "Target.activateTarget", "Target.createTarget", "Target.closeTarget", "Page.enable", "Page.navigate", "Page.getFrameTree", "Page.setLifecycleEventsEnabled", "Page.addScriptToEvaluateOnNewDocument", "Runtime.enable", "Runtime.evaluate", "Runtime.callFunctionOn", "Network.enable" })
            if (!available.Contains(method)) throw new UcException(ErrorCategory.ProtocolError, $"Required CDP method is missing: {method}.");
    }
    private void OnEvent(CdpEvent item)
    {
        lock (registrySync) OnRegistryEvent(item);
    }
    private void OnRegistryEvent(CdpEvent item)
    {
        if (item.Method is "Target.targetCreated" or "Target.targetInfoChanged")
        {
            var info = item.Parameters.GetProperty("targetInfo");
            revisions[info.GetProperty("targetId").GetString()!] = ++eventRevision;
            Upsert(info);
        }
        else if (item.Method == "Target.targetDestroyed")
        {
            var id = item.Parameters.GetProperty("targetId").GetString()!;
            revisions[id] = ++eventRevision;
            targets.TryRemove(id, out _); sessions.TryRemove(id, out _);
        }
        else if (item.Method == "Target.detachedFromTarget")
        {
            var session = item.Parameters.GetProperty("sessionId").GetString();
            foreach (var pair in sessions.Where(x => x.Value == session)) sessions.TryRemove(pair.Key, out _);
        }
        else if (item.Method == "Target.targetCrashed") crashed[item.Parameters.GetProperty("targetId").GetString()!] = 0;
    }
    private void Upsert(JsonElement info)
    {
        if (info.GetProperty("type").GetString() != "page") return;
        var id = info.GetProperty("targetId").GetString()!;
        targets.AddOrUpdate(id, _ => Make(new TargetKey(Guid.NewGuid().ToString("N"))), (_, old) => Make(old.Key));
        TargetEntry Make(TargetKey key) => new(key, id, info.GetProperty("url").GetString()!, info.GetProperty("title").GetString()!, info.TryGetProperty("openerId", out var opener) ? opener.GetString() : null);
    }
    public async Task RefreshAsync(CancellationToken token)
    {
        long barrier;
        lock (registrySync) barrier = eventRevision;
        var result = await BrowserAsync("Target.getTargets", null, token);
        lock (registrySync)
        {
            var seen = new HashSet<string>();
            foreach (var info in result.GetProperty("targetInfos").EnumerateArray())
            {
                var id = info.GetProperty("targetId").GetString()!; seen.Add(id);
                if (!revisions.TryGetValue(id, out var revision) || revision <= barrier) Upsert(info);
            }
            foreach (var id in targets.Keys.Where(x => !seen.Contains(x)))
                if (!revisions.TryGetValue(id, out var revision) || revision <= barrier) { targets.TryRemove(id, out _); sessions.TryRemove(id, out _); }
            foreach (var pair in revisions.Where(x => x.Value <= barrier && !targets.ContainsKey(x.Key)).ToArray()) revisions.Remove(pair.Key);
        }
    }
    public IReadOnlyList<BrowserTarget> Snapshot() => targets.Values.Select(x => new BrowserTarget(x.Key, x.Url, x.Title, x.Opener, x.Key == Controlled)).ToArray();
    public TargetEntry Resolve(TargetKey? key = null)
    {
        var selected = key ?? Controlled ?? throw new AmbiguousTargetException();
        var result = targets.Values.SingleOrDefault(x => x.Key == selected) ?? throw new UcException(ErrorCategory.TargetClosed, "The selected target no longer exists.");
        if (crashed.ContainsKey(result.Id)) throw new UcException(ErrorCategory.TargetCrashed, "The selected target crashed.");
        return result;
    }
    private void SelectById(string id, bool explicitSelection) { Controlled = targets[id].Key; ExplicitSelection = explicitSelection; }
    public async Task SelectAsync(TargetKey key, CancellationToken token)
    {
        var target = Resolve(key);
        await SessionAsync(key, token);
        await BrowserAsync("Target.activateTarget", new { targetId = target.Id }, token);
        Controlled = key; ExplicitSelection = true;
    }
    public async Task<PageSession> SessionAsync(TargetKey? key, CancellationToken token)
    {
        var target = Resolve(key);
        if (sessions.TryGetValue(target.Id, out var existing)) return new(target, existing);
        var result = await BrowserAsync("Target.attachToTarget", new { targetId = target.Id, flatten = true }, token);
        var session = result.GetProperty("sessionId").GetString()!;
        await connection.SendAsync("Page.enable", null, session, token);
        await connection.SendAsync("Runtime.enable", null, session, token);
        await connection.SendAsync("Page.setLifecycleEventsEnabled", new { enabled = true }, session, token);
        await ApplyScriptsAsync(session, token);
        sessions[target.Id] = session;
        return new(target, session);
    }
    public async Task PrepareControlledAsync(CancellationToken token)
    {
        var session = await SessionAsync(Controlled, token);
        await ApplyScriptsAsync(session.SessionId, token);
    }
    private async Task ApplyScriptsAsync(string session, CancellationToken token)
    {
        var registeredScripts = scripts.ToList();
        if (removeDiscoveredCdcProperties)
        {
            const string discovery = "(() => { const names = new Set(); for(let p=globalThis,n=0;p&&n<8;p=Object.getPrototypeOf(p),n++) for(const k of Object.getOwnPropertyNames(p)) if(/^[a-z]{3}_[a-zA-Z0-9]{22}_(Array|Promise|Symbol|Object|Proxy|JSON|Window)$/.test(k)) names.add(k); return [...names].sort(); })()";
            var found = await connection.SendAsync("Runtime.evaluate", new { expression = discovery, returnByValue = true }, session, token);
            if (found.GetProperty("result").TryGetProperty("value", out var names) && names.ValueKind == JsonValueKind.Array)
            {
                var verified = names.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^[a-z]{3}_[a-zA-Z0-9]{22}_(Array|Promise|Symbol|Object|Proxy|JSON|Window)$")).Distinct().Order(StringComparer.Ordinal).ToArray();
                if (verified.Length > 0) registeredScripts.Add($"(() => {{ const names = {JsonSerializer.Serialize(verified)}; for(let p=globalThis,n=0;p&&n<8;p=Object.getPrototypeOf(p),n++) for(const k of names) {{ try {{ delete p[k]; }} catch {{}} }} }})()");
            }
        }
        if (!registered.TryGetValue(session, out var installed)) registered[session] = installed = new(StringComparer.Ordinal);
        foreach (var script in registeredScripts.Distinct(StringComparer.Ordinal))
            if (!installed.Contains(script)) { await connection.SendAsync("Page.addScriptToEvaluateOnNewDocument", new { source = script }, session, token); installed.Add(script); }
    }
    public Task<JsonElement> BrowserAsync(string method, object? args, CancellationToken token) => connection.SendAsync(method, args, null, token);
    public async Task<JsonElement> PageAsync(string method, object? args, TargetKey? target, CancellationToken token)
    {
        var session = await SessionAsync(target, token);
        return await connection.SendAsync(method, args, session.SessionId, token);
    }
    public async Task ReplaceAsync(CancellationToken token)
    {
        var old = Resolve();
        var result = await BrowserAsync("Target.createTarget", new { url = "about:blank" }, token);
        var id = result.GetProperty("targetId").GetString()!;
        await RefreshAsync(token);
        SelectById(id, true);
        await SessionAsync(Controlled, token);
        await BrowserAsync("Target.closeTarget", new { targetId = old.Id }, token);
    }
    public async Task RecoverAsync(CancellationToken token)
    {
        connection.Event -= OnEvent;
        await connection.DisposeAsync();
        connection = new(); sessions.Clear(); registered.Clear();
        await InitializeAsync(token);
        if (Controlled is not null) await SessionAsync(Controlled, token);
    }
    public async Task<PageResult> NavigateAsync(Uri url, NavigationOptions options, Func<CancellationToken, Task>? standard, CancellationToken token)
    {
        var session = await SessionAsync(options.Target ?? Controlled, token);
        await connection.SendAsync("Network.enable", null, session.SessionId, token);
        var tree = await connection.SendAsync("Page.getFrameTree", null, session.SessionId, token);
        var frame = tree.GetProperty("frameTree").GetProperty("frame");
        var frameId = frame.GetProperty("id").GetString();
        var oldLoader = frame.GetProperty("loaderId").GetString();
        var queue = Channel.CreateBounded<CdpEvent>(new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.Wait });
        var overflow = false;
        void Observe(CdpEvent item)
        {
            if (item.SessionId == session.SessionId || item.Method.StartsWith("Target.", StringComparison.Ordinal) || item.Method == "Browser.downloadWillBegin")
                if (!queue.Writer.TryWrite(item)) { overflow = true; queue.Writer.TryComplete(new UcException(ErrorCategory.ProtocolError, "Navigation event queue overflow.")); }
        }
        connection.Event += Observe;
        var redirects = new List<string>();
        var outstanding = new HashSet<string>();
        var lastActivity = DateTimeOffset.UtcNow;
        string? loader = null;
        var committed = false; var dom = false; var loaded = false;
        try
        {
            if (standard is null)
            {
                var result = await connection.SendAsync("Page.navigate", new { url = url.AbsoluteUri }, session.SessionId, token);
                if (result.TryGetProperty("isDownload", out var download) && download.GetBoolean()) return new(url, redirects, NavigationOutcome.Download);
                if (result.TryGetProperty("errorText", out _)) throw new UcException(ErrorCategory.ProtocolError, "Chrome rejected navigation.") { BrowserMayHaveAdvanced = true };
                loader = result.TryGetProperty("loaderId", out var value) ? value.GetString() : null;
            }
            else await standard(token);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (overflow) throw new UcException(ErrorCategory.ProtocolError, "Navigation event queue overflow.");
                if (!Healthy) throw new UcException(ErrorCategory.DevToolsEndpointFailure, "CDP connection dropped during navigation.") { BrowserMayHaveAdvanced = true };
                Resolve(session.Target.Key);
                while (queue.Reader.TryRead(out var item))
                {
                    var p = item.Parameters;
                    if (item.Method == "Browser.downloadWillBegin" && p.GetProperty("frameId").GetString() == frameId) return new(url, redirects, NavigationOutcome.Download);
                    if (item.Method == "Inspector.targetCrashed") throw new UcException(ErrorCategory.TargetCrashed, "Page crashed during navigation.");
                    if (item.Method == "Page.frameNavigated")
                    {
                        var f = p.GetProperty("frame");
                        if (f.GetProperty("id").GetString() == frameId && f.GetProperty("loaderId").GetString() != oldLoader)
                        { loader ??= f.GetProperty("loaderId").GetString(); committed = true; }
                    }
                    if (item.Method == "Page.navigatedWithinDocument" && p.GetProperty("frameId").GetString() == frameId) { committed = dom = loaded = true; }
                    if (item.Method == "Page.lifecycleEvent" && p.GetProperty("frameId").GetString() == frameId)
                    {
                        var eventLoader = p.GetProperty("loaderId").GetString();
                        if (eventLoader != oldLoader) loader ??= eventLoader;
                        if (eventLoader == loader)
                        {
                            var name = p.GetProperty("name").GetString();
                            if (name == "DOMContentLoaded") dom = committed = true;
                            if (name == "load") loaded = dom = committed = true;
                        }
                    }
                    if (item.Method == "Network.requestWillBeSent")
                    {
                        var requestId = p.GetProperty("requestId").GetString()!;
                        outstanding.Add(requestId); lastActivity = DateTimeOffset.UtcNow;
                        if (p.TryGetProperty("frameId", out var rf) && rf.GetString() == frameId && p.TryGetProperty("type", out var type) && type.GetString() == "Document" && p.TryGetProperty("redirectResponse", out var redirect)) redirects.Add(redirect.GetProperty("url").GetString()!);
                    }
                    if (item.Method is "Network.loadingFinished" or "Network.loadingFailed") { outstanding.Remove(p.GetProperty("requestId").GetString()!); lastActivity = DateTimeOffset.UtcNow; }
                }
                var complete = options.WaitUntil switch
                {
                    NavigationWaitUntil.Commit => committed,
                    NavigationWaitUntil.DOMContentLoaded => dom,
                    NavigationWaitUntil.Load => loaded,
                    NavigationWaitUntil.NetworkIdle => loaded && outstanding.Count == 0 && DateTimeOffset.UtcNow - lastActivity >= TimeSpan.FromMilliseconds(500),
                    _ => false
                };
                if (complete)
                {
                    var location = await connection.SendAsync("Runtime.evaluate", new { expression = "location.href", returnByValue = true }, session.SessionId, token);
                    return new(new Uri(location.GetProperty("result").GetProperty("value").GetString()!), redirects, NavigationOutcome.Completed);
                }
                await Task.Delay(20, token);
            }
        }
        finally { connection.Event -= Observe; }
    }
    public async ValueTask DisposeAsync() { connection.Event -= OnEvent; await connection.DisposeAsync(); }
}
