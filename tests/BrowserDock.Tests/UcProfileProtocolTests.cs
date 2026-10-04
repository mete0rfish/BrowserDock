using BrowserDock.Cdp;
using NUnit.Framework;
using Fixture = BrowserDock.Tests.NavigationContractTests.ProtocolFixture;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class UcProfileProtocolTests
{
    [Test]
    public async Task ProfileDiscoversLatePropertiesBeforeNavigationAndRetainsThemAcrossRecovery()
    {
        await using var fixture = await Fixture.StartAsync(); fixture.CdcNames = [];
        await using var controller = new CdpController(fixture.Endpoint, ["profile-script", "caller-script"], true, true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await controller.InitializeAsync(deadline.Token);
        Assert.That(fixture.ScriptRegistrations, Is.EqualTo(2));
        fixture.CdcNames = ["cdc_abcdefghijklmnopqrstuv_Array", "unsafe", "cdc_abcdefghijklmnopqrstuv_Array"];
        fixture.Calls.Clear();
        await controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, deadline.Token);
        var calls = fixture.Calls.ToArray();
        Assert.That(Array.IndexOf(calls, "Page.removeScriptToEvaluateOnNewDocument"), Is.LessThan(Array.IndexOf(calls, "Page.navigate")));
        Assert.That(fixture.ScriptRemovals, Is.EqualTo(2));
        Assert.That(fixture.ScriptRegistrations, Is.EqualTo(5));
        Assert.That(fixture.Sources.Single(x => x.Contains("const names =", StringComparison.Ordinal)), Does.Contain("cdc_abcdefghijklmnopqrstuv_Array").And.Not.Contain("unsafe"));
        fixture.CdcNames = []; // The current document has been cleaned; keep cleanup for the next one.
        await controller.NavigateAsync(new Uri("http://fixture/page"), new(), null, deadline.Token);
        Assert.That(fixture.ScriptRegistrations, Is.EqualTo(5));
        controller.InterruptForTest(); await controller.RecoverAsync(deadline.Token);
        Assert.That(fixture.ScriptRegistrations, Is.EqualTo(8));
        Assert.That(controller.ScriptSessionsForTest, Is.EqualTo(1));
        Assert.That(fixture.Sources.Count(x => x.Contains("const names =", StringComparison.Ordinal)), Is.EqualTo(2));
    }

    [Test]
    public async Task StandardDelegateRunsOnlyAfterLatePropertyScriptsAreReady()
    {
        await using var fixture = await Fixture.StartAsync(); fixture.CdcNames = [];
        await using var controller = new CdpController(fixture.Endpoint, ["caller"], true, true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(deadline.Token);
        fixture.CdcNames = ["cdc_abcdefghijklmnopqrstuv_Array"];
        await controller.NavigateAsync(new Uri("http://fixture/page"), new(), async token =>
        {
            Assert.That(fixture.ScriptRemovals, Is.EqualTo(1));
            Assert.That(fixture.ScriptRegistrations, Is.EqualTo(3));
            // The fake WebDriver dispatches through the page; it is not a real-browser acceptance test.
            await controller.PageAsync("Page.navigate", new { url = "http://fixture/page" }, controller.Controlled, token);
        }, deadline.Token);
        Assert.That(fixture.ScriptRegistrations, Is.EqualTo(3));
    }

    [TestCase("Fixture.detachSession"), TestCase("Fixture.destroyTarget")]
    public async Task ClosingOrDetachingTargetsDiscardsScriptState(string method)
    {
        await using var fixture = await Fixture.StartAsync();
        await using var controller = new CdpController(fixture.Endpoint, ["caller"], true, true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(deadline.Token);
        await controller.BrowserAsync(method, null, deadline.Token);
        while (controller.ScriptSessionsForTest != 0) await Task.Delay(5, deadline.Token);
        if (method == "Fixture.detachSession")
        {
            await controller.PrepareControlledAsync(deadline.Token);
            Assert.That(controller.ScriptSessionsForTest, Is.EqualTo(1));
            Assert.That(fixture.ScriptRegistrations, Is.EqualTo(4));
        }
        else Assert.That(Assert.Throws<BrowserDockException>(() => controller.Resolve())!.Category, Is.EqualTo(ErrorCategory.TargetClosed));
    }

    [Test]
    public async Task ReplacementPreparesItsScriptsBeforeClosingOldTarget()
    {
        await using var fixture = await Fixture.StartAsync(); fixture.SessionPerTarget = true;
        await using var controller = new CdpController(fixture.Endpoint, ["caller"], true, true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.InitializeAsync(deadline.Token);
        fixture.CdcNames = []; fixture.Calls.Clear();
        await controller.ReplaceAsync(deadline.Token);
        while (controller.ScriptSessionsForTest != 1) await Task.Delay(5, deadline.Token);
        Assert.That(controller.Resolve().Id, Is.EqualTo("fallback"));
        var calls = fixture.Calls.ToArray();
        Assert.That(Array.LastIndexOf(calls, "Page.addScriptToEvaluateOnNewDocument"), Is.LessThan(Array.IndexOf(calls, "Target.closeTarget")));
        Assert.That(fixture.ScriptRegistrations, Is.EqualTo(4));
    }

    [TestCase(false), TestCase(true)]
    public async Task ScriptRemovalCapabilityIsRequiredOnlyByTheOptInProfile(bool profile)
    {
        await using var fixture = await Fixture.StartAsync(); fixture.OmitScriptRemovalMethod = true;
        await using var controller = new CdpController(fixture.Endpoint, ["caller"], false, profile);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        if (profile)
        {
            Assert.That(Assert.ThrowsAsync<BrowserDockException>(async () => await controller.InitializeAsync(deadline.Token))!.Category, Is.EqualTo(ErrorCategory.ProtocolError));
            Assert.That(fixture.ScriptRegistrations, Is.Zero);
        }
        else await controller.InitializeAsync(deadline.Token);
    }
}
