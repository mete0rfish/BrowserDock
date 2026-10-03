using System.Diagnostics;
using System.Text.Json;
using BrowserDock.Cdp;
using BrowserDock.FrameworkTests;
using NUnit.Framework;
using Legacy = BrowserDock.Legacy;

namespace BrowserDock.Tests;

[TestFixture, Category("Windows"), NonParallelizable]
public sealed class UcBrowserTests
{
    private BrowserOptions options = null!;
    [SetUp]
    public void RequireFixture()
    {
        if (!Platform.IsWindows || !Platform.IsX64Process || Platform.WindowsBuild < 22000) Assert.Ignore("Requires the Windows browser fixture.");
        var chrome = Environment.GetEnvironmentVariable("BROWSERDOCK_CHROME");
        var driver = Environment.GetEnvironmentVariable("BROWSERDOCK_DRIVER");
        Assert.That(File.Exists(chrome) && File.Exists(driver), Is.True, "Supply the pinned Chrome/driver fixture.");
        options = new() { ChromeBinaryPath = chrome, Driver = new() { ExecutablePath = driver! } };
    }

    [TestCase(false), TestCase(true)]
    public async Task ReconnectKeepsDocumentAndChromeButInvalidatesReferences(bool legacy)
    {
        using var server = await FixtureServer.StartAsync();
        var timeline = new List<object>(); var clock = Stopwatch.StartNew();
        await using var browser = await Browser.StartAsync(options with
        { StageHook = (stage, _) => { if (stage.StartsWith("uc-", StringComparison.Ordinal)) timeline.Add(new { stage, elapsedMs = clock.Elapsed.TotalMilliseconds }); return default; } });
        await Open(browser, legacy, "open", new Uri(server.Url, "/page"));
        await using var old = await browser.GetWebDriverAsync();
        var element = await old.Commands.FindAsync(Locator.Id("button"));
        await old.Commands.ExecuteScriptAsync("document.body.dataset.marker='preserved'");
        var before = browser.TestSnapshot;
        using var chrome = Process.GetProcessById(browser.Health.ChromePid!.Value);
        var created = chrome.StartTime;
        var generation = browser.SessionGeneration;
        if (legacy) await (await new Legacy.Browser(browser).Uc.ReconnectAsync(TimeSpan.FromMilliseconds(250))).DisposeAsync();
        else await (await browser.Uc.ReconnectAsync(TimeSpan.FromMilliseconds(250))).DisposeAsync();
        Assert.That(browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
        Assert.That(browser.SessionGeneration, Is.EqualTo(generation + 1));
        Assert.That(browser.TestSnapshot.SessionId, Is.Not.EqualTo(before.SessionId));
        Assert.That(browser.TestSnapshot.Profile, Is.EqualTo(before.Profile));
        Assert.That(chrome.HasExited, Is.False); Assert.That(chrome.StartTime, Is.EqualTo(created));
        Assert.ThrowsAsync<StaleAttachmentException>(async () => await old.Commands.GetTitleAsync());
        Assert.ThrowsAsync<StaleAttachmentException>(async () => await element.GetTextAsync());
        await using var fresh = await browser.GetWebDriverAsync();
        Assert.That((await fresh.Commands.ExecuteScriptAsync("return document.body.dataset.marker")).GetString(), Is.EqualTo("preserved"));
        Assert.That(await (await element.ReacquireAsync()).GetTextAsync(), Is.EqualTo("ready"));
        if (legacy)
        {
            var uc = new Legacy.Browser(browser).Uc;
            Assert.ThrowsAsync<Legacy.BrowserDockException>(async () => await uc.ConnectAsync());
            await uc.DisconnectAsync(); await uc.DisconnectAsync();
            await (await uc.ConnectAsync()).DisposeAsync();
        }
        else
        {
            Assert.ThrowsAsync<BrowserDockException>(async () => await browser.Uc.ConnectAsync());
            await browser.Uc.DisconnectAsync(); await browser.Uc.DisconnectAsync();
            await (await browser.Uc.ConnectAsync()).DisposeAsync();
        }
        TestContext.Out.WriteLine(JsonSerializer.Serialize(new { legacy, timeline, generation = browser.SessionGeneration }));
    }

    [TestCase(false), TestCase(true)]
    public async Task HelpersPreserveBystanderTabsAndTheirDocumentedReturnState(bool legacy)
    {
        using var server = await FixtureServer.StartAsync();
        await using var browser = await Browser.StartAsync(options);
        var original = (await browser.GetTargetsAsync()).Single().Key;
        var created = await browser.ExecuteCdpAsync("Target.createTarget", new { url = "about:blank" });
        var bystander = (await browser.GetTargetsAsync()).Single(t => t.Key != original).Key;
        var before = browser.Health;
        var ambiguous = Assert.CatchAsync<Exception>(async () => await Open(browser, legacy, "tab", new Uri(server.Url, "/page")))!;
        Assert.That(legacy ? ((Legacy.BrowserDockException)ambiguous).Category.ToString() : ((BrowserDockException)ambiguous).Category.ToString(), Is.EqualTo("AmbiguousTarget"));
        Assert.That(browser.Health.DriverPid, Is.EqualTo(before.DriverPid));
        // Explicit binding, never newest-window selection. The facade option
        // resolves the target before detaching or closing the original page.
        await Open(browser, legacy, "disconnect", new Uri(server.Url, "/redirect"), original);
        Assert.That(browser.State, Is.EqualTo(BrowserState.CdpOnly));
        var replacement = (await browser.GetTargetsAsync()).Single(t => t.Controlled).Key;
        Assert.That(replacement, Is.Not.EqualTo(original));
        Assert.That((await browser.GetTargetsAsync()).Any(t => t.Key == bystander), Is.True);
        var generation = browser.SessionGeneration;
        await Open(browser, legacy, "open", new Uri(server.Url, "/page#anchor"));
        Assert.That(browser.State, Is.EqualTo(BrowserState.CdpOnly));
        Assert.That(browser.SessionGeneration, Is.EqualTo(generation));
        Assert.That((await browser.GetTargetsAsync()).Single(t => t.Controlled).Key, Is.EqualTo(replacement));
        await Open(browser, legacy, "tab", new Uri(server.Url, "/page"));
        Assert.That(browser.State, Is.EqualTo(BrowserState.CdpOnly));
        await Open(browser, legacy, "reconnect", new Uri(server.Url, "/page"));
        Assert.That(browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
        Assert.That(browser.SessionGeneration, Is.EqualTo(generation + 1));
        Assert.That((await browser.GetTargetsAsync()).Any(t => t.Key == bystander), Is.True);
        Assert.That((await browser.GetTargetsAsync()).Count, Is.EqualTo(2));
    }

    [TestCase(false), TestCase(true)]
    public async Task CancellationDuringHoldLeavesChromeDisconnectedAndNoLateReconnect(bool legacy)
    {
        using var server = await FixtureServer.StartAsync();
        using var caller = new CancellationTokenSource();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var browser = await Browser.StartAsync(options with
        {
            StageHook = async (stage, token) => { if (stage == "uc-hold") { entered.TrySetResult(true); await Task.Delay(Timeout.Infinite, token); } }
        });
        var pid = browser.Health.ChromePid;
        var generation = browser.SessionGeneration;
        var pending = Open(browser, legacy, "reconnect", new Uri(server.Url, "/page"), token: caller.Token);
        try
        {
            await TaskCompatibility.WaitAsync(entered.Task, TimeSpan.FromSeconds(60));
            caller.Cancel();
            var error = Assert.CatchAsync<OperationCanceledException>(async () => await pending)!;
            Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
            Assert.That(error.Data["Navigation.Stage"], Is.EqualTo("uc-hold"));
            Assert.That(browser.State, Is.EqualTo(BrowserState.CdpOnly));
            Assert.That(browser.Health.DriverPid, Is.Null);
            Assert.That(browser.Health.ChromePid, Is.EqualTo(pid));
            Assert.That(browser.SessionGeneration, Is.EqualTo(generation));
            await browser.ExecuteCdpAsync("Browser.getVersion");
        }
        finally { caller.Cancel(); try { await pending; } catch (OperationCanceledException) { } }
    }

    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public async Task FailureDuringDisconnectedHoldIsNotReportedAsSuccess(bool legacy, bool closeChrome)
    {
        using var server = await FixtureServer.StartAsync();
        var url = new Uri(server.Url, "/page");
        Browser browser = null!;
        await using var owned = await Browser.StartAsync(options with
        {
            StageHook = async (stage, token) =>
            {
                if (stage != "uc-hold") return;
                if (closeChrome)
                {
                    using var process = Process.GetProcessById(browser.Health.ChromePid!.Value);
                    process.Kill();
                    await TaskCompatibility.WaitForExitAsync(process, token);
                }
                else
                {
                    // A separate CDP client models a user closing the selected
                    // fixture page while the UC operation owns the lifecycle gate.
                    await using var client = new CdpConnection();
                    await client.ConnectAsync(browser.TestSnapshot.Endpoint!, token);
                    var targets = await client.SendAsync("Target.getTargets", null, null, token);
                    var id = targets.GetProperty("targetInfos").EnumerateArray()
                        .Single(t => t.GetProperty("type").GetString() == "page" && t.GetProperty("url").GetString() == url.AbsoluteUri)
                        .GetProperty("targetId").GetString();
                    var closed = await client.SendAsync("Target.closeTarget", new { targetId = id }, null, token);
                    Assert.That(closed.GetProperty("success").GetBoolean(), Is.True);
                    // Closing is asynchronous. Establish the terminal condition
                    // before letting the UC operation verify its return state.
                    while (true)
                    {
                        var remaining = await client.SendAsync("Target.getTargets", null, null, token);
                        if (!remaining.GetProperty("targetInfos").EnumerateArray().Any(t => t.GetProperty("targetId").GetString() == id)) break;
                        await Task.Delay(20, token);
                    }
                }
            }
        });
        browser = owned;
        var original = (await browser.GetTargetsAsync()).Single().Key;
        await browser.ExecuteCdpAsync("Target.createTarget", new { url = "about:blank" });
        var bystander = (await browser.GetTargetsAsync()).Single(t => t.Key != original).Key;
        var generation = browser.SessionGeneration;
        var error = Assert.CatchAsync<Exception>(async () =>
        {
            if (legacy)
                await new Legacy.Browser(browser).Uc.OpenWithDisconnectAsync(url, TimeSpan.FromSeconds(1),
                    new() { Target = new Legacy.TargetKey(original.Value) });
            else
                await browser.Uc.OpenWithDisconnectAsync(url, TimeSpan.FromSeconds(1), new() { Target = original });
        })!;
        var core = legacy ? (BrowserDockException)error.InnerException! : (BrowserDockException)error;
        Assert.That(core.Category, Is.EqualTo(closeChrome ? ErrorCategory.ChromeExited : ErrorCategory.TargetClosed));
        Assert.That(core.BrowserMayHaveAdvanced, Is.True);
        Assert.That(error.Data["Navigation.Stage"], Is.EqualTo("uc-verify-state"));
        Assert.That(error.Data["Navigation.UcOperation"], Is.EqualTo("OpenWithDisconnect"));
        Assert.That(browser.Health.DriverPid, Is.Null);
        Assert.That(browser.SessionGeneration, Is.EqualTo(generation));
        Assert.That(browser.State, Is.EqualTo(closeChrome ? BrowserState.Faulted : BrowserState.CdpOnly));
        if (!closeChrome)
            Assert.That((await browser.GetTargetsAsync()).Select(t => t.Key), Is.EqualTo(new[] { bystander }),
                "Closing the controlled target must not navigate or select the surviving page.");
    }

    private static async Task Open(Browser browser, bool legacy, string operation, Uri url, TargetKey? target = null, CancellationToken token = default)
    {
        if (legacy)
        {
            var uc = new Legacy.Browser(browser).Uc;
            var policy = new Legacy.UcNavigationOptions { Target = target is { } key ? new Legacy.TargetKey(key.Value) : null };
            var result = operation switch
            {
                "tab" => await uc.OpenWithTabAsync(url, policy, token),
                "disconnect" => await uc.OpenWithDisconnectAsync(url, TimeSpan.FromMilliseconds(100), policy, token),
                "reconnect" => await uc.OpenWithReconnectAsync(url, TimeSpan.FromMilliseconds(100), policy, token),
                _ => await uc.OpenAsync(url, policy, token)
            };
            Assert.That(result.FinalUrl.AbsolutePath, Is.EqualTo(url.AbsolutePath == "/redirect" ? "/page" : url.AbsolutePath));
            Assert.That(result.FinalUrl.Fragment, Is.EqualTo(url.Fragment));
        }
        else
        {
            var policy = new UcNavigationOptions { Target = target };
            var result = operation switch
            {
                "tab" => await browser.Uc.OpenWithTabAsync(url, policy, token),
                "disconnect" => await browser.Uc.OpenWithDisconnectAsync(url, TimeSpan.FromMilliseconds(100), policy, token),
                "reconnect" => await browser.Uc.OpenWithReconnectAsync(url, TimeSpan.FromMilliseconds(100), policy, token),
                _ => await browser.Uc.OpenAsync(url, policy, token)
            };
            Assert.That(result.FinalUrl.AbsolutePath, Is.EqualTo(url.AbsolutePath == "/redirect" ? "/page" : url.AbsolutePath));
            Assert.That(result.FinalUrl.Fragment, Is.EqualTo(url.Fragment));
        }
    }
}
