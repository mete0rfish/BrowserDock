using System.Diagnostics;
using System.Text.Json;
using BrowserDock.Hosting;
using BrowserDock.WebDriver;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using Legacy = BrowserDock.Legacy;

namespace BrowserDock.Tests;

// Uses the actual Browser command path, a real owned fixture process and an
// in-memory W3C executor. No Chrome, Windows listener checks or remote driver.
[TestFixture]
public sealed class CommandDeadlineTests
{
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(15);
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task<T> Bounded<T>(Task<T> work) => TaskCompatibility.WaitAsync(work, Watchdog);
    private static Task Bounded(Task work) => TaskCompatibility.WaitAsync(work, Watchdog);

    [Test]
    public async Task DriverCdcCleanupUsesDriverSessionAndOwnsOnlyItsPerWindowIdentifiers()
    {
        await using var fixture = await CommandFixture.StartAsync();
        var calls = new List<string>(); var sequence = 0;
        fixture.Executor.Cdp = command =>
        {
            Assert.That(command.SessionId!.ToString(), Is.EqualTo("fixture-session"));
            var method = (string)command.Parameters!["cmd"]!;
            var args = JsonSerializer.SerializeToElement(command.Parameters["params"]);
            if (method == "Page.addScriptToEvaluateOnNewDocument")
            {
                calls.Add("add:" + args.GetProperty("source").GetString());
                return new Response("fixture-session", new Dictionary<string, object> { ["identifier"] = "owned-" + ++sequence }, WebDriverResult.Success);
            }
            calls.Add("remove:" + args.GetProperty("identifier").GetString());
            return new Response("fixture-session", new Dictionary<string, object>(), WebDriverResult.Success);
        };
        await fixture.Attachment.PrepareCdcCleanupAsync("window-a", null, CancellationToken.None);
        Assert.That(fixture.Executor.CdpCommandsAdded, Is.Zero, "Disabled cleanup must not use the driver CDP bridge.");
        await fixture.Attachment.PrepareCdcCleanupAsync("window-a", "cleanup-a", CancellationToken.None);
        await fixture.Attachment.PrepareCdcCleanupAsync("window-a", "cleanup-a", CancellationToken.None);
        await fixture.Attachment.PrepareCdcCleanupAsync("window-b", "cleanup-b", CancellationToken.None);
        await fixture.Attachment.PrepareCdcCleanupAsync("window-a", "cleanup-new", CancellationToken.None);
        Assert.That(calls, Is.EqualTo(new[] { "add:cleanup-a", "add:cleanup-b", "remove:owned-1", "add:cleanup-new" }));
        Assert.That(fixture.Executor.CdpCommandsAdded, Is.EqualTo(1));
    }

    [TestCase("rejected"), TestCase("missing-id"), TestCase("canceled")]
    public async Task UncertainDriverScriptRegistrationStopsItsAttachment(string failure)
    {
        await using var fixture = await CommandFixture.StartAsync();
        using var canceled = new CancellationTokenSource();
        fixture.Executor.Cdp = _ =>
        {
            if (failure == "canceled") { canceled.Cancel(); throw new OperationCanceledException(canceled.Token); }
            return new Response("fixture-session", new Dictionary<string, object>(),
                failure == "rejected" ? WebDriverResult.UnknownError : WebDriverResult.Success);
        };
        var error = Assert.CatchAsync(async () => await Bounded(fixture.Attachment.PrepareCdcCleanupAsync("window", "cleanup", canceled.Token)));
        if (failure == "canceled") Assert.That(error, Is.InstanceOf<OperationCanceledException>());
        else Assert.That(error, Is.InstanceOf<BrowserDockException>());
        Assert.That(fixture.Executor.Disposals, Is.EqualTo(1));
        await Bounded(fixture.Attachment.Process.WaitAsync(CancellationToken.None));
        Assert.That(fixture.Attachment.Process.Alive, Is.False);
        var sent = fixture.Attachment.Executor.Sent;
        Assert.ThrowsAsync<BrowserDockException>(async () => await fixture.Attachment.PrepareCdcCleanupAsync("window", "cleanup", CancellationToken.None));
        Assert.That(fixture.Attachment.Executor.Sent, Is.EqualTo(sent), "An uncertain mutation must never be retried against the same attachment.");
    }

