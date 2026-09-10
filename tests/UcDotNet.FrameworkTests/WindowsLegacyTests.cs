using System.Diagnostics;
using System.Net.Sockets;
using NUnit.Framework;
using Legacy = UcDotNet.Legacy;

namespace UcDotNet.FrameworkTests;

[TestFixture, Category("Windows"), NonParallelizable]
public sealed class WindowsLegacyTests
{
    private Legacy.BrowserOptions options = null!;
    [SetUp]
    public void RequireWindowsFixture()
    {
        if (!Platform.IsWindows || !Platform.IsX64Process || Platform.WindowsBuild < 22000) Assert.Ignore("Requires Windows 11 x64 with an interactive desktop.");
        var chrome = Environment.GetEnvironmentVariable("UCDOTNET_CHROME");
        var driver = Environment.GetEnvironmentVariable("UCDOTNET_DRIVER");
        if (!File.Exists(chrome) || !File.Exists(driver)) Assert.Ignore("Set UCDOTNET_CHROME and UCDOTNET_DRIVER to a compatible fixture pair.");
        options = new() { ChromeBinaryPath = chrome, Driver = new() { ExecutablePath = driver! }, NewDocumentScripts = new[] { "globalThis.__ucFixture='installed'" } };
    }

    [Test]
    public async Task FacadeReconnectPreservesBrowserStateAndInvalidatesOldReferences()
    {
        using var server = await FixtureServer.StartAsync();
        var browser = await Legacy.UcBrowser.StartAsync(options);
        try
        {
            await browser.NavigateAsync(new Uri(server.Url, "/page"));
            var old = await browser.GetWebDriverAsync();
            try
            {
                var element = await old.Commands.FindAsync(Legacy.Locator.Id("button"));
                await old.Commands.ExecuteScriptAsync("localStorage.setItem('fixture','kept');document.cookie='fixture=kept';document.body.dataset.marker='kept'");
                using var chrome = Process.GetProcessById(browser.Health.ChromePid!.Value);
                var created = chrome.StartTime;
                using var driver = Process.GetProcessById(browser.Health.DriverPid!.Value);
                await browser.DisconnectWebDriverAsync();
                Assert.That(driver.HasExited, Is.True);
                Assert.ThrowsAsync<Legacy.StaleAttachmentException>(async () => await old.Commands.GetTitleAsync());
                Assert.ThrowsAsync<Legacy.StaleAttachmentException>(async () => await element.ClickAsync());
                await browser.ExecuteCdpAsync("Browser.getVersion");
                Assert.That(chrome.HasExited, Is.False);
                Assert.That(chrome.StartTime, Is.EqualTo(created));
                var fresh = await browser.ReconnectWebDriverAsync();
                try
                {
                    Assert.That(fresh.Generation, Is.EqualTo(old.Generation + 1));
                    var state = await fresh.Commands.ExecuteScriptAsync("return [localStorage.getItem('fixture'),document.cookie.includes('fixture=kept'),document.body.dataset.marker,globalThis.__ucFixture]");
                    Assert.That(state[0].GetString(), Is.EqualTo("kept")); Assert.That(state[1].GetBoolean(), Is.True);
                    Assert.That(state[2].GetString(), Is.EqualTo("kept")); Assert.That(state[3].GetString(), Is.EqualTo("installed"));
                    var reacquired = await element.ReacquireAsync(); await reacquired.ClickAsync();
                    Assert.That(await reacquired.GetTextAsync(), Is.EqualTo("clicked"));
                    var input = await fresh.Commands.FindAsync(Legacy.Locator.Id("frame-input"), new() { FramePath = new[] { Legacy.Locator.Css("iframe") } });
                    await input.SendKeysAsync("framework");
                    Assert.That(await input.GetAttributeAsync("value"), Is.EqualTo("framework"));
                }
                finally { await fresh.DisposeAsync(); }
            }
            finally { await old.DisposeAsync(); }
        }
        finally { await browser.DisposeAsync(); }
    }

    [TestCase(Legacy.NavigationWaitUntil.Commit), TestCase(Legacy.NavigationWaitUntil.DOMContentLoaded), TestCase(Legacy.NavigationWaitUntil.Load), TestCase(Legacy.NavigationWaitUntil.NetworkIdle)]
    public async Task FacadeDetachedNavigationSupportsRedirectsAndSameDocument(Legacy.NavigationWaitUntil until)
    {
        using var server = await FixtureServer.StartAsync();
        var browser = await Legacy.UcBrowser.StartAsync(options);
        try
        {
            var result = await browser.NavigateAsync(new Uri(server.Url, "/redirect"), new() { Mode = Legacy.NavigationMode.Detached, WaitUntil = until });
            Assert.That(result.FinalUrl.AbsolutePath, Is.EqualTo("/page"));
            var same = await browser.NavigateAsync(new Uri(server.Url, "/page#anchor"), new() { Mode = Legacy.NavigationMode.CdpOnly, WaitUntil = until });
            Assert.That(same.FinalUrl.Fragment, Is.EqualTo("#anchor"));
            Assert.That(browser.State, Is.EqualTo(Legacy.BrowserState.CdpOnly));
        }
        finally { await browser.DisposeAsync(); }
    }

