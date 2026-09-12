using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using BrowserDock.Cdp;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class NavigationContractTests
{
    [TestCase(NavigationWaitUntil.Commit), TestCase(NavigationWaitUntil.DOMContentLoaded), TestCase(NavigationWaitUntil.Load), TestCase(NavigationWaitUntil.NetworkIdle)]
    public async Task PageNavigationUsesTargetSessionAndWaitsForItsLoader(NavigationWaitUntil until)
    {
        await using var fixture = await ProtocolFixture.StartAsync();
        await using var controller = new CdpController(fixture.Endpoint, ["globalThis.fixture=true"]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(timeout.Token);
        var result = await controller.NavigateAsync(new Uri("http://fixture/page"), new() { WaitUntil = until }, null, timeout.Token);
        Assert.That(result.Url.AbsoluteUri, Is.EqualTo("http://fixture/page"));
        Assert.That(fixture.PageCallsWithoutSession, Is.Zero);
        Assert.That(fixture.ScriptRegistrations, Is.EqualTo(1));
    }
    [Test]
    public async Task SocketRecoveryPreservesTargetKeyAndReinstallsScripts()
    {
        await using var fixture = await ProtocolFixture.StartAsync();
        await using var controller = new CdpController(fixture.Endpoint, ["globalThis.fixture=true"]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(timeout.Token);
        var target = controller.Controlled;
        controller.InterruptForTest();
        await controller.RecoverAsync(timeout.Token);
        Assert.That(controller.Controlled, Is.EqualTo(target));
        Assert.That(fixture.ScriptRegistrations, Is.EqualTo(2));
        await controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, timeout.Token);
    }
    [Test]
    public async Task OldLoaderEventsCannotCompleteNewNavigation()
    {
        await using var fixture = await ProtocolFixture.StartAsync(); fixture.OnlyOldLoader = true;
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var initialization = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(initialization.Token);
        using var canceled = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        Assert.CatchAsync<OperationCanceledException>(async () => await controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, canceled.Token));
    }
    [TestCase("download"), TestCase("same-document"), TestCase("target-closed")]
    public async Task NavigationTerminalCasesAreDistinguished(string scenario)
    {
        await using var fixture = await ProtocolFixture.StartAsync(); fixture.Scenario = scenario;
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(timeout.Token);
        if (scenario == "target-closed")
            Assert.That(Assert.ThrowsAsync<BrowserDockException>(async () => await controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, timeout.Token))!.Category, Is.EqualTo(ErrorCategory.TargetClosed));
        else
        {
            var result = await controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, timeout.Token);
            Assert.That(result.Outcome, Is.EqualTo(scenario == "download" ? NavigationOutcome.Download : NavigationOutcome.Completed));
        }
    }
    [TestCase("pending-network"), TestCase("wrong-session")]
    public async Task UnfinishedNetworkOrAnotherPageCannotCompleteNavigation(string scenario)
    {
        await using var fixture = await ProtocolFixture.StartAsync(); fixture.Scenario = scenario;
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(setup.Token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
        Assert.CatchAsync<OperationCanceledException>(async () => await controller.NavigateAsync(new Uri("http://fixture/page"),
            new() { WaitUntil = scenario == "pending-network" ? NavigationWaitUntil.NetworkIdle : NavigationWaitUntil.Load }, null, deadline.Token));
        Assert.That(controller.ResourcesForTest.Pending, Is.Zero);
        Assert.That(controller.ResourcesForTest.Subscribers, Is.EqualTo(1), "Canceled navigation left an event subscription.");
        fixture.Scenario = null;
        await controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, setup.Token);
    }
    [TestCase("network-finished"), TestCase("network-failed")]
    public async Task FinishedAndFailedRequestsBothPermitNetworkIdle(string scenario)
    {
        await using var fixture = await ProtocolFixture.StartAsync(); fixture.Scenario = scenario;
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(deadline.Token);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await controller.NavigateAsync(new Uri("http://fixture/page"), new() { WaitUntil = NavigationWaitUntil.NetworkIdle }, null, deadline.Token);
        Assert.That(timer.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500)));
    }
    [Test]
    public async Task RepeatedNavigationAndDisposeReleaseWorkersRequestsAndSubscribers()
    {
        await using var fixture = await ProtocolFixture.StartAsync();
        var controller = new CdpController(fixture.Endpoint, ["globalThis.fixture=true", "globalThis.fixture=true"]);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await controller.InitializeAsync(deadline.Token);
            for (var i = 0; i < 20; i++)
            {
                await controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, deadline.Token);
                Assert.That(controller.ResourcesForTest.Subscribers, Is.EqualTo(1));
                Assert.That(controller.ResourcesForTest.Pending, Is.Zero);
            }
            Assert.That(fixture.ScriptRegistrations, Is.EqualTo(1));
        }
        finally { await controller.DisposeAsync(); }
        Assert.That(controller.ResourcesForTest, Is.EqualTo((0, 0, 0, false)));
    }
    [TestCase(false), TestCase(true)]
    public async Task CdcDiscoveryIsOptInValidatedAndDeduplicatedAcrossRecovery(bool enabled)
    {
        await using var fixture = await ProtocolFixture.StartAsync();
        await using var controller = new CdpController(fixture.Endpoint, ["globalThis.fixture=true", "globalThis.fixture=true"], enabled);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(deadline.Token);
        await controller.PrepareControlledAsync(deadline.Token);
        var perSession = enabled ? 2 : 1;
        Assert.That(fixture.ScriptRegistrations, Is.EqualTo(perSession));
        if (enabled)
        {
            var removal = fixture.Sources.Single(x => x.Contains("const names =", StringComparison.Ordinal));
            Assert.That(removal, Does.Contain("cdc_abcdefghijklmnopqrstuv_Array"));
            Assert.That(removal, Does.Not.Contain("location").And.Not.Contain("untrusted"));
        }
        controller.InterruptForTest();
        await controller.RecoverAsync(deadline.Token);
        Assert.That(fixture.ScriptRegistrations, Is.EqualTo(2 * perSession));
    }
    private sealed class ProtocolFixture : IAsyncDisposable
    {
        private LocalServer server = null!;
        public Uri Endpoint => new UriBuilder(server.Url) { Scheme = "ws", Path = "/devtools/browser/fixture" }.Uri;
        public int PageCallsWithoutSession;
        public int ScriptRegistrations;
        public System.Collections.Concurrent.ConcurrentBag<string> Sources = new();
        public bool OnlyOldLoader;
        public string? Scenario;
        public static async Task<ProtocolFixture> StartAsync()
        {
            var fixture = new ProtocolFixture(); fixture.server = await LocalServer.StartAsync(fixture.HandleAsync); return fixture;
        }
        private async Task HandleAsync(HttpContext context)
        {
            if (context.Request.Path == "/json/protocol")
            {
                var names = new Dictionary<string, string[]>
                {
                    ["Browser"] = ["getVersion", "close", "setDownloadBehavior"], ["Target"] = ["setDiscoverTargets", "getTargets", "attachToTarget", "activateTarget", "createTarget", "closeTarget"],
                    ["Page"] = ["enable", "navigate", "getFrameTree", "setLifecycleEventsEnabled", "addScriptToEvaluateOnNewDocument"], ["Runtime"] = ["enable", "evaluate", "callFunctionOn"], ["Network"] = ["enable"]
                };
                await context.Response.WriteAsJsonAsync(new { domains = names.Select(pair => new { domain = pair.Key, commands = pair.Value.Select(name => new { name }) }) }); return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            try
            {
                var bytes = new byte[16384];
                while (!context.RequestAborted.IsCancellationRequested)
                {
                    using var stream = new MemoryStream(); WebSocketReceiveResult message;
                    do { message = await socket.ReceiveAsync(bytes, context.RequestAborted); if (message.MessageType == WebSocketMessageType.Close) return; stream.Write(bytes, 0, message.Count); } while (!message.EndOfMessage);
                    using var document = JsonDocument.Parse(stream.ToArray());
                    var root = document.RootElement; var method = root.GetProperty("method").GetString()!;
                    if ((method.StartsWith("Page.", StringComparison.Ordinal) || method.StartsWith("Runtime.", StringComparison.Ordinal)) && !root.TryGetProperty("sessionId", out _)) Interlocked.Increment(ref PageCallsWithoutSession);
                    object result = method switch
                    {
                        "Browser.getVersion" => new { product = "Chrome/150.0.1.0", protocolVersion = "1.3" },
                        "Target.getTargets" => new { targetInfos = new[] { new { targetId = "page", type = "page", url = "about:blank", title = "fixture" } } },
                        "Target.attachToTarget" => new { sessionId = "session-page" },
                        "Page.getFrameTree" => new { frameTree = new { frame = new { id = "main", loaderId = "old-loader", url = "about:blank" } } },
                        "Page.navigate" => new { frameId = "main", loaderId = "new-loader" },
                        "Runtime.evaluate" => new { result = new { type = "string", value = "http://fixture/page" } },
                        _ => new { }
                    };
                    if (method == "Runtime.evaluate" && root.GetProperty("params").GetProperty("expression").GetString()!.Contains("const names = new Set()", StringComparison.Ordinal))
                        result = new { result = new { value = new[] { "cdc_abcdefghijklmnopqrstuv_Array", "location", "untrusted');throw 1;//", "cdc_abcdefghijklmnopqrstuv_Array" } } };
                    if (method == "Page.addScriptToEvaluateOnNewDocument")
                    {
                        Interlocked.Increment(ref ScriptRegistrations);
                        Sources.Add(root.GetProperty("params").GetProperty("source").GetString()!);
                    }
                    if (method == "Page.navigate" && Scenario == "download") result = new { frameId = "main", isDownload = true };
                    if (method == "Page.navigate" && Scenario == "same-document") result = new { frameId = "main" };
                    await Send(new { id = root.GetProperty("id").GetInt64(), result });
                    if (method == "Page.navigate")
                    {
                        if (Scenario == "target-closed") { await Send(new { method = "Target.targetDestroyed", @params = new { targetId = "page" } }); continue; }
                        if (Scenario == "same-document") { await Send(new { method = "Page.navigatedWithinDocument", sessionId = "session-page", @params = new { frameId = "main", url = "http://fixture/page" } }); continue; }
                        var loader = OnlyOldLoader ? "old-loader" : "new-loader";
                        var eventSession = Scenario == "wrong-session" ? "other-page" : "session-page";
                        await Send(new { method = "Page.frameNavigated", sessionId = eventSession, @params = new { frame = new { id = "main", loaderId = loader, url = "http://fixture/page" } } });
                        if (Scenario is "pending-network" or "network-finished" or "network-failed")
                            await Send(new { method = "Network.requestWillBeSent", sessionId = eventSession, @params = new { requestId = "fetch-1" } });
                        foreach (var name in new[] { "DOMContentLoaded", "load" }) await Send(new { method = "Page.lifecycleEvent", sessionId = eventSession, @params = new { frameId = "main", loaderId = loader, name } });
                        if (Scenario is "network-finished" or "network-failed")
                            await Send(new { method = Scenario == "network-finished" ? "Network.loadingFinished" : "Network.loadingFailed", sessionId = eventSession, @params = new { requestId = "fetch-1" } });
                    }
                }
                async Task Send(object payload) => await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload), WebSocketMessageType.Text, true, context.RequestAborted);
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
        }
        public ValueTask DisposeAsync() => server.DisposeAsync();
    }
}
