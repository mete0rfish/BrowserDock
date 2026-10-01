using NUnit.Framework;
using Legacy = BrowserDock.Legacy;

namespace BrowserDock.Tests;

// Exercises public preflight and facade translation, stopping accepted calls at
// a probe barrier. No Chrome/CDP/WebDriver is created or represented as healthy.
[TestFixture]
public sealed class NavigationOptionsTests
{
    private static readonly Uri Url = new("http://fixture.invalid/page");
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(5);

    private static IEnumerable<TestCaseData> RejectedCalls()
    {
        var attached = BrowserState.WebDriverAttached;
        var cdp = BrowserState.CdpOnly;
        var cases = new (string Name, BrowserState State, NavigationOptions Options)[]
        {
            ("standard-reconnect", attached, new() { ReconnectAfterNavigation = true }),
            ("standard-zero-delay", attached, new() { ReconnectDelay = TimeSpan.Zero }),
            ("standard-replacement", attached, new() { TargetPolicy = TargetPolicy.ReplaceControlled }),
            ("attached-disconnect-zero-delay", attached, new() { Mode = NavigationMode.Detached, ReconnectDelay = TimeSpan.Zero }),
            ("cdp-disconnect-positive-delay", cdp, new() { Mode = NavigationMode.Detached, ReconnectDelay = TimeSpan.FromSeconds(1), TargetPolicy = TargetPolicy.ReplaceControlled, Target = new("unselected") }),
            ("reconnect-negative-delay", attached, new() { Mode = NavigationMode.Detached, ReconnectAfterNavigation = true, ReconnectDelay = TimeSpan.FromTicks(-1) }),
            ("reconnect-whole-budget", attached, new() { Mode = NavigationMode.Detached, ReconnectAfterNavigation = true, ReconnectDelay = Budget }),
            ("reconnect-over-budget", cdp, new() { Mode = NavigationMode.Detached, ReconnectAfterNavigation = true, ReconnectDelay = Budget + TimeSpan.FromTicks(1) }),
            ("reconnect-unrepresentable-delay", attached, new() { Mode = NavigationMode.Detached, ReconnectAfterNavigation = true, ReconnectDelay = TimeSpan.MaxValue }),
            ("cdp-reconnect", cdp, new() { Mode = NavigationMode.CdpOnly, ReconnectAfterNavigation = true }),
            ("cdp-zero-delay", cdp, new() { Mode = NavigationMode.CdpOnly, ReconnectDelay = TimeSpan.Zero }),
            ("cdp-from-attached", attached, new() { Mode = NavigationMode.CdpOnly, TargetPolicy = TargetPolicy.ReplaceControlled }),
            ("standard-from-cdp", cdp, new()),
            ("invalid-mode", attached, new() { Mode = (NavigationMode)100 }),
            ("invalid-completion", attached, new() { WaitUntil = (NavigationWaitUntil)100 }),
            ("invalid-target-policy", attached, new() { TargetPolicy = (TargetPolicy)100 }),
        };
        foreach (var item in cases)
        foreach (var legacy in new[] { false, true })
            yield return new TestCaseData(item.State, item.Options, legacy).SetName($"Reject_{item.Name}_{(legacy ? "Legacy" : "Core")}");
    }

