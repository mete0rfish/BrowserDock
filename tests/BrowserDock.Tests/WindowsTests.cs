using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using BrowserDock.Hosting;
using BrowserDock.WebDriver;

namespace BrowserDock.Tests;

[TestFixture, Category("Windows"), NonParallelizable]
public sealed partial class WindowsTests
{
    private BrowserOptions options = null!;
    [SetUp]
    public void RequireWindowsFixture()
    {
        if (!OperatingSystem.IsWindows() || Environment.OSVersion.Version.Build < 22000) Assert.Ignore("Requires Windows 11 x64 and explicitly supplied browser/driver binaries.");
        var chrome = Environment.GetEnvironmentVariable("BROWSERDOCK_CHROME");
        var driver = Environment.GetEnvironmentVariable("BROWSERDOCK_DRIVER");
        if (string.IsNullOrEmpty(chrome) || string.IsNullOrEmpty(driver) || !File.Exists(chrome) || !File.Exists(driver)) Assert.Ignore("Set BROWSERDOCK_CHROME and BROWSERDOCK_DRIVER to a compatible fixture pair.");
        options = new() { ChromeBinaryPath = chrome, Driver = new() { ExecutablePath = driver! }, NewDocumentScripts = ["globalThis.__browserDockFixture = 'installed'"] };
    }
    internal static Task<LocalServer> FixtureAsync() => LocalServer.StartAsync(async context =>
    {
        if (await TestFixtures.ReferencePages.HandleAsync(context)) return;
        if (context.Request.Path == "/redirect") { context.Response.Redirect("/page"); return; }
        if (context.Request.Path == "/download") { context.Response.ContentType = "application/octet-stream"; context.Response.Headers.ContentDisposition = "attachment; filename=fixture.txt"; await context.Response.WriteAsync("fixture"); return; }
        if (context.Request.Path == "/slow") { await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted); }
        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync("""
            <!doctype html><title>BrowserDock fixture</title><button id="button" onclick="this.textContent='clicked'">ready</button>
            <input id="input"><iframe src="/frame"></iframe>
            <script>if(location.pathname==='/frame')document.querySelector('iframe').remove();</script>
            """);
    });
    [Test, CancelAfter(150000)]
    public async Task AC01_02_03_DisconnectPreservesChromeAndReconnectReplacesSession()
    {
        await using var server = await FixtureAsync();
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var browser = await Browser.StartAsync(options with
        {
            StageHook = async (stage, token) =>
            {
                if (stage != "admission-closed") return;
                closed.TrySetResult(true);
                await release.Task.WaitAsync(token);
            }
        });
        await browser.NavigateAsync(new Uri(server.Url, "/page"));
        await using var old = await browser.GetWebDriverAsync();
        var element = await old.Commands.FindAsync(Locator.Id("button"));
        await old.Commands.ExecuteScriptAsync("localStorage.setItem('fixture','preserved');document.cookie='fixture=preserved';document.body.dataset.marker='preserved'");
        var before = browser.TestSnapshot;
        var pid = browser.Health.ChromePid!.Value;
        using var process = Process.GetProcessById(pid);
        var created = process.StartTime;
        var driverPid = browser.Health.DriverPid!.Value;
        var disconnect = browser.DisconnectWebDriverAsync().AsTask();
        try
        {
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var sent = browser.TestSnapshot.Sent;
            Assert.ThrowsAsync<StaleAttachmentException>(async () => await old.Commands.GetTitleAsync());
            Assert.ThrowsAsync<StaleAttachmentException>(async () => await element.ClickAsync());
            Assert.That(browser.TestSnapshot.Sent, Is.EqualTo(sent));
        }
        finally { release.TrySetResult(true); }
        var detachTime = Stopwatch.StartNew();
        await disconnect.WaitAsync(TimeSpan.FromSeconds(5));
        TestContext.Out.WriteLine($"driver cleanup after admission barrier: {detachTime.Elapsed}");
        Assert.That(IsAlive(driverPid), Is.False);
        await AssertPortClosed(before.DriverPort!.Value);
        await Task.Delay(TimeSpan.FromSeconds(30));
        await browser.ExecuteCdpAsync("Browser.getVersion");
        Assert.That(process.HasExited, Is.False); Assert.That(process.StartTime, Is.EqualTo(created));
        await using var fresh = await browser.ReconnectWebDriverAsync();
        Assert.That(browser.TestSnapshot.SessionId, Is.Not.EqualTo(before.SessionId));
        Assert.That(browser.TestSnapshot.Profile, Is.EqualTo(before.Profile));
        Assert.That(browser.TestSnapshot.Endpoint, Is.EqualTo(before.Endpoint));
        Assert.That(fresh.Generation, Is.EqualTo(old.Generation + 1));
        var count = browser.TestSnapshot.Sent;
        Assert.ThrowsAsync<StaleAttachmentException>(async () => await old.Commands.GetUrlAsync());
        Assert.ThrowsAsync<StaleAttachmentException>(async () => await element.GetTextAsync());
        Assert.That(browser.TestSnapshot.Sent, Is.EqualTo(count));
        var preserved = await fresh.Commands.ExecuteScriptAsync("return [localStorage.getItem('fixture'),document.body.dataset.marker,document.cookie.includes('fixture=preserved')]");
        Assert.Multiple(() => { Assert.That(preserved[0].GetString(), Is.EqualTo("preserved")); Assert.That(preserved[1].GetString(), Is.EqualTo("preserved")); Assert.That(preserved[2].GetBoolean(), Is.True); });
        var reacquired = await element.ReacquireAsync(); await reacquired.ClickAsync(); Assert.That(await reacquired.GetTextAsync(), Is.EqualTo("clicked"));
        await fresh.Commands.ExecuteScriptAsync("document.getElementById('button').outerHTML='<button id=button>replaced</button>'");
        Assert.That(Assert.ThrowsAsync<BrowserDockException>(async () => await reacquired.GetTextAsync())!.Category, Is.EqualTo(ErrorCategory.StaleDomElement));
    }
    [TestCase(NavigationWaitUntil.Commit), TestCase(NavigationWaitUntil.DOMContentLoaded), TestCase(NavigationWaitUntil.Load), TestCase(NavigationWaitUntil.NetworkIdle)]
    public async Task NavigationModesRedirectsAndSameDocument(NavigationWaitUntil wait)
    {
        await using var server = await FixtureAsync();
        await using var browser = await Browser.StartAsync(options);
        var result = await browser.NavigateAsync(new Uri(server.Url, "/redirect"), new() { Mode = NavigationMode.Detached, WaitUntil = wait, ReconnectAfterNavigation = true });
        Assert.That(result.FinalUrl.AbsolutePath, Is.EqualTo("/page"));
        Assert.That(result.RedirectChain.Any(x => x.EndsWith("/redirect", StringComparison.Ordinal)), Is.True);
        await browser.DisconnectWebDriverAsync();
        var same = await browser.NavigateAsync(new Uri(server.Url, "/page#anchor"), new() { Mode = NavigationMode.CdpOnly, WaitUntil = wait });
        Assert.That(same.FinalUrl.Fragment, Is.EqualTo("#anchor"));
        Assert.That(browser.State, Is.EqualTo(BrowserState.CdpOnly));
    }
    [Test]
    public async Task ExplicitTargetsAndSocketRecoveryRestoreScripts()
    {
        await using var server = await FixtureAsync();
        await using var browser = await Browser.StartAsync(options);
        await browser.ExecuteCdpAsync("Target.createTarget", new { url = "about:blank" });
        await browser.DisconnectWebDriverAsync();
        Assert.ThrowsAsync<AmbiguousTargetException>(async () => await browser.ReconnectWebDriverAsync());
        var targets = await browser.GetTargetsAsync();
        await browser.SelectTargetAsync(targets.Single(x => !x.Controlled).Key);
        browser.InterruptCdpForTest();
        await browser.NavigateAsync(new Uri(server.Url, "/page"), new() { Mode = NavigationMode.CdpOnly });
        var result = await browser.ExecuteCdpAsync("Runtime.evaluate", new { expression = "globalThis.__browserDockFixture", returnByValue = true });
        Assert.That(result.GetProperty("result").GetProperty("value").GetString(), Is.EqualTo("installed"));
        await using var lease = await browser.ReconnectWebDriverAsync();
        Assert.That(await lease.Commands.GetUrlAsync(), Does.EndWith("/page"));
    }
    [Test, Category("Stress")]
    public async Task AC05_TwentyInstancesHaveDistinctEndpointsAndPorts()
    {
        RequireStress();
        var started = new System.Collections.Concurrent.ConcurrentBag<Browser>();
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ => started.Add(await Browser.StartAsync(options))));
            Assert.That(started.Select(x => x.TestSnapshot.Endpoint!.Port).Distinct().Count(), Is.EqualTo(20));
            Assert.That(started.Select(x => x.TestSnapshot.DriverPort).Distinct().Count(), Is.EqualTo(20));
        }
        finally { await Task.WhenAll(started.Select(x => x.DisposeAsync().AsTask())); }
    }
    [Test, Category("Stress")]
    public async Task AC04_09_HundredCyclesDoNotLeakOwnedResources()
    {
        RequireStress();
        await using var server = await FixtureAsync();
        using var self = Process.GetCurrentProcess();
        var measurements = new List<(int Handles, int Threads, long Bytes)>();
        for (var i = 0; i < 105; i++)
        {
            await using var browser = await Browser.StartAsync(options);
            await browser.NavigateAsync(new Uri(server.Url, "/page"), new() { Mode = NavigationMode.Detached });
            await browser.ReconnectWebDriverAsync();
            var pid = browser.Health.ChromePid!.Value; var driver = browser.Health.DriverPid!.Value;
            var before = browser.TestSnapshot;
            var resources = browser.CaptureResourcesForTest();
            var stopTime = Stopwatch.StartNew();
            await browser.StopAsync();
            Assert.That(stopTime.Elapsed, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(resources(), Is.EqualTo((0, 0, 0, false)), "Pending CDP requests, workers, subscribers or sockets survived stop.");
            Assert.That(browser.StoppedTreeAliveForTest, Is.False, "Owned Chrome job still contains processes.");
            Assert.That(IsAlive(pid), Is.False); Assert.That(IsAlive(driver), Is.False);
            Assert.That(Directory.Exists(before.Profile), Is.False);
            await AssertPortClosed(before.DriverPort!.Value); await AssertPortClosed(before.Endpoint!.Port);
            self.Refresh(); measurements.Add((self.HandleCount, self.Threads.Count, self.PrivateMemorySize64));
        }
        var baseline = measurements[4]; var final = measurements[^1];
        var medianBytes = measurements.TakeLast(20).Select(x => x.Bytes).Order().ElementAt(10);
        TestContext.Out.WriteLine(JsonSerializer.Serialize(new { measurements, baseline, final, medianBytes }, new JsonSerializerOptions { IncludeFields = true }));
        Assert.That(final.Handles, Is.LessThanOrEqualTo(baseline.Handles + 5));
        Assert.That(final.Threads, Is.LessThanOrEqualTo(baseline.Threads + 2));
        Assert.That(medianBytes, Is.LessThanOrEqualTo(baseline.Bytes * 1.2));
    }
    [TestCase("chrome-start"), TestCase("endpoint"), TestCase("driver-start"), TestCase("session-created")]
    public async Task AC06_StartupCancellationRollsBack(string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var root = TestPaths.NewDirectory();
        try
        {
            var configured = options with { Profile = new() { Directory = root }, StageHook = (name, token) => { if (name == stage) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); } return ValueTask.CompletedTask; } };
            Assert.ThrowsAsync<OperationCanceledException>(async () => await Browser.StartAsync(configured, cancellation.Token));
            using var unlocked = Hosting.Profile.Acquire(new() { Directory = root });
        }
        finally { Directory.Delete(root, true); }
    }
    [Test]
    public async Task AC06_DriverCrashAndChromeCrashHaveSeparateOutcomes()
    {
        var browser = await Browser.StartAsync(options);
        try
        {
            await using var lease = await browser.GetWebDriverAsync();
            using (var driver = Process.GetProcessById(browser.Health.DriverPid!.Value)) { driver.Kill(); await driver.WaitForExitAsync(); }
            Assert.ThrowsAsync<BrowserDockException>(async () => await lease.Commands.GetUrlAsync());
            Assert.That(browser.State, Is.EqualTo(BrowserState.CdpOnly));
            await browser.ReconnectWebDriverAsync();
            using (var chrome = Process.GetProcessById(browser.Health.ChromePid!.Value)) { chrome.Kill(); await chrome.WaitForExitAsync(); }
            Assert.ThrowsAsync<BrowserExitedException>(async () => await browser.GetTargetsAsync());
            Assert.That(browser.State, Is.EqualTo(BrowserState.Faulted));
        }
        finally { await browser.DisposeAsync(); }
    }
    [Test]
    public async Task AC07_AttachedResizeReportsCapabilityResult()
    {
        await using var browser = await Browser.StartAsync(options);
        await using var lease = await browser.GetWebDriverAsync();
        var version = await browser.ExecuteCdpAsync("Browser.getVersion");
        try
        {
            await lease.Commands.ResizeWindowAsync(1024, 768);
            var size = await lease.Commands.ExecuteScriptAsync("return [window.outerWidth,window.outerHeight]");
            Assert.That(size[0].GetInt32(), Is.EqualTo(1024));
            Assert.That(size[1].GetInt32(), Is.EqualTo(768));
            TestContext.Out.WriteLine($"{version}: attached resize supported");
        }
        catch (BrowserDockException e) when (e.Category == ErrorCategory.UnsupportedAttachedCommand) { TestContext.Out.WriteLine($"{version}: attached resize unsupported"); }
    }
    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public async Task AC01_DetachCapabilityAndDeleteSessionComparison(bool detach, bool sendDelete)
    {
        await using var unrelated = await Browser.StartAsync(options);
        await using var browser = await Browser.StartAsync(options);
        await browser.DisconnectWebDriverAsync();
        var snapshot = browser.TestSnapshot;
        var port = BrowserHosting.CandidatePort();
        using var driverProcess = new OwnedProcess(options.Driver.ExecutablePath, [$"--port={port}", "--allowed-ips=127.0.0.1"]);
        using var executor = new DetachAwareCommandExecutor(new LoopbackExecutor(new Uri($"http://127.0.0.1:{port}"), TimeSpan.FromSeconds(5)));
        using var http = BrowserHosting.Http();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            while (true)
            {
                try { using var status = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/status", deadline.Token)); if (status.RootElement.GetProperty("value").GetProperty("ready").GetBoolean()) break; }
                catch (HttpRequestException) { }
                await Task.Delay(50, deadline.Token);
            }
            var chrome = new ChromeOptions { DebuggerAddress = $"127.0.0.1:{snapshot.Endpoint!.Port}" };
            if (detach) chrome.LeaveBrowserRunning = true;
            using var remote = await Task.Run(() => new RemoteWebDriver(executor, chrome.ToCapabilities()), deadline.Token);
            if (sendDelete) using (var response = await http.DeleteAsync($"http://127.0.0.1:{port}/session/{remote.SessionId}", deadline.Token)) response.EnsureSuccessStatusCode();
            else { driverProcess.Terminate(); await driverProcess.WaitAsync(deadline.Token); }
            var survived = IsAlive(browser.Health.ChromePid!.Value);
            var cdpAlive = false;
            if (survived) { try { await browser.ExecuteCdpAsync("Browser.getVersion"); cdpAlive = true; } catch (BrowserDockException) { } }
            TestContext.Out.WriteLine(JsonSerializer.Serialize(new { detach, sendDelete, survived, cdpAlive }));
            if (detach && !sendDelete) Assert.That(survived && cdpAlive, Is.True, "Product combination must preserve Chrome and CDP.");
            Assert.That(IsAlive(unrelated.Health.ChromePid!.Value), Is.True);
        }
        finally { executor.Detach(); driverProcess.Terminate(); await driverProcess.WaitAsync(deadline.Token); }
    }
    [Test]
    public async Task AC06_NavigationAndReconnectCancellationPreserveRecoverableBrowser()
    {
        await using var server = await FixtureAsync();
        using var cancel = new CancellationTokenSource();
        var cancelStage = "";
        var configured = options with { StageHook = (stage, token) => { if (stage == cancelStage) { cancel.Cancel(); token.ThrowIfCancellationRequested(); } return ValueTask.CompletedTask; } };
        await using var browser = await Browser.StartAsync(configured);
        await browser.DisconnectWebDriverAsync();
        var generation = browser.SessionGeneration;
        cancelStage = "reconnect";
        Assert.CatchAsync<OperationCanceledException>(async () => await browser.ReconnectWebDriverAsync(cancel.Token));
        Assert.That(browser.State, Is.EqualTo(BrowserState.CdpOnly));
        Assert.That(browser.SessionGeneration, Is.EqualTo(generation));
        cancelStage = "";
        await browser.ReconnectWebDriverAsync();
        using var navigation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var error = Assert.CatchAsync<OperationCanceledException>(async () => await browser.NavigateAsync(new Uri(server.Url, "/slow"), new() { Mode = NavigationMode.Detached }, navigation.Token));
        Assert.That(error, Is.TypeOf<NavigationCanceledException>());
        Assert.That(((NavigationCanceledException)error!).BrowserMayHaveAdvanced, Is.True);
        await browser.ExecuteCdpAsync("Browser.getVersion");
    }
    [Test]
    public async Task CallerProfileAndCanceledStopPreserveSentinelAndReleaseLock()
    {
        var root = TestPaths.NewDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "sentinel"), "keep");
            var browser = await Browser.StartAsync(options with { Profile = new() { Directory = root } });
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await browser.StopAsync(cancellationToken: canceled.Token);
            Assert.That(browser.State, Is.EqualTo(BrowserState.Stopped));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(root, "sentinel")), Is.EqualTo("keep"));
            using var unlocked = Profile.Acquire(new() { Directory = root });
        }
        finally { Directory.Delete(root, true); }
    }
    private static void RequireStress() { if (Environment.GetEnvironmentVariable("BROWSERDOCK_STRESS") != "1") Assert.Ignore("Set BROWSERDOCK_STRESS=1 to run resource-intensive acceptance tests."); }
    [TestCase(NavigationMode.Standard), TestCase(NavigationMode.Detached)]
    public async Task DownloadsAreTerminalResultsInsteadOfLoadTimeouts(NavigationMode mode)
    {
        var downloads = TestPaths.NewDirectory();
        try
        {
            await using var server = await FixtureAsync();
            await using var browser = await Browser.StartAsync(options);
            await browser.ExecuteCdpAsync("Browser.setDownloadBehavior", new { behavior = "allow", downloadPath = downloads, eventsEnabled = true });
            var result = await browser.NavigateAsync(new Uri(server.Url, "/download"), new() { Mode = mode });
            Assert.That(result.Outcome, Is.EqualTo(NavigationOutcome.Download));
        }
        finally { Directory.Delete(downloads, true); }
    }
    private static bool IsAlive(int pid) { try { using var process = Process.GetProcessById(pid); return !process.HasExited; } catch (ArgumentException) { return false; } }
    private static async Task AssertPortClosed(int port)
    {
        using var socket = new TcpClient();
        try { await socket.ConnectAsync("127.0.0.1", port).WaitAsync(TimeSpan.FromSeconds(1)); Assert.Fail($"Owned port {port} remains open."); }
        catch (SocketException) { }
    }
}
