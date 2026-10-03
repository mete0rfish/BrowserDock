using BrowserDock.Cdp;
using NUnit.Framework;
using Legacy = BrowserDock.Legacy;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class UcWorkflowTests
{
    private static readonly Uri Url = new("http://fixture/page");
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    [TestCase("Reconnect", true, false, true), TestCase("Reconnect", false, false, true)]
    [TestCase("Open", true, false, true), TestCase("Open", false, false, false)]
    [TestCase("OpenWithTab", true, true, true), TestCase("OpenWithTab", false, true, false)]
    [TestCase("OpenWithReconnect", true, true, true), TestCase("OpenWithReconnect", false, true, true)]
    [TestCase("OpenWithDisconnect", true, true, false), TestCase("OpenWithDisconnect", false, true, false)]
    public async Task OrdersLifecycleAndPreservesExplicitReturnPolicy(string name, bool attached, bool replaces, bool reconnects)
    {
        var context = new Context(attached);
        var target = new TargetKey("selected");
        var operation = (UcOperation)Enum.Parse(typeof(UcOperation), name);
        var result = await Run(context, operation, new() { Target = target });
        var expected = new List<string> { "prepare:selected", attached ? "disconnect" : "confirm-disconnected" };
        if (replaces) expected.Add("replace");
        if (operation != UcOperation.Reconnect) expected.Add("navigate:Load");
        if (reconnects) expected.Add("attach");
        Assert.That(context.Calls, Is.EqualTo(expected));
        Assert.That(context.State, Is.EqualTo(reconnects ? BrowserState.WebDriverAttached : BrowserState.CdpOnly));
        Assert.That(context.Generation, Is.EqualTo((attached ? 1 : 0) + (reconnects ? 1 : 0)));
        Assert.That(result?.Url, operation == UcOperation.Reconnect ? Is.Null : Is.EqualTo(Url));
    }

    [TestCase(2000, 0, 1000), TestCase(5000, 0, 0), TestCase(2000, 1000, 0), TestCase(0, 500, 2500)]
    public async Task MinimumDurationIncludesNavigationAndPostExitProbe(int navigationMs, int probeMs, int expectedHoldMs)
    {
        var context = new Context(true) { NavigationTime = TimeSpan.FromMilliseconds(navigationMs), PostExitProbe = TimeSpan.FromMilliseconds(probeMs) };
        await Run(context, UcOperation.OpenWithReconnect, duration: TimeSpan.FromSeconds(3));
        Assert.That(context.Delays.Sum(x => x.TotalMilliseconds), Is.EqualTo(expectedHoldMs));
        Assert.That(context.AttachedAt - context.DisconnectedAt, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(3).Ticks));
    }

    [Test]
    public async Task DisconnectedCallerGetsANewDurationOrigin()
    {
        var context = new Context(false) { Now = TimeSpan.FromDays(1).Ticks };
        await Run(context, UcOperation.Reconnect, duration: TimeSpan.FromSeconds(3));
        Assert.That(context.Delays.Single(), Is.EqualTo(TimeSpan.FromSeconds(3)));
    }

    [TestCase(NavigationWaitUntil.Commit), TestCase(NavigationWaitUntil.DOMContentLoaded), TestCase(NavigationWaitUntil.Load), TestCase(NavigationWaitUntil.NetworkIdle)]
    public async Task PassesOnlyRequestedMilestoneAndPreservesTerminalResult(NavigationWaitUntil wait)
    {
        var context = new Context(true) { Result = new(new Uri("http://fixture/final"), new[] { "http://fixture/redirect" }, NavigationOutcome.Download) };
        var result = await Run(context, UcOperation.OpenWithDisconnect, new() { WaitUntil = wait }, TimeSpan.FromSeconds(1));
        Assert.That(context.Calls, Does.Contain("navigate:" + wait));
        Assert.That(context.Calls, Does.Not.Contain("attach"));
        Assert.That(result, Is.SameAs(context.Result));
        Assert.That(context.Delays.Single(), Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [TestCase("uc-prepare", false), TestCase("uc-disconnect", false), TestCase("uc-detached", true)]
    [TestCase("uc-replace", true), TestCase("uc-navigation", true), TestCase("uc-navigation-completed", true)]
    [TestCase("uc-hold", true), TestCase("uc-reconnect", true)]
    public void CancellationStopsLaterStagesAndPreservesCause(string stage, bool advanced)
    {
        using var caller = new CancellationTokenSource();
        var context = new Context(true) { Stage = s => { if (s == stage) caller.Cancel(); } };
        var error = Assert.ThrowsAsync<NavigationCanceledException>(async () =>
            await Run(context, UcOperation.OpenWithReconnect, duration: TimeSpan.FromSeconds(1), token: caller.Token))!;
        Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
        Assert.That(error.InnerException, Is.InstanceOf<OperationCanceledException>());
        Assert.That(error.BrowserMayHaveAdvanced, Is.EqualTo(advanced));
        Assert.That(error.Data["Navigation.Stage"], Is.EqualTo(stage));
        Assert.That(context.Calls, Does.Not.Contain("attach"));
        Assert.That(context.State, Is.EqualTo(advanced ? BrowserState.CdpOnly : BrowserState.WebDriverAttached));
    }

    [TestCase("prepare:selected", false), TestCase("disconnect", true), TestCase("replace", true), TestCase("navigate:Load", true), TestCase("attach", true)]
    public void FailuresAreNotReplayedAndRetainPartialProgress(string failedCall, bool advanced)
    {
        var original = new BrowserDockException(ErrorCategory.ProtocolError, "injected");
        var context = new Context(true) { FailCall = failedCall, Failure = original };
        var error = Assert.ThrowsAsync<BrowserDockException>(async () => await Run(context, UcOperation.OpenWithReconnect, new() { Target = new("selected") }))!;
        Assert.That(error, Is.SameAs(original));
        Assert.That(error.BrowserMayHaveAdvanced, Is.EqualTo(advanced));
        Assert.That(context.Calls.Count(x => x == failedCall), Is.EqualTo(1));
        Assert.That(context.Calls.Last(), Is.EqualTo(failedCall));
    }

    [Test]
    public async Task MissingMilestoneCannotReconnectWhenDurationHasElapsed()
    {
        using var canceled = new CancellationTokenSource();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new Context(true) { Navigation = async token => { entered.TrySetResult(true); await Task.Delay(Timeout.Infinite, token); } };
        var pending = Run(context, UcOperation.OpenWithReconnect, duration: TimeSpan.FromSeconds(1), token: canceled.Token);
        try
        {
            await TaskCompatibility.WaitAsync(entered.Task, TimeSpan.FromSeconds(5));
            context.Now += TimeSpan.FromSeconds(20).Ticks;
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(context.Calls, Does.Not.Contain("attach"));
            canceled.Cancel();
            Assert.ThrowsAsync<NavigationCanceledException>(async () => await pending);
        }
        finally { canceled.Cancel(); }
    }

    [TestCase(false), TestCase(true)]
    public async Task PublicFacadeRejectsInvalidDurationBeforeProbe(bool legacy)
    {
        var probes = 0;
        await using var browser = Browser.NavigationFixtureForTest(new()
        {
            Driver = new() { ExecutablePath = "unused" }, Timeouts = new() { Navigation = Budget, Reconnect = TimeSpan.FromSeconds(2) },
            StageHook = (stage, _) => { if (stage == "uc-prepare") { probes++; throw new InvalidOperationException("must not reach probe"); } return default; }
        }, BrowserState.WebDriverAttached);
        var health = browser.Health;
        foreach (var operation in new[] { UcOperation.Reconnect, UcOperation.OpenWithReconnect, UcOperation.OpenWithDisconnect })
        foreach (var duration in new[] { TimeSpan.FromTicks(-1), operation == UcOperation.Reconnect ? TimeSpan.FromSeconds(2) : Budget, TimeSpan.MaxValue })
        {
            var error = Assert.CatchAsync<Exception>(async () => await PublicCall(browser, legacy, operation, duration))!;
            var core = legacy ? (BrowserDockException)error.InnerException! : (BrowserDockException)error;
            Assert.That(core.Category, Is.EqualTo(ErrorCategory.ConfigurationError));
            Assert.That(core.BrowserMayHaveAdvanced, Is.False);
            Assert.That(core.OperationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(browser.Health, Is.EqualTo(health));
        }
        Assert.That(probes, Is.Zero);
    }

    [TestCase(false), TestCase(true)]
    public async Task PublicWorkflowOwnsOneGateAndDeadline(bool legacy)
    {
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probes = 0;
        await using var browser = Browser.NavigationFixtureForTest(new()
        {
            Driver = new() { ExecutablePath = "unused" }, Timeouts = new() { Reconnect = TimeSpan.FromMilliseconds(200) },
            StageHook = async (stage, token) => { if (stage == "uc-prepare") { Interlocked.Increment(ref probes); entered.TrySetResult(true); await Task.Delay(Timeout.Infinite, token); } }
        }, BrowserState.WebDriverAttached);
        var first = PublicCall(browser, legacy, UcOperation.OpenWithReconnect, TimeSpan.Zero, cancel.Token);
        try
        {
            await TaskCompatibility.WaitAsync(entered.Task, TimeSpan.FromSeconds(5));
            var failure = Assert.CatchAsync<Exception>(async () => await PublicCall(browser, legacy, UcOperation.Reconnect, TimeSpan.Zero))!;
            var core = legacy ? (BrowserDockException)failure.InnerException! : (BrowserDockException)failure;
            Assert.That(core.Category, Is.EqualTo(ErrorCategory.OperationTimedOut));
            Assert.That(core.BrowserMayHaveAdvanced, Is.False);
            Assert.That(probes, Is.EqualTo(1), "Queued workflow dispatched before lifecycle ownership.");
            cancel.Cancel();
            var canceled = Assert.CatchAsync<OperationCanceledException>(async () => await first)!;
            Assert.That(canceled.CancellationToken, Is.EqualTo(cancel.Token));
            Assert.That(canceled.Data["Navigation.Stage"], Is.EqualTo("uc-prepare"));
            Assert.That(browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
        }
        finally { cancel.Cancel(); try { await first; } catch (OperationCanceledException) { } }
    }

    [TestCase(false), TestCase(true)]
    public async Task TimeoutDuringWorkflowKeepsStageAndCallerCancellationIsDistinct(bool legacy)
    {
        await using var browser = Browser.NavigationFixtureForTest(new()
        {
            Driver = new() { ExecutablePath = "unused" }, Timeouts = new() { Reconnect = TimeSpan.FromMilliseconds(100) },
            StageHook = async (stage, token) => { if (stage == "uc-prepare") await Task.Delay(Timeout.Infinite, token); }
        }, BrowserState.WebDriverAttached);
        var error = Assert.CatchAsync<Exception>(async () => await PublicCall(browser, legacy, UcOperation.Reconnect, TimeSpan.Zero))!;
        var core = legacy ? (BrowserDockException)error.InnerException! : (BrowserDockException)error;
        Assert.That(core.Category, Is.EqualTo(ErrorCategory.OperationTimedOut));
        Assert.That(error.Data["Navigation.UcOperation"], Is.EqualTo("Reconnect"));
        Assert.That(error.Data["Navigation.Stage"], Is.EqualTo("uc-prepare"));
    }

    private static Task<PageResult?> Run(Context context, UcOperation operation, UcNavigationOptions? options = null, TimeSpan duration = default, CancellationToken token = default)
        => UcWorkflow.RunAsync(context, operation, operation == UcOperation.Reconnect ? null : Url, options ?? new(), duration, Budget, token, token);
    private static Task PublicCall(Browser browser, bool legacy, UcOperation operation, TimeSpan duration, CancellationToken token = default)
    {
        if (legacy)
        {
            var uc = new Legacy.Browser(browser).Uc;
            return operation switch
            {
                UcOperation.Reconnect => uc.ReconnectAsync(duration, token),
                UcOperation.OpenWithDisconnect => uc.OpenWithDisconnectAsync(Url, duration, cancellationToken: token),
                _ => uc.OpenWithReconnectAsync(Url, duration, cancellationToken: token)
            };
        }
        return operation switch
        {
            UcOperation.Reconnect => browser.Uc.ReconnectAsync(duration, token).AsTask(),
            UcOperation.OpenWithDisconnect => browser.Uc.OpenWithDisconnectAsync(Url, duration, cancellationToken: token).AsTask(),
            _ => browser.Uc.OpenWithReconnectAsync(Url, duration, cancellationToken: token).AsTask()
        };
    }
    private sealed class Context(bool attached) : IUcContext
    {
        public BrowserState State { get; private set; } = attached ? BrowserState.WebDriverAttached : BrowserState.CdpOnly;
        public long Generation = attached ? 1 : 0;
        public long Now, DisconnectedAt, AttachedAt;
        public TimeSpan NavigationTime, PostExitProbe;
        public List<string> Calls = new();
        public List<TimeSpan> Delays = new();
        public Action<string>? Stage;
        public string? FailCall;
        public Exception Failure = new InvalidOperationException();
        public Func<CancellationToken, Task>? Navigation;
        public PageResult Result = new(Url, Array.Empty<string>(), NavigationOutcome.Completed);
        private void Call(string value) { Calls.Add(value); if (value == FailCall) throw Failure; }
        public Task PrepareAsync(TargetKey? target, CancellationToken token) { Call("prepare:" + target?.Value); return Task.CompletedTask; }
        public Task<long> DetachAsync(CancellationToken token)
        {
            Call(State == BrowserState.WebDriverAttached ? "disconnect" : "confirm-disconnected");
            State = BrowserState.CdpOnly; DisconnectedAt = Now; Now += PostExitProbe.Ticks;
            return Task.FromResult(DisconnectedAt);
        }
        public Task ReplaceAsync(CancellationToken token) { Call("replace"); return Task.CompletedTask; }
        public async Task<PageResult> NavigateAsync(Uri url, NavigationWaitUntil wait, CancellationToken token)
        { Call("navigate:" + wait); if (Navigation is not null) await Navigation(token); Now += NavigationTime.Ticks; return Result; }
        public Task AttachAsync(CancellationToken token)
        { Call("attach"); State = BrowserState.WebDriverAttached; Generation++; AttachedAt = Now; return Task.CompletedTask; }
        public TimeSpan Elapsed(long since) => TimeSpan.FromTicks(Now - since);
        public Task DelayAsync(TimeSpan duration, CancellationToken token) { token.ThrowIfCancellationRequested(); Delays.Add(duration); Now += duration.Ticks; return Task.CompletedTask; }
        public ValueTask StageAsync(string stage, CancellationToken token) { Stage?.Invoke(stage); return default; }
    }
}
