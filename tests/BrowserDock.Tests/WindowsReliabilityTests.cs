using System.Diagnostics;
using NUnit.Framework;

namespace BrowserDock.Tests;

public sealed partial class WindowsTests
{
    [Test]
    public async Task NestedFrameReacquisitionPreservesContextAcrossSessionChange()
    {
        await using var server = await FixtureAsync();
        await using var browser = await Browser.StartAsync(options);
        await browser.NavigateAsync(new Uri(server.Url, "/reference/page"));
        var input = await browser.FindAsync(Locator.Id("frame-input"), new() { FramePath = [Locator.Id("outer"), Locator.Id("inner")] });
        await input.SendKeysAsync("nested");
        await browser.DisconnectWebDriverAsync();
        await using var fresh = await browser.ReconnectWebDriverAsync();
        Assert.ThrowsAsync<StaleAttachmentException>(async () => await input.SendKeysAsync("bad"));
        var replacement = await input.ReacquireAsync();
        await replacement.SendKeysAsync("-kept");
        var value = await fresh.Commands.ExecuteScriptAsync("return document.querySelector('input').value");
        Assert.That(value.GetString(), Is.EqualTo("nested-kept"));
    }

    [Test]
    public async Task ReconnectDeadlineAfterSessionCreationCleansAttachmentAndKeepsGeneration()
    {
        var hold = false;
        await using var browser = await Browser.StartAsync(options with
        {
            Timeouts = options.Timeouts with { Reconnect = TimeSpan.FromSeconds(3) },
            StageHook = async (stage, token) => { if (hold && stage == "session-created") await Task.Delay(Timeout.Infinite, token); }
        });
        await browser.DisconnectWebDriverAsync();
        var generation = browser.SessionGeneration;
        var pid = browser.Health.ChromePid;
        hold = true;
        var timer = Stopwatch.StartNew();
        var error = Assert.ThrowsAsync<BrowserDockException>(async () => await browser.ReconnectWebDriverAsync());
        Assert.That(error!.Category, Is.EqualTo(ErrorCategory.OperationTimedOut));
        Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(13)));
        Assert.That(browser.SessionGeneration, Is.EqualTo(generation));
        Assert.That(browser.State, Is.EqualTo(BrowserState.CdpOnly));
        Assert.That(browser.Health.DriverPid, Is.Null);
        Assert.That(browser.Health.ChromePid, Is.EqualTo(pid));
        await browser.ExecuteCdpAsync("Browser.getVersion");
        hold = false;
        await using var lease = await browser.ReconnectWebDriverAsync();
        Assert.That(lease.Generation, Is.EqualTo(generation + 1));
    }

    [Test]
    public async Task HungWebDriverCommandIsInterruptedByDisconnect()
    {
        await using var server = await FixtureAsync();
        await using var browser = await Browser.StartAsync(options with
        { Timeouts = options.Timeouts with { Command = TimeSpan.FromSeconds(10), DisconnectDrain = TimeSpan.FromMilliseconds(100) } });
        await browser.NavigateAsync(new Uri(server.Url, "/reference/page"));
        await using var lease = await browser.GetWebDriverAsync();
        var sent = browser.TestSnapshot.Sent;
        var blocked = lease.Commands.ExecuteScriptAsync("const until=Date.now()+5000;while(Date.now()<until){};return true").AsTask();
        await WaitUntilAsync(() => browser.TestSnapshot.Sent > sent);
        using var driver = Process.GetProcessById(browser.Health.DriverPid!.Value);
        var disconnect = browser.DisconnectWebDriverAsync().AsTask();
        var failure = Assert.CatchAsync<Exception>(async () => await blocked.WaitAsync(TimeSpan.FromSeconds(12)));
        Assert.That(failure, Is.TypeOf<BrowserDockException>());
        await disconnect.WaitAsync(TimeSpan.FromSeconds(12));
        Assert.That(driver.HasExited, Is.True);
        Assert.That(browser.State, Is.EqualTo(BrowserState.CdpOnly));
        await browser.ExecuteCdpAsync("Browser.getVersion");
    }

    [Test]
    public async Task CallerCancellationDuringCleanupDoesNotInterruptOwnedResourceRelease()
    {
        using var cancellation = new CancellationTokenSource();
        var browser = await Browser.StartAsync(options with
        { StageHook = (stage, _) => { if (stage == "cleanup") cancellation.Cancel(); return default; } });
        var probe = browser.CaptureResourcesForTest();
        var snapshot = browser.TestSnapshot;
        using var chrome = Process.GetProcessById(browser.Health.ChromePid!.Value);
        try
        {
            await browser.StopAsync(cancellationToken: cancellation.Token);
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(browser.State, Is.EqualTo(BrowserState.Stopped));
            Assert.That(chrome.HasExited, Is.True);
            Assert.That(browser.StoppedTreeAliveForTest, Is.False);
            Assert.That(probe(), Is.EqualTo((0, 0, 0, false)));
            Assert.That(Directory.Exists(snapshot.Profile), Is.False);
            await AssertPortClosed(snapshot.Endpoint!.Port);
            await AssertPortClosed(snapshot.DriverPort!.Value);
        }
        finally { await browser.DisposeAsync(); }
    }

    [Test, Category("Stress")]
    public async Task AC09_HundredTargetCyclesKeepWebDriverAndCdpOnTheSelectedPage()
    {
        RequireStress();
        await using var server = await FixtureAsync();
        await using var browser = await Browser.StartAsync(options);
        var original = (await browser.GetTargetsAsync()).Single().Key;
        for (var i = 0; i < 100; i++)
        {
            var created = await browser.ExecuteCdpAsync("Target.createTarget", new { url = "about:blank" });
            var targetId = created.GetProperty("targetId").GetString();
            var child = (await browser.GetTargetsAsync()).Single(x => x.Key != original).Key;
            await browser.SelectTargetAsync(child);
            await browser.NavigateAsync(new Uri(server.Url, $"/reference/page?cycle={i}"));
            await using var lease = await browser.GetWebDriverAsync();
            Assert.That(await lease.Commands.GetUrlAsync(), Does.EndWith($"cycle={i}"));
            var popupUrl = new Uri(server.Url, $"/reference/page?popup={i}").AbsoluteUri;
            await lease.Commands.ExecuteScriptAsync("window.open(arguments[0], '_blank')", [popupUrl]);
            var targets = await browser.GetTargetsAsync();
            Assert.That(targets.Count, Is.EqualTo(3));
            Assert.That((await lease.Commands.GetWindowHandlesAsync()).Count, Is.EqualTo(3));
            var popup = targets.Single(x => x.Url == popupUrl);
            await browser.SelectTargetAsync(popup.Key);
            Assert.That(await lease.Commands.GetUrlAsync(), Is.EqualTo(popupUrl));
            var raw = await browser.ExecuteCdpAsync("Target.getTargets");
            var popupId = raw.GetProperty("targetInfos").EnumerateArray().Single(x => x.GetProperty("url").GetString() == popupUrl).GetProperty("targetId").GetString();
            await browser.SelectTargetAsync(original);
            await browser.ExecuteCdpAsync("Target.closeTarget", new { targetId = popupId });
            await browser.ExecuteCdpAsync("Target.closeTarget", new { targetId });
            Assert.That((await browser.GetTargetsAsync()).Single().Key, Is.EqualTo(original));
            Assert.That((await lease.Commands.GetWindowHandlesAsync()).Count, Is.EqualTo(1));
            if (i % 10 == 0)
            {
                browser.InterruptCdpForTest();
                await browser.NavigateAsync(new Uri(server.Url, $"/reference/page?recovered={i}"));
                var installed = await lease.Commands.ExecuteScriptAsync("return globalThis.__browserDockFixture");
                Assert.That(installed.GetString(), Is.EqualTo("installed"));
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, deadline.Token);
    }
}