    [TestCase(false), TestCase(true)]
    public async Task QueuedDeadlinePreservesAttachmentAndGateOwnership(bool legacy)
    {
        // Holding the gate guarantees expiry. Keep the normal command budget so
        // the successful follow-up also tolerates dispatch delays on hosted runners.
        await using var fixture = await CommandFixture.StartAsync();
        var epoch = fixture.Browser.AttachmentEpoch;
        await fixture.Browser.CommandsGateForTest.WaitAsync();
        try
        {
            var error = await Catch(fixture.Title(legacy));
            Assert.Multiple(() =>
            {
                AssertTimeout(error, legacy);
                Assert.That(fixture.Executor.Commands, Is.Zero);
                Assert.That(fixture.Executor.Disposals, Is.Zero);
                Assert.That(fixture.Browser.CommandsGateForTest.CurrentCount, Is.Zero);
                Assert.That(fixture.Browser.AttachmentEpoch, Is.EqualTo(epoch));
                Assert.That(fixture.Browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
                Assert.That(fixture.Attachment.Process.Alive, Is.True);
            });
        }
        finally { fixture.Browser.CommandsGateForTest.Release(); }
        Assert.That(await Bounded(fixture.Title(legacy)), Is.EqualTo("fixture"));
        Assert.That(fixture.Executor.Commands, Is.EqualTo(1));
    }

    [TestCase(false), TestCase(true)]
    public async Task QueuedCallerCancellationPreservesTokenAndDiagnostics(bool legacy)
    {
        await using var fixture = await CommandFixture.StartAsync();
        using var caller = new CancellationTokenSource();
        var epoch = fixture.Browser.AttachmentEpoch;
        await fixture.Browser.CommandsGateForTest.WaitAsync();
        try
        {
            var pending = fixture.Title(legacy, caller.Token);
            caller.Cancel();
            var error = (OperationCanceledException)await Catch(pending);
            Assert.Multiple(() =>
            {
                Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
                Assert.That(error.InnerException, Is.InstanceOf<OperationCanceledException>());
                Assert.That(error.Data["OperationId"], Is.Not.EqualTo(Guid.Empty));
                Assert.That(error.Data["Diagnostic"], Is.InstanceOf<BrowserHealthSnapshot>());
                Assert.That(error.Data["CleanupFailures"], Is.Empty);
                Assert.That(fixture.Browser.AttachmentEpoch, Is.EqualTo(epoch));
                Assert.That(fixture.Executor.Commands, Is.Zero);
                Assert.That(fixture.Executor.Disposals, Is.Zero);
                Assert.That(fixture.Browser.CommandsGateForTest.CurrentCount, Is.Zero);
            });
        }
        finally { fixture.Browser.CommandsGateForTest.Release(); }
        Assert.That(await Bounded(fixture.Title(legacy)), Is.EqualTo("fixture"));
    }

    [Test]
    public async Task DisposedLeaseWhileQueuedDoesNotDispatchOrRecover()
    {
        await using var fixture = await CommandFixture.StartAsync();
        await fixture.Browser.CommandsGateForTest.WaitAsync();
        var pending = fixture.Title(false);
        await fixture.Lease.DisposeAsync();
        fixture.Browser.CommandsGateForTest.Release();
        var error = (BrowserDockException)await Catch(pending);
        Assert.That(error.Category, Is.EqualTo(ErrorCategory.StaleAttachment));
        Assert.That(error.OperationId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(fixture.Executor.Commands, Is.Zero);
        Assert.That(fixture.Executor.Disposals, Is.Zero);
        Assert.That(fixture.Browser.CommandsGateForTest.CurrentCount, Is.EqualTo(1));
        Assert.That(await Bounded(fixture.Browser.LeaseForTest().Commands.GetTitleAsync().AsTask()), Is.EqualTo("fixture"));
    }

    [TestCase(false), TestCase(true)]
    public async Task RunningDeadlineDisposesExactAttachmentAndReportsOriginalError(bool legacy)
    {
        var entered = Signal(); var release = Signal();
        await using var fixture = await CommandFixture.StartAsync(TimeSpan.FromSeconds(1));
        fixture.Executor.Title = async () => { entered.TrySetResult(true); await release.Task; return "late"; };
        var pending = fixture.Title(legacy);
        Exception error;
        try { await Bounded(entered.Task); error = await Catch(pending); }
        finally { release.TrySetResult(true); }
        AssertTimeout(error, legacy);
        Assert.That(fixture.Browser.State, Is.EqualTo(BrowserState.CdpOnly));
        Assert.That(fixture.Executor.Disposals, Is.EqualTo(1));
        Assert.That(fixture.Attachment.Process.Alive, Is.False);
        Assert.That(fixture.Browser.CommandsGateForTest.CurrentCount, Is.EqualTo(1));
    }

    [TestCase(false), TestCase(true)]
    public async Task RecoveryGateContentionIsBoundedAndDoesNotReleaseAnUnownedGate(bool legacy)
    {
        var recovering = Signal();
        await using var fixture = await CommandFixture.StartAsync(cleanupBudget: TimeSpan.FromMilliseconds(200),
            stage: (name, _) => { if (name == "command-recovery") recovering.TrySetResult(true); return default; });
        fixture.Executor.Title = () => Task.FromException<string>(new WebDriverException("driver lost"));
        await fixture.Browser.LifecycleGateForTest.WaitAsync();
        try
        {
            var pending = fixture.Title(legacy);
            await Bounded(recovering.Task);
            var error = await Catch(pending);
            Assert.Multiple(() =>
            {
                Assert.That(Category(error), Is.EqualTo((int)ErrorCategory.AttachmentLost));
                Assert.That(Cleanup(error), Has.Some.StartsWith("lifecycle:"));
                Assert.That(fixture.Browser.State, Is.EqualTo(BrowserState.Faulted));
                Assert.That(fixture.Browser.LifecycleGateForTest.CurrentCount, Is.Zero);
                Assert.That(fixture.Browser.CommandsGateForTest.CurrentCount, Is.EqualTo(1));
                Assert.That(fixture.Executor.Disposals, Is.EqualTo(1));
            });
            var rejected = await Catch(fixture.Title(legacy));
            Assert.That(Category(rejected), Is.EqualTo((int)ErrorCategory.StaleAttachment));
            Assert.That(fixture.Executor.Commands, Is.EqualTo(1));
        }
        finally { fixture.Browser.LifecycleGateForTest.Release(); }
    }

    [Test]
    public async Task RecoveryUsesRemainingBudgetForProbeAndPreservesCleanupDiagnostics()
    {
        var probing = Signal();
        CancellationToken recoveryToken = default;
        await using var fixture = await CommandFixture.StartAsync(cleanupBudget: TimeSpan.FromMilliseconds(200),
            stage: (name, token) => { if (name == "command-recovery") recoveryToken = token; return default; },
            probe: async token =>
            {
                Assert.That(token, Is.EqualTo(recoveryToken), "Probe must retain the original recovery scope.");
                probing.TrySetResult(true); await Task.Delay(Timeout.Infinite, token);
            });
        fixture.Executor.Title = () => Task.FromException<string>(new WebDriverException("driver lost"));
        var pending = fixture.Title(false);
        await Bounded(probing.Task);
        var error = (BrowserDockException)await Catch(pending);
        Assert.That(error.Category, Is.EqualTo(ErrorCategory.AttachmentLost));
        Assert.That(error.InnerException, Is.TypeOf<WebDriverException>());
        Assert.That(error.CleanupFailures, Has.Some.StartsWith("driver/probe:"));
        Assert.That(error.Diagnostic!.State, Is.EqualTo(BrowserState.Faulted));
        Assert.That(fixture.Browser.Health.CleanupFailures, Is.Not.Empty);
        Assert.That(fixture.Browser.LifecycleGateForTest.CurrentCount, Is.EqualTo(1));
    }

    [Test]
    public async Task OldEpochFailureCannotInvalidateOrDestroyReplacement()
    {
        var entered = Signal(); var release = Signal();
        await using var fixture = await CommandFixture.StartAsync();
        fixture.Executor.Title = async () => { entered.TrySetResult(true); await release.Task; throw new WebDriverException("old failure"); };
        var pending = fixture.Title(false);
        await Bounded(entered.Task);
        var replacement = await fixture.AddAttachmentAsync();
        fixture.Browser.ReplaceAttachmentForTest(replacement.Attachment);
        var epoch = fixture.Browser.AttachmentEpoch;
        release.TrySetResult(true);
        var error = (BrowserDockException)await Catch(pending);
        Assert.That(error.Category, Is.EqualTo(ErrorCategory.AttachmentLost));
        Assert.That(fixture.Browser.AttachmentEpoch, Is.EqualTo(epoch));
        Assert.That(fixture.Browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
        Assert.That(replacement.Executor.Disposals, Is.Zero);
        Assert.That(replacement.Attachment.Process.Alive, Is.True);
        Assert.That(await Bounded(fixture.Browser.LeaseForTest().Commands.GetTitleAsync().AsTask()), Is.EqualTo("fixture"));
    }

    [Test]
    public async Task InvalidationPrecedesCommandGateReleaseWithoutTakingLifecycleGate()
    {
        var recovering = Signal(); var release = Signal();
        await using var fixture = await CommandFixture.StartAsync(stage: async (name, token) =>
        { if (name == "command-recovery") { recovering.TrySetResult(true); await TaskCompatibility.WaitAsync(release.Task, token); } });
        fixture.Executor.Title = () => Task.FromException<string>(new WebDriverException("driver lost"));
        var pending = fixture.Title(false);
        try
        {
            await Bounded(recovering.Task);
            Assert.That(fixture.Browser.CommandsGateForTest.CurrentCount, Is.EqualTo(1));
            Assert.That(fixture.Browser.LifecycleGateForTest.CurrentCount, Is.EqualTo(1));
            Assert.Throws<StaleAttachmentException>(() => fixture.Browser.LeaseForTest());
            var rejected = (BrowserDockException)await Catch(fixture.Title(false));
            Assert.That(rejected.Category, Is.EqualTo(ErrorCategory.StaleAttachment));
            Assert.That(fixture.Executor.Commands, Is.EqualTo(1));
        }
        finally { release.TrySetResult(true); }
        await Catch(pending);
    }

    [TestCase(false), TestCase(true)]
    public async Task ConcurrentStopAndDisposePreserveLifetimeCancellationAndSingleCleanupOwner(bool legacy)
    {
        var entered = Signal(); var release = Signal();
        await using var fixture = await CommandFixture.StartAsync();
        fixture.Executor.Title = async () => { entered.TrySetResult(true); await release.Task; return "late"; };
        var pending = fixture.Title(legacy);
        Exception error;
        try
        {
            await Bounded(entered.Task);
            var stopping = fixture.Browser.StopAsync().AsTask();
            var disposing = fixture.Browser.DisposeAsync().AsTask();
            error = await Catch(pending);
            await Bounded(Task.WhenAll(stopping, disposing));
        }
        finally { release.TrySetResult(true); }
        Assert.That(error, Is.InstanceOf<OperationCanceledException>());
        Assert.That(((OperationCanceledException)error).CancellationToken.IsCancellationRequested, Is.True);
        Assert.That(error.Data["OperationId"], Is.Not.EqualTo(Guid.Empty));
        Assert.That(fixture.Browser.State, Is.EqualTo(BrowserState.Stopped));
        Assert.That(fixture.Executor.Disposals, Is.EqualTo(1));
        Assert.That(fixture.Browser.CommandsGateForTest.CurrentCount, Is.EqualTo(1));
        Assert.That(fixture.Browser.LifecycleGateForTest.CurrentCount, Is.EqualTo(1));
    }

    [TestCase(false), TestCase(true)]
    public async Task RunningCallerCancellationPreservesTokenAndCompletesOwnedCleanup(bool legacy)
    {
        var entered = Signal(); var release = Signal();
        await using var fixture = await CommandFixture.StartAsync();
        using var caller = new CancellationTokenSource();
        fixture.Executor.Title = async () => { entered.TrySetResult(true); await release.Task; return "late"; };
        var pending = fixture.Title(legacy, caller.Token);
        Exception error;
        try
        {
            await Bounded(entered.Task);
            caller.Cancel();
            error = await Catch(pending);
        }
        finally { release.TrySetResult(true); }
        Assert.That(error, Is.InstanceOf<OperationCanceledException>());
        Assert.That(((OperationCanceledException)error).CancellationToken, Is.EqualTo(caller.Token));
        Assert.That(error.Data["CleanupFailures"], Is.Empty);
        Assert.That(fixture.Browser.State, Is.EqualTo(BrowserState.CdpOnly));
        Assert.That(fixture.Executor.Disposals, Is.EqualTo(1));
    }

    [Test]
    public async Task ReplacementDuringRecoveryBudgetExpiryKeepsNewAdmissionOpen()
    {
        var recovering = Signal();
        await using var fixture = await CommandFixture.StartAsync(cleanupBudget: TimeSpan.FromSeconds(3),
            stage: (name, _) => { if (name == "command-recovery") recovering.TrySetResult(true); return default; });
        fixture.Executor.Title = () => Task.FromException<string>(new WebDriverException("old failure"));
        await fixture.Browser.LifecycleGateForTest.WaitAsync();
        try
        {
            var pending = fixture.Title(false);
            await Bounded(recovering.Task);
            var replacement = await fixture.AddAttachmentAsync();
            fixture.Browser.ReplaceAttachmentForTest(replacement.Attachment);
            var epoch = fixture.Browser.AttachmentEpoch;
            var error = (BrowserDockException)await Catch(pending);
            Assert.That(error.CleanupFailures, Has.Some.StartsWith("lifecycle:"));
            Assert.That(fixture.Browser.AttachmentEpoch, Is.EqualTo(epoch));
            Assert.That(fixture.Browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
            Assert.That(replacement.Executor.Disposals, Is.Zero);
            Assert.That(await Bounded(fixture.Browser.LeaseForTest().Commands.GetTitleAsync().AsTask()), Is.EqualTo("fixture"));
        }
        finally { fixture.Browser.LifecycleGateForTest.Release(); }
    }

    [Test]
    public async Task CancellationAfterDeadlineClassificationDoesNotChangeOriginalReason()
    {
        var entered = Signal(); var recovering = Signal(); var releaseWork = Signal(); var releaseRecovery = Signal();
        using var caller = new CancellationTokenSource();
        await using var fixture = await CommandFixture.StartAsync(TimeSpan.FromSeconds(1), stage: async (name, token) =>
        { if (name == "command-recovery") { recovering.TrySetResult(true); await TaskCompatibility.WaitAsync(releaseRecovery.Task, token); } });
        fixture.Executor.Title = async () => { entered.TrySetResult(true); await releaseWork.Task; return "late"; };
        var pending = fixture.Title(false, caller.Token);
        try
        {
            await Bounded(entered.Task);
            await Bounded(recovering.Task);
            caller.Cancel();
            releaseRecovery.TrySetResult(true);
            var error = (BrowserDockException)await Catch(pending);
            Assert.That(error.Category, Is.EqualTo(ErrorCategory.OperationTimedOut));
            Assert.That(error.InnerException, Is.InstanceOf<OperationCanceledException>());
        }
        finally { releaseRecovery.TrySetResult(true); releaseWork.TrySetResult(true); }
    }

    [Test]
    public async Task BothCancellationSourcesPreferCallerAndDoNotTurnIntoTimeout()
    {
        await using var fixture = await CommandFixture.StartAsync();
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        await Bounded(fixture.Browser.StopAsync().AsTask());
        var error = (OperationCanceledException)await Catch(fixture.Title(false, caller.Token));
        Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
        Assert.That(fixture.Executor.Commands, Is.Zero);
    }

    [Test]
    public async Task ArgumentFailureRetainsHealthyAttachmentAndOriginalException()
    {
        await using var fixture = await CommandFixture.StartAsync();
        var original = new ArgumentException("bad command");
        fixture.Executor.Title = () => Task.FromException<string>(original);
        var epoch = fixture.Browser.AttachmentEpoch;
        var error = (BrowserDockException)await Catch(fixture.Title(false));
        Assert.That(error.Category, Is.EqualTo(ErrorCategory.ConfigurationError));
        Assert.That(error.InnerException, Is.SameAs(original));
        Assert.That(fixture.Executor.Disposals, Is.Zero);
        Assert.That(fixture.Browser.AttachmentEpoch, Is.EqualTo(epoch));
        fixture.Executor.Title = () => Task.FromResult("fixture");
        Assert.That(await Bounded(fixture.Title(false)), Is.EqualTo("fixture"));
    }

    [Test]
    public async Task TypedFailureRetainsExistingCleanupDetails()
    {
        await using var fixture = await CommandFixture.StartAsync();
        var original = new BrowserDockException(ErrorCategory.ProtocolError, "original") { CleanupFailures = new[] { "original cleanup" } };
        fixture.Executor.Title = () => Task.FromException<string>(original);
        var error = (BrowserDockException)await Catch(fixture.Title(false));
        Assert.That(error, Is.SameAs(original));
        Assert.That(error.CleanupFailures, Is.EqualTo(new[] { "original cleanup" }));
        Assert.That(error.OperationId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(fixture.Executor.Disposals, Is.Zero);
    }

    [Test]
    public async Task ReplacementWhileHealthProbeIsPendingCannotBeOverwritten()
    {
        var probing = Signal(); var release = Signal();
        await using var fixture = await CommandFixture.StartAsync(probe: async token =>
        { probing.TrySetResult(true); await TaskCompatibility.WaitAsync(release.Task, token); });
        fixture.Executor.Title = () => Task.FromException<string>(new WebDriverException("old failure"));
        var pending = fixture.Title(false);
        try
        {
            await Bounded(probing.Task);
            var replacement = await fixture.AddAttachmentAsync();
            fixture.Browser.ReplaceAttachmentForTest(replacement.Attachment);
            var epoch = fixture.Browser.AttachmentEpoch;
            release.TrySetResult(true);
            await Catch(pending);
            Assert.That(fixture.Browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
            Assert.That(fixture.Browser.AttachmentEpoch, Is.EqualTo(epoch));
            Assert.That(replacement.Executor.Disposals, Is.Zero);
            Assert.That(await Bounded(fixture.Browser.LeaseForTest().Commands.GetTitleAsync().AsTask()), Is.EqualTo("fixture"));
        }
        finally { release.TrySetResult(true); }
    }

    [TestCase(false, false, false), TestCase(true, false, false)]
    [TestCase(false, true, false), TestCase(true, true, false)]
    [TestCase(false, false, true), TestCase(true, false, true)]
    [TestCase(false, true, true), TestCase(true, true, true)]
    public async Task CancellationBeforeActionStartsPreservesAttachment(bool legacy, bool timeout, bool queuedWorker)
    {
        var entered = Signal(); var release = Signal();
        // The blocked stage/worker forces the timeout; a subsecond budget would
        // also constrain the successful follow-up and race Framework scheduling.
        await using var fixture = await CommandFixture.StartAsync(
            stage: async (name, token) =>
            {
                if (!queuedWorker && name == "command-dispatch")
                { entered.TrySetResult(true); await TaskCompatibility.WaitAsync(release.Task, token); }
            });
        if (queuedWorker) fixture.Attachment.BeforeDispatchForTest = async () =>
        { entered.TrySetResult(true); await release.Task; };
        using var caller = new CancellationTokenSource();
        var epoch = fixture.Browser.AttachmentEpoch;
        var pending = fixture.Title(legacy, caller.Token);
        try
        {
            await Bounded(entered.Task);
            if (!timeout) caller.Cancel();
            var error = await Catch(pending);
            if (timeout) AssertTimeout(error, legacy);
            else
            {
                Assert.That(error, Is.InstanceOf<OperationCanceledException>());
                Assert.That(((OperationCanceledException)error).CancellationToken, Is.EqualTo(caller.Token));
                Assert.That(error.Data["CleanupFailures"], Is.Empty);
            }
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Executor.Commands, Is.Zero);
                Assert.That(fixture.Executor.Disposals, Is.Zero);
                Assert.That(fixture.Attachment.Process.Alive, Is.True);
                Assert.That(fixture.Browser.AttachmentEpoch, Is.EqualTo(epoch));
                Assert.That(fixture.Browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
                Assert.That(fixture.Browser.CommandsGateForTest.CurrentCount, Is.EqualTo(1));
            });
        }
        finally { release.TrySetResult(true); }
        if (queuedWorker)
            Assert.ThrowsAsync<OperationCanceledException>(async () => await Bounded(fixture.Attachment.InvocationForTest!));
        fixture.Attachment.BeforeDispatchForTest = null;
        Assert.That(fixture.Executor.Commands, Is.Zero, "A worker released after cancellation must not dispatch late.");
        Assert.That(await Bounded(fixture.Title(legacy)), Is.EqualTo("fixture"));
        Assert.That(fixture.Executor.Commands, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecutorCleanupFailureCanBeRetriedByOneConcurrentOwner()
    {
        await using var fixture = await CommandFixture.StartAsync();
        var original = new InvalidOperationException("executor cleanup failed");
        fixture.Executor.DisposeFailure = original;
        var error = Assert.ThrowsAsync<AggregateException>(async () =>
            await Bounded(fixture.Attachment.DestroyAsync(CancellationToken.None)));
        Assert.That(error!.Flatten().InnerExceptions, Does.Contain(original));
        Assert.That(fixture.Attachment.Process.Alive, Is.False, "Executor failure must not skip process cleanup.");
        Assert.That(fixture.Executor.Disposals, Is.Zero);
        var attempts = fixture.Executor.DisposeCalls;
        fixture.Executor.DisposeFailure = null;
        var entered = Signal(); var release = Signal();
        fixture.Executor.DisposeHook = () => { entered.TrySetResult(true); release.Task.GetAwaiter().GetResult(); };
        var first = fixture.Attachment.DestroyAsync(CancellationToken.None);
        try
        {
            await Bounded(entered.Task);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            var cancellation = Assert.CatchAsync<OperationCanceledException>(async () =>
                await Bounded(fixture.Attachment.DestroyAsync(canceled.Token)));
            Assert.That(cancellation!.CancellationToken, Is.EqualTo(canceled.Token));
            var second = fixture.Attachment.DestroyAsync(CancellationToken.None);
            release.TrySetResult(true);
            await Bounded(Task.WhenAll(first, second));
        }
        finally { release.TrySetResult(true); }
        await Bounded(fixture.Browser.StopAsync().AsTask());
        Assert.That(fixture.Browser.State, Is.EqualTo(BrowserState.Stopped));
        Assert.That(fixture.Executor.Disposals, Is.EqualTo(1));
        Assert.That(fixture.Executor.DisposeCalls, Is.EqualTo(attempts + 1));
    }

    [Test]
    public async Task TerminationFailureRetainsProcessForStopRetryWithoutRepeatingDisposal()
    {
        await using var fixture = await CommandFixture.StartAsync();
        var original = new InvalidOperationException("termination failed");
        fixture.Attachment.TerminateForTest = () => throw original;
        var error = Assert.ThrowsAsync<AggregateException>(async () =>
            await Bounded(fixture.Attachment.DestroyAsync(CancellationToken.None)));
        Assert.That(error!.Flatten().InnerExceptions, Does.Contain(original));
        Assert.That(fixture.Attachment.Process.Alive, Is.True);
        Assert.That(fixture.Executor.Disposals, Is.EqualTo(1), "Other cleanup steps must still run.");
        fixture.Attachment.TerminateForTest = null;
        await Bounded(Task.WhenAll(fixture.Browser.StopAsync().AsTask(), fixture.Browser.DisposeAsync().AsTask()));
        Assert.That(fixture.Browser.State, Is.EqualTo(BrowserState.Stopped));
        Assert.That(fixture.Attachment.Process.Alive, Is.False);
        Assert.That(fixture.Executor.DisposeCalls, Is.EqualTo(1), "Successful cleanup steps must not repeat.");
    }

    [TestCase("terminate"), TestCase("executor")]
    public async Task CanceledCleanupWaitReportsBlockedStageAndRetainsItsOwner(string stage)
    {
        await using var fixture = await CommandFixture.StartAsync();
        var entered = Signal(); var release = Signal();
        void Block() { entered.TrySetResult(true); release.Task.GetAwaiter().GetResult(); }
        if (stage == "terminate") fixture.Attachment.TerminateForTest = () => { Block(); fixture.Attachment.Process.Terminate(); };
        else fixture.Executor.DisposeHook = Block;
        using var caller = new CancellationTokenSource();
        var waiting = fixture.Attachment.DestroyAsync(caller.Token);
        try
        {
            await Bounded(entered.Task);
            caller.Cancel();
            var error = Assert.CatchAsync<OperationCanceledException>(async () => await Bounded(waiting))!;
            Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
            using var trace = JsonDocument.Parse((string)error.Data["Cleanup.AttachmentTrace"]!);
            var last = trace.RootElement.EnumerateArray().Last().GetProperty("event").GetString();
            Assert.That(last, Is.EqualTo(stage == "terminate" ? "process-terminate-start" : "executor-dispose-start"));
            using var snapshot = JsonDocument.Parse((string)error.Data["Cleanup.ProcessSnapshot"]!);
            Assert.That(snapshot.RootElement.GetProperty("pid").GetInt32(), Is.EqualTo(fixture.Attachment.Process.Id));
            Assert.That(snapshot.RootElement.GetProperty("stdout").GetString(), Is.Not.Empty);
            var core = new BrowserDockException(ErrorCategory.CleanupIncomplete, "cleanup");
            fixture.Attachment.RecordCleanup(core);
            Assert.That(Legacy.Api.Translate(core).Data["Cleanup.AttachmentTrace"], Is.EqualTo(core.Data["Cleanup.AttachmentTrace"]));
        }
        finally { release.TrySetResult(true); }
        await Bounded(fixture.Attachment.DestroyAsync(CancellationToken.None));
        Assert.That(fixture.Executor.DisposeCalls, Is.EqualTo(1));
        Assert.That(fixture.Attachment.Process.Alive, Is.False);
    }

    [Test]
    public async Task CleanupDiagnosticsExcludeExceptionMessagesAndProcessPaths()
    {
        await using var fixture = await CommandFixture.StartAsync();
        fixture.Executor.DisposeFailure = new InvalidOperationException("private-script secret-cookie /private/profile");
        var error = Assert.CatchAsync<AggregateException>(async () => await Bounded(fixture.Attachment.DestroyAsync(CancellationToken.None)))!;
        var evidence = string.Join(" ", error.Data.Values.Cast<object>());
        Assert.That(evidence, Does.Not.Contain("private-script").And.Not.Contain("secret-cookie").And.Not.Contain("/private/profile"));
        Assert.That(evidence, Does.Contain("executor-dispose-failed").And.Contain("InvalidOperationException"));
        fixture.Executor.DisposeFailure = null;
        await Bounded(fixture.Attachment.DestroyAsync(CancellationToken.None));
    }

    [Test]
    public async Task RepeatedOwnedAttachmentCleanupCompletesExitAndBothOutputDrains()
    {
        // A portable controlled repetition, also executed by the net481 CI job.
        // A pass does not identify the historical intermittent failure's cause.
        for (var iteration = 0; iteration < 20; iteration++)
        {
            await using var fixture = await CommandFixture.StartAsync();
            await Bounded(fixture.Browser.StopAsync().AsTask());
            using var snapshot = JsonDocument.Parse(fixture.Attachment.Process.WaitSnapshot);
            Assert.That(snapshot.RootElement.GetProperty("stage").GetString(), Is.EqualTo("completed"), "iteration " + iteration);
            Assert.That(snapshot.RootElement.GetProperty("stdout").GetString(), Is.EqualTo("RanToCompletion"));
            Assert.That(snapshot.RootElement.GetProperty("stderr").GetString(), Is.EqualTo("RanToCompletion"));
        }
    }

    [Test]
    public async Task RepeatedBoundCdpCleanupCompletesAllPumps()
    {
        // The reproduced net481 timeout occurred after driver cleanup completed,
        // in fixtures with a live CDP connection. Keep this distinct from the
        // process-only repetition and retain the existing per-operation budget.
        for (var iteration = 0; iteration < 40; iteration++)
        {
            await using var fixture = await ElementWaitTests.Fixture.StartAsync();
            var resources = fixture.Core.Browser.CaptureResourcesForTest();
            await Bounded(fixture.Core.Browser.StopAsync().AsTask());
            Assert.That(fixture.Core.Browser.State, Is.EqualTo(BrowserState.Stopped), "iteration " + iteration);
            Assert.That(resources(), Is.EqualTo((0, 0, 0, false)), "CDP/diagnostic resources at iteration " + iteration);
        }
    }

    [TestCase(32), TestCase(33)]
    public async Task ReadinessSharingViolationRetriesOnlyObservationWithinStartupBudget(int code)
    {
        var ready = Path.GetTempFileName(); var reads = 0; var entered = Signal(); var release = Signal();
        using var deadline = new CancellationTokenSource(Watchdog);
        try
        {
            var pending = CommandFixture.ReadReadyAsync(ready, () => true, deadline.Token, async (_, token) =>
            {
                if (++reads == 1) throw new IOException("controlled sharing violation", unchecked((int)0x80070000) | code);
                entered.TrySetResult(true); await TaskCompatibility.WaitAsync(release.Task, token); return "123";
            });
            await Bounded(entered.Task); Assert.That(pending.IsCompleted, Is.False);
            release.TrySetResult(true); Assert.That(await Bounded(pending), Is.EqualTo("123"));
            Assert.That(reads, Is.EqualTo(2));
        }
        finally { release.TrySetResult(true); File.Delete(ready); }
    }

    [Test]
    public void ReadinessDoesNotRetryUnrelatedIoFailures()
    {
        var ready = Path.GetTempFileName(); var reads = 0;
        var original = new IOException("controlled unrelated I/O", unchecked((int)0x80070005));
        try
        {
            var error = Assert.ThrowsAsync<IOException>(async () => await CommandFixture.ReadReadyAsync(ready, () => true,
                CancellationToken.None, (_, _) => { reads++; return Task.FromException<string>(original); }));
            Assert.That(error, Is.SameAs(original)); Assert.That(reads, Is.EqualTo(1));
        }
        finally { File.Delete(ready); }
    }

    [Test]
    public void ReadinessSharingViolationPreservesCancellationToken()
    {
        var ready = Path.GetTempFileName(); using var caller = new CancellationTokenSource();
        try
        {
            var error = Assert.CatchAsync<OperationCanceledException>(async () => await CommandFixture.ReadReadyAsync(ready,
                () => true, caller.Token, (_, _) => { caller.Cancel(); return Task.FromException<string>(new IOException("controlled", unchecked((int)0x80070020))); }));
            Assert.That(error!.CancellationToken, Is.EqualTo(caller.Token));
        }
        finally { File.Delete(ready); }
    }

    [Test]
    public void ReadinessSharingViolationCannotHideExitedOwner()
    {
        var ready = Path.GetTempFileName(); var alive = true; var reads = 0;
        try
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await CommandFixture.ReadReadyAsync(ready, () => alive,
                CancellationToken.None, (_, _) => { reads++; alive = false; return Task.FromException<string>(new IOException("controlled", unchecked((int)0x80070020))); }));
            Assert.That(reads, Is.EqualTo(1));
        }
        finally { File.Delete(ready); }
    }

    private static async Task<Exception> Catch(Task<string> work)
    {
        try { await Bounded(work); }
        catch (Exception e) when (e is not TimeoutException) { return e; }
        throw new AssertionException("Expected a command failure.");
    }
    private static int Category(Exception error) => error is Legacy.BrowserDockException legacy ? (int)legacy.Category : (int)((BrowserDockException)error).Category;
    private static IReadOnlyList<string> Cleanup(Exception error) => error is Legacy.BrowserDockException legacy ? legacy.CleanupFailures : ((BrowserDockException)error).CleanupFailures;
    private static void AssertTimeout(Exception error, bool legacy)
    {
        Assert.That(Category(error), Is.EqualTo((int)ErrorCategory.OperationTimedOut));
        var core = legacy ? (BrowserDockException)error.InnerException! : (BrowserDockException)error;
        Assert.That(core.OperationId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(core.Diagnostic, Is.Not.Null);
        Assert.That(core.InnerException, Is.InstanceOf<OperationCanceledException>());
        if (legacy)
        {
            Assert.That(((Legacy.BrowserDockException)error).OperationId, Is.EqualTo(core.OperationId));
            Assert.That(((Legacy.BrowserDockException)error).Diagnostic, Is.Not.Null);
        }
    }

    internal sealed class CommandFixture : IAsyncDisposable
    {
        private readonly List<Attachment> owned = new();
        public Browser Browser { get; }
        public WebDriverLease Lease { get; }
        public Attachment Attachment { get; }
        public MemoryExecutor Executor { get; }
        private CommandFixture(Attachment initial, MemoryExecutor executor, TimeSpan? commandBudget, TimeSpan? cleanupBudget,
            Func<string, CancellationToken, ValueTask>? stage, Func<CancellationToken, Task>? probe)
        {
            Attachment = initial; Executor = executor; owned.Add(initial);
            Browser = new(new() { Driver = new() { ExecutablePath = "unused" },
                Timeouts = new() { Command = commandBudget ?? TimeSpan.FromSeconds(5) }, StageHook = stage },
                Attachment, probe ?? (_ => Task.CompletedTask), cleanupBudget ?? TimeSpan.FromSeconds(5));
            Lease = Browser.LeaseForTest();
        }
        public static async Task<CommandFixture> StartAsync(TimeSpan? commandBudget = null, TimeSpan? cleanupBudget = null,
            Func<string, CancellationToken, ValueTask>? stage = null, Func<CancellationToken, Task>? probe = null)
        {
            var (attachment, executor) = await CreateAttachmentAsync();
            return new(attachment, executor, commandBudget, cleanupBudget, stage, probe);
        }
        public Task<string> Title(bool legacy, CancellationToken token = default)
            => legacy ? new Legacy.GuardedCommands(Lease.Commands).GetTitleAsync(token) : Lease.Commands.GetTitleAsync(token).AsTask();
        public async Task<(Attachment Attachment, MemoryExecutor Executor)> AddAttachmentAsync()
        {
            var value = await CreateAttachmentAsync(); owned.Add(value.Attachment); return value;
        }
        private static async Task<(Attachment Attachment, MemoryExecutor Executor)> CreateAttachmentAsync()
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "BrowserDock.slnx"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("Repository fixture host not found.");
            var configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
            var path = Path.Combine(root.FullName, "tests", "BrowserDock.FixtureHost", "bin", configuration, "net10.0",
                Platform.IsWindows ? "BrowserDock.FixtureHost.exe" : "BrowserDock.FixtureHost");
            var ready = Path.Combine(Path.GetTempPath(), "browserdock-command-" + Guid.NewGuid().ToString("N"));
            var process = new OwnedProcess(path, new[] { "--command-ready-file=" + ready });
            var memory = new MemoryExecutor();
            var executor = new DetachAwareCommandExecutor(memory);
            try
            {
                using var startup = new CancellationTokenSource(Watchdog);
                Assert.That(int.Parse(await ReadReadyAsync(ready, () => process.Alive, startup.Token)), Is.EqualTo(process.Id));
                var driver = new RemoteWebDriver(executor, global::BrowserDock.WebDriver.Attachment.CreateOptions(new Uri("ws://127.0.0.1:1/devtools/browser/fixture")).ToCapabilities());
                return (new Attachment(process, 0, executor, driver), memory);
            }
            catch
            {
                process.Terminate();
                using var cleanup = new CancellationTokenSource(Watchdog);
                await process.WaitAsync(cleanup.Token);
                process.Dispose(); executor.Dispose(); throw;
            }
            finally { File.Delete(ready); File.Delete(ready + ".tmp"); }
        }
        internal static async Task<string> ReadReadyAsync(string ready, Func<bool> alive, CancellationToken token,
            Func<string, CancellationToken, Task<string>>? read = null)
        {
            read ??= RuntimeCompatibility.ReadAllTextAsync;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (!alive()) throw new InvalidOperationException("Owned command fixture exited before readiness.");
                try { if (File.Exists(ready)) return await read(ready, token); }
                catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
                {
                    // Publication is atomic, but Windows file scanners can hold
                    // a transient incompatible handle. Retry only this readiness
                    // observation, within the existing startup token/budget.
                }
                await Task.Delay(20, token);
            }
        }
        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            try { await Bounded(Browser.StopAsync().AsTask()); }
            catch (Exception error) { Record(error); }
            foreach (var item in owned)
            {
                try { await Bounded(item.DestroyAsync(CancellationToken.None)); }
                catch (Exception error) { Record(error); }
            }
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("Fixture stop and attachment cleanup failed.", failures);

            void Record(Exception error)
            {
                Browser.RecordCleanup(error);
                foreach (var item in owned) { item.RecordCleanup(error); TestFixtures.FailureEvidence.Write(error); }
                failures.Add(error);
            }
        }
    }

    internal sealed class MemoryExecutor : ICommandExecutor
    {
        private int commands, disposals, disposeCalls;
        public int Commands => Volatile.Read(ref commands);
        public int Disposals => Volatile.Read(ref disposals);
        public int DisposeCalls => Volatile.Read(ref disposeCalls);
        public Exception? DisposeFailure { get; set; }
        public Action? DisposeHook { get; set; }
        public Func<Task<string>> Title { get; set; } = () => Task.FromResult("fixture");
        public Func<Command, Response>? Cdp { get; set; }
        public Func<Command, Response>? Command { get; set; }
        public int CdpCommandsAdded { get; private set; }
        public bool TryAddCommand(string name, CommandInfo? info)
        {
            if (name != "browserDockCdp") return false;
            Assert.That(info, Is.InstanceOf<HttpCommandInfo>());
            CdpCommandsAdded++; return true;
        }
        public Response Execute(Command command)
        {
            if (command.Name == DriverCommand.NewSession)
                return new Response("fixture-session", new Dictionary<string, object> { ["browserName"] = "chrome", ["browserVersion"] = "150.0.1.0", ["platformName"] = "fixture" }, WebDriverResult.Success);
            Interlocked.Increment(ref commands);
            if (command.Name == "browserDockCdp") return Cdp!(command);
            if (Command is not null) return Command(command);
            return new Response(command.SessionId?.ToString(), Title().GetAwaiter().GetResult(), WebDriverResult.Success);
        }
        public Task<Response> ExecuteAsync(Command command) => Task.FromResult(Execute(command));
        public void Dispose()
        {
            Interlocked.Increment(ref disposeCalls);
            DisposeHook?.Invoke();
            if (DisposeFailure is { } failure) throw failure;
            Interlocked.Increment(ref disposals);
        }
    }
}
