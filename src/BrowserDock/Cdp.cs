using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using BrowserDock.Hosting;

namespace BrowserDock.Cdp;

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
    internal (int Pending, int Tasks, int Subscribers, bool SocketOpen) ResourcesForTest =>
        (pending.Count, (reader.IsCompleted ? 0 : 1) + (dispatcher.IsCompleted ? 0 : 1), Event?.GetInvocationList().Length ?? 0, socket.State == WebSocketState.Open);
    internal void InterruptForTest() => Fail(new IOException("Injected socket interruption."));
    public async Task ConnectAsync(Uri endpoint, CancellationToken token)
    {
        if (endpoint.Scheme != "ws" || !endpoint.IsLoopback) throw new BrowserDockException(ErrorCategory.ConfigurationError, "CDP requires a loopback WebSocket endpoint.");
        socket.Options.Proxy = null;
        await socket.ConnectAsync(endpoint, token).ConfigureAwait(false);
        reader = ReadAsync();
        dispatcher = DispatchAsync();
    }
    public async Task<JsonElement> SendAsync(string method, object? parameters, string? session, CancellationToken token)
    {
        if (!Healthy) throw new BrowserDockException(ErrorCategory.DevToolsEndpointFailure, "CDP connection is unavailable.");
        var id = Interlocked.Increment(ref sequence);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            using var cancellation = token.Register(() => completion.TrySetCanceled(token));
            var payload = new Dictionary<string, object?> { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new { } };
            if (session is not null) payload["sessionId"] = session;
            await sendGate.WaitAsync(token).ConfigureAwait(false);
            try { await socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(payload)), WebSocketMessageType.Text, true, token).ConfigureAwait(false); }
            finally { sendGate.Release(); }
            return await completion.Task.ConfigureAwait(false);
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
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), lifetime.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("CDP peer closed.");
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > 16 * 1024 * 1024) throw new BrowserDockException(ErrorCategory.ProtocolError, "CDP response exceeds the 16 MiB limit.");
                } while (!result.EndOfMessage);
                using var json = JsonDocument.Parse(message.ToArray());
                var root = json.RootElement;
                if (root.TryGetProperty("id", out var id))
                {
                    if (!pending.TryGetValue(id.GetInt64(), out var request)) continue;
                    if (root.TryGetProperty("error", out var error)) request.TrySetException(new BrowserDockException(ErrorCategory.ProtocolError, $"CDP command failed ({error.GetProperty("code").GetInt32()})."));
                    else request.TrySetResult(root.GetProperty("result").Clone());
                }
                else if (root.TryGetProperty("method", out var method))
                {
                    var item = new CdpEvent(method.GetString()!, root.TryGetProperty("sessionId", out var session) ? session.GetString() : null, root.GetProperty("params").Clone());
                    if (!events.Writer.TryWrite(item)) throw new BrowserDockException(ErrorCategory.ProtocolError, "CDP lifecycle event buffer overflow; connection must be recovered.");
                }
            }
        }
        catch (Exception e) { Fail(e); }
        finally { events.Writer.TryComplete(); }
    }
    private async Task DispatchAsync()
    {
        try
        {
            while (await events.Reader.WaitToReadAsync(lifetime.Token).ConfigureAwait(false))
                while (events.Reader.TryRead(out var item)) Event?.Invoke(item);
        }
        catch (Exception e) { Fail(e); }
    }
    private void Fail(Exception error)
    {
        lifetime.Cancel(); socket.Abort();
        foreach (var request in pending.Values) request.TrySetException(new BrowserDockException(ErrorCategory.DevToolsEndpointFailure, "CDP transport was interrupted.", error));
    }
    public async ValueTask DisposeAsync()
    {
        Fail(new ObjectDisposedException(nameof(CdpConnection)));
        await Task.WhenAll(reader, dispatcher).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
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
    internal (int Pending, int Tasks, int Subscribers, bool SocketOpen) ResourcesForTest => connection.ResourcesForTest;
    internal void InterruptForTest() => connection.InterruptForTest();
    public TargetKey? Controlled { get; private set; }
    public bool ExplicitSelection { get; private set; }
    public async Task InitializeAsync(CancellationToken token)
    {
        await connection.ConnectAsync(endpoint, token).ConfigureAwait(false);
        connection.Event += OnEvent;
        await BrowserAsync("Browser.getVersion", null, token).ConfigureAwait(false);
        await ValidateProtocolAsync(token).ConfigureAwait(false);
        await BrowserAsync("Browser.setDownloadBehavior", new { behavior = "default", eventsEnabled = true }, token).ConfigureAwait(false);
        await BrowserAsync("Target.setDiscoverTargets", new { discover = true }, token).ConfigureAwait(false);
        await RefreshAsync(token).ConfigureAwait(false);
        // Chrome is launched with about:blank, but its first page may appear
        // after the DevTools endpoint. Creating a fallback here races that page.
        // The caller's CDP deadline bounds discovery; recovery keeps its target.
        while (Controlled is null && targets.IsEmpty)
        {
            await Task.Delay(50, token).ConfigureAwait(false);
            await RefreshAsync(token).ConfigureAwait(false);
        }
        lock (registrySync)
            if (Controlled is null && targets.Count == 1) SelectById(targets.Values.Single().Id, false);
        if (Controlled is not null) await SessionAsync(Controlled.Value, token).ConfigureAwait(false);
    }
    private async Task ValidateProtocolAsync(CancellationToken token)
    {
        using var http = BrowserHosting.Http();
        using var protocol = JsonDocument.Parse(await RuntimeCompatibility.HttpStringAsync(http, $"http://127.0.0.1:{endpoint.Port}/json/protocol", token).ConfigureAwait(false));
        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var domain in protocol.RootElement.GetProperty("domains").EnumerateArray())
            if (domain.TryGetProperty("commands", out var commands))
                foreach (var command in commands.EnumerateArray()) available.Add(domain.GetProperty("domain").GetString() + "." + command.GetProperty("name").GetString());
        foreach (var method in new[] { "Browser.getVersion", "Browser.close", "Browser.setDownloadBehavior", "Target.setDiscoverTargets", "Target.getTargets", "Target.attachToTarget", "Target.activateTarget", "Target.createTarget", "Target.closeTarget", "Page.enable", "Page.navigate", "Page.getFrameTree", "Page.setLifecycleEventsEnabled", "Page.addScriptToEvaluateOnNewDocument", "Runtime.enable", "Runtime.evaluate", "Runtime.callFunctionOn", "Network.enable" })
            if (!available.Contains(method)) throw new BrowserDockException(ErrorCategory.ProtocolError, $"Required CDP method is missing: {method}.");
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
        else if (item.Method is "Target.detachedFromTarget" or "Inspector.detached")
        {
            var session = item.Method == "Inspector.detached" ? item.SessionId : item.Parameters.GetProperty("sessionId").GetString();
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
        var result = await BrowserAsync("Target.getTargets", null, token).ConfigureAwait(false);
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
        var result = targets.Values.SingleOrDefault(x => x.Key == selected) ?? throw new BrowserDockException(ErrorCategory.TargetClosed, "The selected target no longer exists.");
        if (crashed.ContainsKey(result.Id)) throw new BrowserDockException(ErrorCategory.TargetCrashed, "The selected target crashed.");
        return result;
    }
    private void SelectById(string id, bool explicitSelection) { Controlled = targets[id].Key; ExplicitSelection = explicitSelection; }
    public async Task SelectAsync(TargetKey key, CancellationToken token)
    {
        var target = Resolve(key);
        await SessionAsync(key, token).ConfigureAwait(false);
        await BrowserAsync("Target.activateTarget", new { targetId = target.Id }, token).ConfigureAwait(false);
        Controlled = key; ExplicitSelection = true;
    }
    public async Task<PageSession> SessionAsync(TargetKey? key, CancellationToken token)
    {
        var target = Resolve(key);
        if (sessions.TryGetValue(target.Id, out var existing)) return new(target, existing);
        var result = await BrowserAsync("Target.attachToTarget", new { targetId = target.Id, flatten = true }, token).ConfigureAwait(false);
        var session = result.GetProperty("sessionId").GetString()!;
        await connection.SendAsync("Page.enable", null, session, token).ConfigureAwait(false);
        await connection.SendAsync("Runtime.enable", null, session, token).ConfigureAwait(false);
        await connection.SendAsync("Page.setLifecycleEventsEnabled", new { enabled = true }, session, token).ConfigureAwait(false);
        await ApplyScriptsAsync(session, token).ConfigureAwait(false);
        sessions[target.Id] = session;
        return new(target, session);
    }
    public async Task PrepareControlledAsync(CancellationToken token)
    {
        var session = await SessionAsync(Controlled, token).ConfigureAwait(false);
        await ApplyScriptsAsync(session.SessionId, token).ConfigureAwait(false);
    }
    private async Task ApplyScriptsAsync(string session, CancellationToken token)
    {
        var registeredScripts = scripts.ToList();
        if (removeDiscoveredCdcProperties)
        {
            const string discovery = "(() => { const names = new Set(); for(let p=globalThis,n=0;p&&n<8;p=Object.getPrototypeOf(p),n++) for(const k of Object.getOwnPropertyNames(p)) if(/^[a-z]{3}_[a-zA-Z0-9]{22}_(Array|Promise|Symbol|Object|Proxy|JSON|Window)$/.test(k)) names.add(k); return [...names].sort(); })()";
            var found = await connection.SendAsync("Runtime.evaluate", new { expression = discovery, returnByValue = true }, session, token).ConfigureAwait(false);
            if (found.GetProperty("result").TryGetProperty("value", out var names) && names.ValueKind == JsonValueKind.Array)
            {
                var verified = names.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^[a-z]{3}_[a-zA-Z0-9]{22}_(Array|Promise|Symbol|Object|Proxy|JSON|Window)$")).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
                if (verified.Length > 0) registeredScripts.Add($"(() => {{ const names = {JsonSerializer.Serialize(verified)}; for(let p=globalThis,n=0;p&&n<8;p=Object.getPrototypeOf(p),n++) for(const k of names) {{ try {{ delete p[k]; }} catch {{}} }} }})()");
            }
        }
        if (!registered.TryGetValue(session, out var installed)) registered[session] = installed = new(StringComparer.Ordinal);
        foreach (var script in registeredScripts.Distinct(StringComparer.Ordinal))
            if (!installed.Contains(script)) { await connection.SendAsync("Page.addScriptToEvaluateOnNewDocument", new { source = script }, session, token).ConfigureAwait(false); installed.Add(script); }
    }
    public Task<JsonElement> BrowserAsync(string method, object? args, CancellationToken token) => connection.SendAsync(method, args, null, token);
    public async Task<JsonElement> PageAsync(string method, object? args, TargetKey? target, CancellationToken token)
    {
        var session = await SessionAsync(target, token).ConfigureAwait(false);
        return await connection.SendAsync(method, args, session.SessionId, token).ConfigureAwait(false);
    }
    public async Task ReplaceAsync(CancellationToken token)
    {
        var old = Resolve();
        var result = await BrowserAsync("Target.createTarget", new { url = "about:blank" }, token).ConfigureAwait(false);
        var id = result.GetProperty("targetId").GetString()!;
        await RefreshAsync(token).ConfigureAwait(false);
        SelectById(id, true);
        await SessionAsync(Controlled, token).ConfigureAwait(false);
        await BrowserAsync("Target.closeTarget", new { targetId = old.Id }, token).ConfigureAwait(false);
    }
    public async Task RecoverAsync(CancellationToken token)
    {
        connection.Event -= OnEvent;
        await connection.DisposeAsync().ConfigureAwait(false);
        connection = new(); sessions.Clear(); registered.Clear();
        await InitializeAsync(token).ConfigureAwait(false);
        if (Controlled is not null) await SessionAsync(Controlled, token).ConfigureAwait(false);
    }
    public async Task<PageResult> NavigateAsync(Uri url, NavigationOptions options, Func<CancellationToken, Task>? standard, CancellationToken token)
    {
        var stage = "target-session";
        PageSession? session = null;
        string? frameId = null, oldLoader = null;
        var observing = false;
        long received = 0;
        string? lastEvent = null;
        var trace = new BoundedDiagnosticTrace();
        var queue = Channel.CreateBounded<CdpEvent>(new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.Wait });
        var overflow = false;
        void Observe(CdpEvent item)
        {
            if (item.SessionId == session?.SessionId || item.Method.StartsWith("Target.", StringComparison.Ordinal) || item.Method == "Browser.downloadWillBegin")
            {
                Interlocked.Increment(ref received);
                Volatile.Write(ref lastEvent, item.Method);
                // Omit payload-heavy events; keep ordering of navigation and failure metadata.
                if (!item.Method.EndsWith("ExtraInfo", StringComparison.Ordinal) && item.Method != "Network.dataReceived")
                    trace.Cdp(item.Method, item.SessionId, item.Parameters);
                if (!queue.Writer.TryWrite(item)) { overflow = true; queue.Writer.TryComplete(new BrowserDockException(ErrorCategory.ProtocolError, "Navigation event queue overflow.")); }
            }
        }
        var redirects = new List<string>();
        var outstanding = new HashSet<string>();
        var lastActivity = DateTimeOffset.UtcNow;
        string? loader = null;
        string? documentRequest = null, documentFailure = null;
        var documentResponse = false;
        // W3C navigation returns no loader ID. Do not let a previous, still
        // provisional navigation claim this operation through its late events.
        var requireDocumentRequest = standard is not null && url.Scheme is "http" or "https";
        var committed = false; var dom = false; var loaded = false;
        try
        {
            session = await SessionAsync(options.Target ?? Controlled, token).ConfigureAwait(false);
            stage = "Network.enable";
            await connection.SendAsync("Network.enable", null, session.SessionId, token).ConfigureAwait(false);
            stage = "Page.getFrameTree";
            var tree = await connection.SendAsync("Page.getFrameTree", null, session.SessionId, token).ConfigureAwait(false);
            var frame = tree.GetProperty("frameTree").GetProperty("frame");
            frameId = frame.GetProperty("id").GetString();
            oldLoader = frame.GetProperty("loaderId").GetString();
            connection.Event += Observe; observing = true;
            trace.Add("navigation-dispatch", ("mode", options.Mode.ToString()));
            lastActivity = DateTimeOffset.UtcNow;
            if (standard is null)
            {
                stage = "Page.navigate";
                var result = await connection.SendAsync("Page.navigate", new { url = url.AbsoluteUri }, session.SessionId, token).ConfigureAwait(false);
                if (result.TryGetProperty("isDownload", out var download) && download.GetBoolean()) return new(url, redirects, NavigationOutcome.Download);
                if (result.TryGetProperty("errorText", out var rejection))
                {
                    var error = new BrowserDockException(ErrorCategory.ProtocolError, $"Chrome rejected navigation: {rejection.GetString()}") { BrowserMayHaveAdvanced = true };
                    error.Data[NavigationDiagnostics.Prefix + "CdpErrorText"] = rejection.GetString() ?? "";
                    throw error;
                }
                loader = result.TryGetProperty("loaderId", out var value) ? value.GetString() : null;
            }
            else { stage = "WebDriver.Navigate"; await standard(token).ConfigureAwait(false); }
            trace.Add("navigation-command-returned");
            stage = "navigation-events";
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (overflow) throw new BrowserDockException(ErrorCategory.ProtocolError, "Navigation event queue overflow.");
                if (!Healthy) throw new BrowserDockException(ErrorCategory.DevToolsEndpointFailure, "CDP connection dropped during navigation.") { BrowserMayHaveAdvanced = true };
                Resolve(session.Target.Key);
                while (queue.Reader.TryRead(out var item))
                {
                    var p = item.Parameters;
                    if (item.Method == "Browser.downloadWillBegin" && p.GetProperty("frameId").GetString() == frameId) return new(url, redirects, NavigationOutcome.Download);
                    if ((item.Method == "Target.detachedFromTarget" && BoundedDiagnosticTrace.Field(p, "sessionId") == session.SessionId) ||
                        item.Method == "Inspector.detached")
                        throw new BrowserDockException(ErrorCategory.DevToolsEndpointFailure, "The CDP session observing navigation was detached.") { BrowserMayHaveAdvanced = true };
                    if (item.Method == "Inspector.targetCrashed") throw new BrowserDockException(ErrorCategory.TargetCrashed, "Page crashed during navigation.");
                    if (item.Method == "Page.frameNavigated")
                    {
                        var f = p.GetProperty("frame");
                        if ((!requireDocumentRequest || documentRequest is not null) &&
                            f.GetProperty("id").GetString() == frameId && f.GetProperty("loaderId").GetString() != oldLoader &&
                            (loader is null || f.GetProperty("loaderId").GetString() == loader))
                        { loader ??= f.GetProperty("loaderId").GetString(); committed = true; }
                    }
                    if (item.Method == "Page.navigatedWithinDocument" && p.GetProperty("frameId").GetString() == frameId) { committed = dom = loaded = true; }
                    if (item.Method == "Page.lifecycleEvent" && p.GetProperty("frameId").GetString() == frameId &&
                        (!requireDocumentRequest || documentRequest is not null))
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
                        if (BoundedDiagnosticTrace.Field(p, "frameId") == frameId && BoundedDiagnosticTrace.Field(p, "type") == "Document" &&
                            BoundedDiagnosticTrace.Field(p, "loaderId") is { } requestLoader && requestLoader != oldLoader)
                        {
                            var redirected = p.TryGetProperty("redirectResponse", out var redirect);
                            var first = documentRequest is null && (standard is null
                                ? loader is null || loader == requestLoader
                                : !redirected && p.TryGetProperty("request", out var request) && MatchesNavigationRequest(url, request));
                            if (first || (documentRequest == requestId && loader == requestLoader))
                            {
                                loader = requestLoader;
                                documentRequest = requestId; documentResponse = false; documentFailure = null;
                                if (redirected) redirects.Add(redirect.GetProperty("url").GetString()!);
                            }
                        }
                    }
                    if (item.Method == "Network.responseReceived" && documentRequest is not null && BoundedDiagnosticTrace.Field(p, "requestId") == documentRequest)
                        documentResponse = true;
                    if (item.Method is "Network.loadingFinished" or "Network.loadingFailed") { outstanding.Remove(p.GetProperty("requestId").GetString()!); lastActivity = DateTimeOffset.UtcNow; }
                    if (item.Method == "Network.loadingFailed" && documentRequest is not null && BoundedDiagnosticTrace.Field(p, "requestId") == documentRequest)
                    {
                        documentFailure = BoundedDiagnosticTrace.Field(p, "errorText") ?? "Unknown document loading failure";
                        // Chrome also reports ERR_ABORTED after a download response, before
                        // Browser.downloadWillBegin. Preserve that event's full existing budget;
                        // do not introduce a timing guess or classify response MIME types.
                        if (documentFailure != "net::ERR_ABORTED" || !documentResponse)
                        {
                            var error = new BrowserDockException(ErrorCategory.ProtocolError, "Main document loading failed: " + documentFailure) { BrowserMayHaveAdvanced = true };
                            error.Data[NavigationDiagnostics.Prefix + "CdpErrorText"] = documentFailure;
                            throw error;
                        }
                    }
                }
                var complete = options.WaitUntil switch
                {
                    NavigationWaitUntil.Commit => committed,
                    NavigationWaitUntil.DOMContentLoaded => dom,
                    NavigationWaitUntil.Load => loaded,
                    NavigationWaitUntil.NetworkIdle => loaded && outstanding.Count == 0 && DateTimeOffset.UtcNow - lastActivity >= TimeSpan.FromMilliseconds(500),
                    _ => false
                };
                if (complete && documentFailure is null)
                {
                    stage = "Runtime.evaluate";
                    var location = await connection.SendAsync("Runtime.evaluate", new { expression = "location.href", returnByValue = true }, session.SessionId, token).ConfigureAwait(false);
                    return new(new Uri(location.GetProperty("result").GetProperty("value").GetString()!), redirects, NavigationOutcome.Completed);
                }
                await Task.Delay(20, token).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            trace.Add("navigation-failed", ("stage", stage), ("exception", error.GetType().Name));
            error.Data[NavigationDiagnostics.Prefix + "Trace"] = trace.Snapshot();
            error.Data[NavigationDiagnostics.Prefix + "DocumentRequestId"] = documentRequest ?? "";
            if (documentFailure is not null)
            {
                error.Data[NavigationDiagnostics.Prefix + "CdpErrorText"] = documentFailure;
                error.Data[NavigationDiagnostics.Prefix + "AwaitingDownload"] = (documentFailure == "net::ERR_ABORTED" && documentResponse).ToString();
            }
            NavigationDiagnostics.Record(error, stage, options);
            error.Data[NavigationDiagnostics.Prefix + "SessionId"] = session?.SessionId ?? "";
            error.Data[NavigationDiagnostics.Prefix + "TargetId"] = session?.Target.Id ?? "";
            error.Data[NavigationDiagnostics.Prefix + "FrameId"] = frameId ?? "";
            error.Data[NavigationDiagnostics.Prefix + "PreviousLoaderId"] = oldLoader ?? "";
            error.Data[NavigationDiagnostics.Prefix + "LoaderId"] = loader ?? "";
            error.Data[NavigationDiagnostics.Prefix + "EventsReceived"] = Interlocked.Read(ref received).ToString(System.Globalization.CultureInfo.InvariantCulture);
            error.Data[NavigationDiagnostics.Prefix + "LastEvent"] = Volatile.Read(ref lastEvent) ?? "";
            error.Data[NavigationDiagnostics.Prefix + "Committed"] = committed.ToString();
            error.Data[NavigationDiagnostics.Prefix + "DOMContentLoaded"] = dom.ToString();
            error.Data[NavigationDiagnostics.Prefix + "Loaded"] = loaded.ToString();
            error.Data[NavigationDiagnostics.Prefix + "OutstandingRequests"] = outstanding.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            throw;
        }
        finally { if (observing) connection.Event -= Observe; }
    }
    private static bool MatchesNavigationRequest(Uri destination, JsonElement request)
    {
        if (!Uri.TryCreate(BoundedDiagnosticTrace.Field(request, "url"), UriKind.Absolute, out var observed)) return false;
        // Network requests omit fragments and credentials. Keep path/query case
        // significant while normalizing host names and default ports via Uri.
        return destination.Scheme == observed.Scheme && destination.Port == observed.Port &&
            string.Equals(destination.IdnHost, observed.IdnHost, StringComparison.OrdinalIgnoreCase) &&
            destination.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped) == observed.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped);
    }
    public async ValueTask DisposeAsync() { connection.Event -= OnEvent; await connection.DisposeAsync().ConfigureAwait(false); }
}
