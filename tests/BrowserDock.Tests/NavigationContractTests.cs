using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using BrowserDock.Cdp;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class NavigationContractTests
{
    [TestCase("document-failed", "net::ERR_CONNECTION_RESET")]
    [TestCase("document-aborted", "net::ERR_ABORTED")]
    [TestCase("redirect-failed", "net::ERR_CONNECTION_RESET")]
    public async Task StandardDocumentFailureReturnsItsCauseWithoutWaitingForTheNavigationDeadline(string scenario, string reason)
    {
        await using var fixture = await ProtocolFixture.StartAsync(); fixture.Scenario = scenario;
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(deadline.Token);
        var error = Assert.ThrowsAsync<BrowserDockException>(async () => await controller.NavigateAsync(new Uri("http://fixture/page"), new(),
            async token => { await controller.PageAsync("Page.navigate", new { url = "http://fixture/page" }, null, token); }, deadline.Token));
        Assert.That(error!.Category, Is.EqualTo(ErrorCategory.ProtocolError));
        Assert.That(error.Data["Navigation.CdpErrorText"], Is.EqualTo(reason));
        Assert.That(error.Data["Navigation.DocumentRequestId"], Is.EqualTo("document"));
        Assert.That(error.Data["Navigation.Trace"], Does.Contain("Network.loadingFailed"));
        Assert.That(deadline.IsCancellationRequested, Is.False);
        Assert.That(controller.ResourcesForTest.Pending, Is.Zero);
        Assert.That(controller.ResourcesForTest.Subscribers, Is.EqualTo(1));
    }

    [TestCase("session-detached"), TestCase("inspector-detached")]
    public async Task DetachedObserverFailsEvenWhenTheBrowserAndTargetRemainAlive(string scenario)
    {
        await using var fixture = await ProtocolFixture.StartAsync(); fixture.Scenario = scenario;
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(deadline.Token);
        var error = Assert.ThrowsAsync<BrowserDockException>(async () => await controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, deadline.Token));
        Assert.That(error!.Category, Is.EqualTo(ErrorCategory.DevToolsEndpointFailure));
        Assert.That(controller.Healthy, Is.True);
        Assert.That(controller.Resolve().Id, Is.EqualTo("page"));
        Assert.That(deadline.IsCancellationRequested, Is.False);
        Assert.That(controller.ResourcesForTest.Subscribers, Is.EqualTo(1));
        fixture.Scenario = null;
        var result = await controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, deadline.Token);
        Assert.That(result.Outcome, Is.EqualTo(NavigationOutcome.Completed));
        Assert.That(fixture.AttachRequests, Is.EqualTo(2), "A subsequent operation must not reuse the detached observer session.");
    }

    [TestCase("other-frame-failed"), TestCase("old-loader-failed"), TestCase("other-session-failed"), TestCase("other-session-detached")]
    public async Task UnrelatedFailuresDoNotAbortTheControlledNavigation(string scenario)
    {
        await using var fixture = await ProtocolFixture.StartAsync(); fixture.Scenario = scenario;
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(deadline.Token);
        var result = await controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, deadline.Token);
        Assert.That(result.Outcome, Is.EqualTo(NavigationOutcome.Completed));
    }

    [Test]
    public async Task ResponseAbortWaitsForALaterDownloadEvent()
    {
        await using var fixture = await ProtocolFixture.StartAsync(); fixture.Scenario = "response-aborted-download";
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(deadline.Token);
        var result = await controller.NavigateAsync(new Uri("http://fixture/download"), new(),
            async token => { await controller.PageAsync("Page.navigate", new { url = "http://fixture/download" }, null, token); }, deadline.Token);
        Assert.That(result.Outcome, Is.EqualTo(NavigationOutcome.Download));
    }

    [Test]
    public async Task AmbiguousResponseAbortPreservesCancellationAndTheObservedFailure()
    {
        await using var fixture = await ProtocolFixture.StartAsync(); fixture.Scenario = "response-aborted-no-download";
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(setup.Token);
        using var canceled = new CancellationTokenSource();
        var pending = controller.NavigateAsync(new Uri("http://fixture/download"), new(),
            async token =>
            {
                await controller.PageAsync("Page.navigate", new { url = "http://fixture/download" }, null, token);
                await fixture.FailureSent.Task.WaitAsync(setup.Token);
                await controller.BrowserAsync("Browser.getVersion", null, setup.Token);
                canceled.CancelAfter(TimeSpan.FromSeconds(1));
            }, canceled.Token);
        try
        {
            var error = Assert.CatchAsync<OperationCanceledException>(async () => await pending.WaitAsync(setup.Token))!;
            Assert.That(error.CancellationToken, Is.EqualTo(canceled.Token));
            Assert.That(error.Data["Navigation.CdpErrorText"], Is.EqualTo("net::ERR_ABORTED"));
            Assert.That(error.Data["Navigation.AwaitingDownload"], Is.EqualTo("True"));
        }
        finally { canceled.Cancel(); }
    }
    [TestCase(1), TestCase(3)]
    public async Task InitializationWaitsForInitialPageWithoutCreatingAnother(int emptySnapshots)
    {
        await using var fixture = await ProtocolFixture.StartAsync();
        fixture.EmptySnapshotsRemaining = emptySnapshots;
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(deadline.Token);
        Assert.That(fixture.CreatedTargets, Is.Zero, "The initial Chrome page must not race a fallback tab.");
        Assert.That(fixture.DiscoveryRequests, Is.EqualTo(emptySnapshots + 1));
        Assert.That(controller.Snapshot(), Has.Count.EqualTo(1));
        Assert.That(controller.Resolve().Id, Is.EqualTo("page"));
        Assert.That(controller.ExplicitSelection, Is.False);
    }
    [Test]
    public async Task InitializationWaitingForInitialPageHonorsCancellation()
    {
        await using var fixture = await ProtocolFixture.StartAsync();
        fixture.EmptySnapshotsRemaining = int.MaxValue;
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var initializing = controller.InitializeAsync(deadline.Token);
        await fixture.EmptySnapshotRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        deadline.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await initializing.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.That(fixture.CreatedTargets, Is.Zero);
        Assert.That(controller.Controlled, Is.Null);
        Assert.That(controller.ResourcesForTest.Pending, Is.Zero);
    }
    [Test]
    public async Task InitializationDoesNotChooseAmongMultipleInitialPages()
    {
        await using var fixture = await ProtocolFixture.StartAsync();
        fixture.MultipleInitialPages = true;
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(deadline.Token);
        Assert.That(controller.Snapshot(), Has.Count.EqualTo(2));
        Assert.That(controller.Controlled, Is.Null);
        Assert.Throws<AmbiguousTargetException>(() => controller.Resolve());
        Assert.That(fixture.CreatedTargets, Is.Zero);
    }
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
    [TestCase("net::ERR_ABORTED"), TestCase("net::ERR_CONNECTION_REFUSED")]
    public async Task NavigationRejectionPreservesChromeErrorAndSessionContext(string errorText)
    {
        await using var fixture = await ProtocolFixture.StartAsync();
        fixture.NavigationError = errorText;
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await controller.InitializeAsync(setup.Token);
        var error = Assert.ThrowsAsync<BrowserDockException>(async () =>
            await controller.NavigateAsync(new Uri("http://fixture/page"), new() { Mode = NavigationMode.Detached }, null, setup.Token))!;
        Assert.Multiple(() =>
        {
            Assert.That(error.Category, Is.EqualTo(ErrorCategory.ProtocolError));
            Assert.That(error.Message, Does.Contain(errorText));
            Assert.That(error.Data["Navigation.CdpErrorText"], Is.EqualTo(errorText));
            Assert.That(error.Data["Navigation.Stage"], Is.EqualTo("Page.navigate"));
            Assert.That(error.Data["Navigation.Mode"], Is.EqualTo("Detached"));
            Assert.That(error.Data["Navigation.SessionId"], Is.EqualTo("session-page"));
            Assert.That(error.Data["Navigation.TargetId"], Is.EqualTo("page"));
            Assert.That(error.Data["Navigation.FrameId"], Is.EqualTo("main"));
            Assert.That(error.Data["Navigation.PreviousLoaderId"], Is.EqualTo("old-loader"));
            Assert.That(error.BrowserMayHaveAdvanced, Is.True);
            Assert.That(controller.ResourcesForTest.Pending, Is.Zero);
            Assert.That(controller.ResourcesForTest.Subscribers, Is.EqualTo(1));
        });
        fixture.NavigationError = null;
        await controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, setup.Token);
    }

    [TestCase("Network.enable"), TestCase("Page.getFrameTree"), TestCase("Page.navigate"), TestCase("Runtime.evaluate")]
    public async Task CanceledProtocolRequestIdentifiesNavigationStage(string method)
    {
        await using var fixture = await ProtocolFixture.StartAsync();
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await controller.InitializeAsync(setup.Token);
        fixture.HoldMethod = method;
        using var canceled = new CancellationTokenSource();
        var pending = controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, canceled.Token);
        try
        {
            await fixture.HeldRequest.Task.WaitAsync(setup.Token);
            // Receipt on the server can precede completion of the client's socket send.
            // A second command crosses the send gate so cancellation tests the held
            // response wait, rather than legitimately aborting an in-flight socket send.
            await controller.BrowserAsync("Browser.getVersion", null, setup.Token);
            canceled.Cancel();
            var error = Assert.CatchAsync<OperationCanceledException>(async () => await pending.WaitAsync(setup.Token))!;
            Assert.That(error.Data["Navigation.Stage"], Is.EqualTo(method));
            Assert.That(error.CancellationToken, Is.EqualTo(canceled.Token));
            Assert.That(controller.ResourcesForTest.Pending, Is.Zero);
            Assert.That(controller.ResourcesForTest.Subscribers, Is.EqualTo(1));
        }
        finally { canceled.Cancel(); }
        fixture.HoldMethod = null;
        await controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, setup.Token);
    }

    [TestCase(true), TestCase(false)]
    public async Task StandardCancellationDistinguishesDriverFromEventWaiting(bool holdDriver)
    {
        await using var fixture = await ProtocolFixture.StartAsync();
        await using var controller = new CdpController(fixture.Endpoint, []);
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await controller.InitializeAsync(setup.Token);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var canceled = new CancellationTokenSource();
        var pending = controller.NavigateAsync(new Uri("http://fixture/download"), new(), token =>
        {
            entered.TrySetResult(true);
            return holdDriver ? Task.Delay(Timeout.Infinite, token) : Task.CompletedTask;
        }, canceled.Token);
        try
        {
            await entered.Task.WaitAsync(setup.Token);
            canceled.Cancel();
            var error = Assert.CatchAsync<OperationCanceledException>(async () => await pending.WaitAsync(setup.Token))!;
            Assert.That(error.Data["Navigation.Stage"], Is.EqualTo(holdDriver ? "WebDriver.Navigate" : "navigation-events"));
            Assert.That(error.Data["Navigation.Committed"], Is.EqualTo("False"));
            Assert.That(error.Data["Navigation.Loaded"], Is.EqualTo("False"));
            Assert.That(error.Data["Navigation.OutstandingRequests"], Is.EqualTo("0"));
            Assert.That(controller.ResourcesForTest.Pending, Is.Zero);
            Assert.That(controller.ResourcesForTest.Subscribers, Is.EqualTo(1));
        }
        finally { canceled.Cancel(); }
    }

    private sealed class ProtocolFixture : IAsyncDisposable
    {
        private LocalServer server = null!;
        public Uri Endpoint => new UriBuilder(server.Url) { Scheme = "ws", Path = "/devtools/browser/fixture" }.Uri;
        public int PageCallsWithoutSession;
        public int ScriptRegistrations;
        public int EmptySnapshotsRemaining;
        public int DiscoveryRequests;
        public int CreatedTargets;
        public int AttachRequests;
        public bool MultipleInitialPages;
        public TaskCompletionSource<bool> EmptySnapshotRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public System.Collections.Concurrent.ConcurrentBag<string> Sources = new();
        public bool OnlyOldLoader;
        public string? Scenario;
        public string? NavigationError;
        public string? HoldMethod;
        public TaskCompletionSource<bool> HeldRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> FailureSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
                    if (method == "Target.attachToTarget") Interlocked.Increment(ref AttachRequests);
                    if (method == HoldMethod) { HeldRequest.TrySetResult(true); continue; }
                    if ((method.StartsWith("Page.", StringComparison.Ordinal) || method.StartsWith("Runtime.", StringComparison.Ordinal)) && !root.TryGetProperty("sessionId", out _)) Interlocked.Increment(ref PageCallsWithoutSession);
                    object result = method switch
                    {
                        "Browser.getVersion" => new { product = "Chrome/150.0.1.0", protocolVersion = "1.3" },
                        "Target.getTargets" => Targets(),
                        "Target.createTarget" => CreateTarget(),
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
                    if (method == "Page.navigate" && NavigationError is not null) result = new { frameId = "main", errorText = NavigationError };
                    await Send(new { id = root.GetProperty("id").GetInt64(), result });
                    if (method == "Page.navigate" && NavigationError is not null) continue;
                    if (method == "Page.navigate")
                    {
                        if (Scenario is "session-detached" or "other-session-detached" or "inspector-detached")
                        {
                            if (Scenario == "inspector-detached") await Send(new { method = "Inspector.detached", sessionId = "session-page", @params = new { reason = "fixture-detach" } });
                            else await Send(new { method = "Target.detachedFromTarget", @params = new { sessionId = Scenario == "session-detached" ? "session-page" : "other-session", targetId = "page" } });
                            if (Scenario != "other-session-detached") continue;
                        }
                        if (Scenario is "document-failed" or "document-aborted" or "redirect-failed" or "other-frame-failed" or "old-loader-failed" or "other-session-failed" or "response-aborted-download" or "response-aborted-no-download")
                        {
                            var injectedSession = Scenario == "other-session-failed" ? "other-session" : "session-page";
                            var injectedFrame = Scenario == "other-frame-failed" ? "child" : "main";
                            var injectedLoader = Scenario == "old-loader-failed" ? "old-loader" : "new-loader";
                            await Send(new { method = "Network.requestWillBeSent", sessionId = injectedSession, @params = new { requestId = "document", loaderId = injectedLoader, frameId = injectedFrame, type = "Document" } });
                            if (Scenario == "redirect-failed") await Send(new { method = "Network.requestWillBeSent", sessionId = injectedSession, @params = new { requestId = "document", loaderId = injectedLoader, frameId = injectedFrame, type = "Document", redirectResponse = new { url = "http://fixture/redirect" } } });
                            if (Scenario.StartsWith("response-", StringComparison.Ordinal)) await Send(new { method = "Network.responseReceived", sessionId = injectedSession, @params = new { requestId = "document", type = "Document" } });
                            await Send(new { method = "Network.loadingFailed", sessionId = injectedSession, @params = new { requestId = "document", type = "Document", errorText = Scenario.Contains("aborted", StringComparison.Ordinal) ? "net::ERR_ABORTED" : "net::ERR_CONNECTION_RESET", canceled = true } });
                            FailureSent.TrySetResult(true);
                            if (Scenario == "response-aborted-download")
                            {
                                await Task.Delay(150, context.RequestAborted);
                                await Send(new { method = "Browser.downloadWillBegin", @params = new { frameId = "main", guid = "download" } });
                                continue;
                            }
                            if (Scenario is "document-failed" or "document-aborted" or "redirect-failed") continue;
                        }
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
        private object Targets()
        {
            Interlocked.Increment(ref DiscoveryRequests);
            var infos = new List<object>();
            if (EmptySnapshotsRemaining > 0)
            {
                EmptySnapshotsRemaining--;
                // Non-page targets must not satisfy the initial page wait.
                infos.Add(new { targetId = "worker", type = "service_worker", url = "http://fixture/worker.js", title = "worker" });
                EmptySnapshotRequested.TrySetResult(true);
            }
            else
            {
                infos.Add(new { targetId = "page", type = "page", url = "about:blank", title = "fixture" });
                if (MultipleInitialPages) infos.Add(new { targetId = "other-page", type = "page", url = "about:blank", title = "other" });
            }
            if (CreatedTargets > 0) infos.Add(new { targetId = "fallback", type = "page", url = "about:blank", title = "fallback" });
            return new { targetInfos = infos };
        }
        private object CreateTarget()
        {
            Interlocked.Increment(ref CreatedTargets);
            return new { targetId = "fallback" };
        }
    }
}