    [TestCaseSource(nameof(RejectedCalls))]
    public async Task InvalidOptionsFailBeforeProbeOrLifecycleMutation(BrowserState state, NavigationOptions options, bool legacy)
    {
        var probes = 0;
        await using var browser = Fixture(state, _ => probes++);
        var health = browser.Health;
        var snapshot = browser.TestSnapshot;
        var error = await Failure(Navigate(browser, options, legacy));
        var core = Unwrap(error, legacy);
        Assert.Multiple(() =>
        {
            Assert.That(core.Category, Is.EqualTo(ErrorCategory.ConfigurationError));
            Assert.That(core.OperationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(core.Diagnostic, Is.EqualTo(health));
            Assert.That(core.BrowserMayHaveAdvanced, Is.False);
            Assert.That(core.CleanupFailures, Is.Empty);
            Assert.That(probes, Is.Zero);
            Assert.That(browser.Health, Is.EqualTo(health));
            Assert.That(browser.TestSnapshot, Is.EqualTo(snapshot));
        });
        // A rejection must release lifecycle ownership for the next operation.
        var next = state == BrowserState.WebDriverAttached ? new NavigationOptions()
            : new NavigationOptions { Mode = NavigationMode.CdpOnly };
        var accepted = Unwrap(await Failure(Navigate(browser, next, legacy)), legacy);
        Assert.That(accepted.Category, Is.EqualTo(ErrorCategory.ProtocolError));
        Assert.That(probes, Is.EqualTo(1));
    }

    private static IEnumerable<TestCaseData> AcceptedCalls()
    {
        var cases = new (string Name, BrowserState State, NavigationOptions Options)[]
        {
            ("standard", BrowserState.WebDriverAttached, new()),
            ("attached-disconnect", BrowserState.WebDriverAttached, new() { Mode = NavigationMode.Detached }),
            ("cdp-disconnect-replace", BrowserState.CdpOnly, new() { Mode = NavigationMode.Detached, TargetPolicy = TargetPolicy.ReplaceControlled }),
            ("attached-reconnect", BrowserState.WebDriverAttached, new() { Mode = NavigationMode.Detached, ReconnectAfterNavigation = true }),
            ("cdp-reconnect-zero", BrowserState.CdpOnly, new() { Mode = NavigationMode.Detached, ReconnectAfterNavigation = true, ReconnectDelay = TimeSpan.Zero }),
            ("attached-reconnect-positive-replace", BrowserState.WebDriverAttached, new() { Mode = NavigationMode.Detached, ReconnectAfterNavigation = true, ReconnectDelay = TimeSpan.FromSeconds(1), TargetPolicy = TargetPolicy.ReplaceControlled }),
            ("cdp-reconnect-before-budget", BrowserState.CdpOnly, new() { Mode = NavigationMode.Detached, ReconnectAfterNavigation = true, ReconnectDelay = Budget - TimeSpan.FromTicks(1) }),
            ("cdp", BrowserState.CdpOnly, new() { Mode = NavigationMode.CdpOnly }),
            ("chrome-ready-replace", BrowserState.ChromeReady, new() { Mode = NavigationMode.CdpOnly, TargetPolicy = TargetPolicy.ReplaceControlled }),
        };
        foreach (var item in cases)
        foreach (NavigationWaitUntil wait in Enum.GetValues(typeof(NavigationWaitUntil)))
        foreach (var legacy in new[] { false, true })
            yield return new TestCaseData(item.State, item.Options with { WaitUntil = wait }, legacy)
                .SetName($"Accept_{item.Name}_{wait}_{(legacy ? "Legacy" : "Core")}");
    }

    [TestCaseSource(nameof(AcceptedCalls))]
    public async Task AcceptedOptionsReachProbeBarrier(BrowserState state, NavigationOptions options, bool legacy)
    {
        var probes = 0;
        await using var browser = Fixture(state, _ => probes++);
        var health = browser.Health;
        var error = Unwrap(await Failure(Navigate(browser, options, legacy)), legacy);
        Assert.That(error.Category, Is.EqualTo(ErrorCategory.ProtocolError));
        Assert.That(probes, Is.EqualTo(1));
        Assert.That(browser.Health, Is.EqualTo(health));
    }

    [Test]
    public void StateMatrixRetainsSupportedInitialStates()
    {
        var supported = new Dictionary<NavigationMode, BrowserState[]>
        {
            [NavigationMode.Standard] = new[] { BrowserState.WebDriverAttached },
            [NavigationMode.Detached] = new[] { BrowserState.WebDriverAttached, BrowserState.CdpOnly },
            [NavigationMode.CdpOnly] = new[] { BrowserState.CdpOnly, BrowserState.ChromeReady },
        };
        foreach (var row in supported)
        foreach (BrowserState state in Enum.GetValues(typeof(BrowserState)))
        {
            var options = new NavigationOptions { Mode = row.Key };
            if (row.Value.Contains(state)) Assert.DoesNotThrow(() => Browser.ValidateNavigation(state, options, Budget));
            else Assert.That(Assert.Throws<BrowserDockException>(() => Browser.ValidateNavigation(state, options, Budget))!.Category,
                Is.EqualTo(ErrorCategory.ConfigurationError));
        }
    }

    [TestCase(false), TestCase(true)]
    public async Task DelayUsesCapturedNavigationBudgetRatherThanReconnectTimeout(bool legacy)
    {
        var probes = 0;
        await using var browser = Fixture(BrowserState.CdpOnly, _ => probes++, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(1));
        var accepted = new NavigationOptions { Mode = NavigationMode.Detached, ReconnectAfterNavigation = true, ReconnectDelay = TimeSpan.FromSeconds(61) };
        Assert.That(Unwrap(await Failure(Navigate(browser, accepted, legacy)), legacy).Category, Is.EqualTo(ErrorCategory.ProtocolError));
        foreach (var delay in new[] { TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(121) })
            Assert.That(Unwrap(await Failure(Navigate(browser, accepted with { ReconnectDelay = delay }, legacy)), legacy).Category,
                Is.EqualTo(ErrorCategory.ConfigurationError));
        Assert.That(probes, Is.EqualTo(1));
    }

    [TestCase(false), TestCase(true)]
    public async Task LeaseNavigationKeepsDefaultsAndRejectsDisposedLease(bool legacy)
    {
        var probes = 0;
        await using var browser = Fixture(BrowserState.WebDriverAttached, _ => probes++);
        var lease = new WebDriverLease(browser, browser.SessionGeneration, browser.AttachmentEpoch);
        Task NavigateLease() => legacy ? new Legacy.GuardedCommands(lease.Commands).NavigateAsync(Url) : lease.Commands.NavigateAsync(Url).AsTask();
        Assert.That(Unwrap(await Failure(NavigateLease()), legacy).Category, Is.EqualTo(ErrorCategory.ProtocolError));
        await lease.DisposeAsync();
        Assert.That(Unwrap(await Failure(NavigateLease()), legacy).Category, Is.EqualTo(ErrorCategory.StaleAttachment));
        Assert.That(probes, Is.EqualTo(1));
    }

    private static Browser Fixture(BrowserState state, Action<CancellationToken> beforeProbe, TimeSpan? navigation = null, TimeSpan? reconnect = null)
        => Browser.NavigationFixtureForTest(new()
        {
            Driver = new() { ExecutablePath = "unused" },
            Timeouts = new() { Navigation = navigation ?? Budget, Reconnect = reconnect ?? TimeSpan.FromSeconds(45) },
            StageHook = (stage, token) =>
            {
                if (stage == "navigation-probe")
                {
                    beforeProbe(token);
                    throw new BrowserDockException(ErrorCategory.ProtocolError, "Portable preflight probe barrier.");
                }
                return default;
            },
        }, state);

    private static Task Navigate(Browser browser, NavigationOptions options, bool legacy)
        => legacy ? new Legacy.Browser(browser).NavigateAsync(Url, new()
        {
            Mode = (Legacy.NavigationMode)options.Mode, ReconnectAfterNavigation = options.ReconnectAfterNavigation,
            ReconnectDelay = options.ReconnectDelay, WaitUntil = (Legacy.NavigationWaitUntil)options.WaitUntil,
            TargetPolicy = (Legacy.TargetPolicy)options.TargetPolicy,
            Target = options.Target is { } target ? new Legacy.TargetKey(target.Value) : null,
        }) : browser.NavigateAsync(Url, options).AsTask();

    private static async Task<Exception> Failure(Task task)
    {
        try { await TaskCompatibility.WaitAsync(task, Watchdog); }
        catch (Exception e) when (e is BrowserDockException or Legacy.BrowserDockException) { return e; }
        throw new AssertionException("Expected the configuration error or probe barrier.");
    }
    private static BrowserDockException Unwrap(Exception error, bool legacy)
    {
        if (!legacy) return (BrowserDockException)error;
        var translated = (Legacy.BrowserDockException)error;
        var core = (BrowserDockException)translated.InnerException!;
        Assert.That((int)translated.Category, Is.EqualTo((int)core.Category));
        Assert.That(translated.OperationId, Is.EqualTo(core.OperationId));
        Assert.That(translated.Diagnostic?.State, Is.EqualTo(core.Diagnostic is { } health ? (Legacy.BrowserState?)health.State : null));
        return core;
    }
}