    [TestCase("chrome-start"), TestCase("endpoint"), TestCase("driver-start"), TestCase("session-created")]
    public void StartupCancellationRollsBackAndUnlocksCallerProfile(string stage)
    {
        var root = NewDirectory();
        using var canceled = new CancellationTokenSource();
        try
        {
            options.Profile.Directory = root;
            var snapshot = options.Snapshot() with { StageHook = (name, token) => { if (name == stage) { canceled.Cancel(); token.ThrowIfCancellationRequested(); } return default; } };
            Assert.CatchAsync<OperationCanceledException>(async () => await UcBrowser.StartAsync(snapshot, canceled.Token));
            using var unlocked = Hosting.Profile.Acquire(new() { Directory = root });
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task NavigationCancellationAndCanceledStopReleaseOwnedResources()
    {
        var root = NewDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "sentinel"), "keep"); options.Profile.Directory = root;
            using var server = await FixtureServer.StartAsync();
            var core = await UcBrowser.StartAsync(options.Snapshot());
            var browser = new Legacy.UcBrowser(core);
            try
            {
                using var chrome = Process.GetProcessById(browser.Health.ChromePid!.Value);
                var snapshot = core.TestSnapshot;
                using var canceled = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                var error = Assert.ThrowsAsync<Legacy.NavigationCanceledException>(async () => await browser.NavigateAsync(new Uri(server.Url, "/slow"), new() { Mode = Legacy.NavigationMode.Detached }, canceled.Token));
                Assert.That(error!.BrowserMayHaveAdvanced, Is.True);
                await browser.ExecuteCdpAsync("Browser.getVersion");
                canceled.Cancel(); await browser.StopAsync(cancellationToken: canceled.Token);
                Assert.That(chrome.HasExited, Is.True);
                await AssertPortClosed(snapshot.Endpoint!.Port); await AssertPortClosed(snapshot.DriverPort!.Value);
                Assert.That(File.ReadAllText(Path.Combine(root, "sentinel")), Is.EqualTo("keep"));
                using var unlocked = Hosting.Profile.Acquire(new() { Directory = root });
            }
            finally { await browser.DisposeAsync(); }
        }
        finally { Directory.Delete(root, true); }
    }

    [Test, Category("Stress")]
    public async Task HundredCyclesReleaseTemporaryProfilesProcessesAndPorts()
    {
        RequireStress();
        using var server = await FixtureServer.StartAsync();
        using var self = Process.GetCurrentProcess();
        var measurements = new List<(int Handles, int Threads, long Bytes)>();
        for (var i = 0; i < 105; i++)
        {
            var core = await UcBrowser.StartAsync(options.Snapshot());
            var browser = new Legacy.UcBrowser(core);
            try
            {
                await browser.NavigateAsync(new Uri(server.Url, "/page"), new() { Mode = Legacy.NavigationMode.Detached });
                var lease = await browser.ReconnectWebDriverAsync(); await lease.DisposeAsync();
                var before = core.TestSnapshot;
                using var chrome = Process.GetProcessById(browser.Health.ChromePid!.Value);
                using var driver = Process.GetProcessById(browser.Health.DriverPid!.Value);
                await browser.StopAsync();
                Assert.That(chrome.HasExited && driver.HasExited, Is.True);
                Assert.That(Directory.Exists(before.Profile), Is.False);
                await AssertPortClosed(before.Endpoint!.Port); await AssertPortClosed(before.DriverPort!.Value);
            }
            finally { await browser.DisposeAsync(); }
            self.Refresh(); measurements.Add((self.HandleCount, self.Threads.Count, self.PrivateMemorySize64));
        }
        var baseline = measurements[4]; var final = measurements[104];
        var median = measurements.Skip(85).Select(x => x.Bytes).OrderBy(x => x).ElementAt(10);
        TestContext.Out.WriteLine($"baseline={baseline}, final={final}, medianBytes={median}");
        Assert.That(final.Handles, Is.LessThanOrEqualTo(baseline.Handles + 5));
        Assert.That(final.Threads, Is.LessThanOrEqualTo(baseline.Threads + 2));
        Assert.That(median, Is.LessThanOrEqualTo(baseline.Bytes * 1.2));
    }

    [Test, Category("Stress")]
    public async Task TwentyInstancesUseDistinctPorts()
    {
        RequireStress();
        var started = new System.Collections.Concurrent.ConcurrentBag<UcBrowser>();
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ => started.Add(await UcBrowser.StartAsync(options.Snapshot()))));
            Assert.That(started.Select(x => x.TestSnapshot.Endpoint!.Port).Distinct().Count(), Is.EqualTo(20));
            Assert.That(started.Select(x => x.TestSnapshot.DriverPort).Distinct().Count(), Is.EqualTo(20));
        }
        finally { await Task.WhenAll(started.Select(x => new Legacy.UcBrowser(x).DisposeAsync())); }
    }

    private static string NewDirectory() { var path = Path.Combine(Path.GetTempPath(), "UcDotNet-framework-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static void RequireStress() { if (Environment.GetEnvironmentVariable("UCDOTNET_STRESS") != "1") Assert.Ignore("Set UCDOTNET_STRESS=1 for resource-intensive tests."); }
    private static async Task AssertPortClosed(int port)
    {
        using var client = new TcpClient();
        try { await TaskCompatibility.WaitAsync(client.ConnectAsync("127.0.0.1", port), TimeSpan.FromSeconds(1)); Assert.Fail("Owned port remains open: " + port); }
        catch (SocketException) { }
    }
}
