using System.Diagnostics;
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

    [TestCase(false), TestCase(true)]
    public async Task QueuedDeadlinePreservesAttachmentAndGateOwnership(bool legacy)
    {
        await using var fixture = new CommandFixture(TimeSpan.FromMilliseconds(150));
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
        await using var fixture = new CommandFixture();
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
        await using var fixture = new CommandFixture();
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
        await using var fixture = new CommandFixture(TimeSpan.FromSeconds(1));
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
        await using var fixture = new CommandFixture(cleanupBudget: TimeSpan.FromMilliseconds(200),
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
        await using var fixture = new CommandFixture(cleanupBudget: TimeSpan.FromMilliseconds(200),
            probe: async token => { probing.TrySetResult(true); await Task.Delay(Timeout.Infinite, token); });
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
        await using var fixture = new CommandFixture();
        fixture.Executor.Title = async () => { entered.TrySetResult(true); await release.Task; throw new WebDriverException("old failure"); };
        var pending = fixture.Title(false);
        await Bounded(entered.Task);
        var replacement = fixture.AddAttachment();
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
        await using var fixture = new CommandFixture(stage: async (name, token) =>
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
        await using var fixture = new CommandFixture();
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

    [Test]
    public async Task BothCancellationSourcesPreferCallerAndDoNotTurnIntoTimeout()
    {
        await using var fixture = new CommandFixture();
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
        await using var fixture = new CommandFixture();
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

    private sealed class CommandFixture : IAsyncDisposable
    {
        private readonly List<Attachment> owned = new();
        public Browser Browser { get; }
        public WebDriverLease Lease { get; }
        public Attachment Attachment { get; }
        public MemoryExecutor Executor { get; }
        public CommandFixture(TimeSpan? commandBudget = null, TimeSpan? cleanupBudget = null,
            Func<string, CancellationToken, ValueTask>? stage = null, Func<CancellationToken, Task>? probe = null)
        {
            (Attachment, Executor) = AddAttachment();
            Browser = new(new() { Driver = new() { ExecutablePath = "unused" },
                Timeouts = new() { Command = commandBudget ?? TimeSpan.FromSeconds(5) }, StageHook = stage },
                Attachment, probe ?? (_ => Task.CompletedTask), cleanupBudget ?? TimeSpan.FromSeconds(5));
            Lease = Browser.LeaseForTest();
        }
        public Task<string> Title(bool legacy, CancellationToken token = default)
            => legacy ? new Legacy.GuardedCommands(Lease.Commands).GetTitleAsync(token) : Lease.Commands.GetTitleAsync(token).AsTask();
        public (Attachment Attachment, MemoryExecutor Executor) AddAttachment()
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "BrowserDock.slnx"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("Repository fixture host not found.");
            var configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
            var path = Path.Combine(root.FullName, "tests", "BrowserDock.FixtureHost", "bin", configuration, "net10.0",
                Platform.IsWindows ? "BrowserDock.FixtureHost.exe" : "BrowserDock.FixtureHost");
            var process = new OwnedProcess(path, Array.Empty<string>());
            var memory = new MemoryExecutor();
            var executor = new DetachAwareCommandExecutor(memory);
            try
            {
                var driver = new RemoteWebDriver(executor, global::BrowserDock.WebDriver.Attachment.CreateOptions(new Uri("ws://127.0.0.1:1/devtools/browser/fixture")).ToCapabilities());
                var attachment = new Attachment(process, 0, executor, driver);
                owned.Add(attachment); return (attachment, memory);
            }
            catch { process.Terminate(); process.Dispose(); executor.Dispose(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            try { await Bounded(Browser.StopAsync().AsTask()); }
            finally
            {
                foreach (var item in owned) await Bounded(item.DestroyAsync(CancellationToken.None));
            }
        }
    }

    private sealed class MemoryExecutor : ICommandExecutor
    {
        private int commands, disposals;
        public int Commands => Volatile.Read(ref commands);
        public int Disposals => Volatile.Read(ref disposals);
        public Func<Task<string>> Title { get; set; } = () => Task.FromResult("fixture");
        public Action? OnDispose { get; set; }
        public bool TryAddCommand(string name, CommandInfo? info) => false;
        public Response Execute(Command command)
        {
            if (command.Name == DriverCommand.NewSession)
                return new Response("fixture-session", new Dictionary<string, object> { ["browserName"] = "chrome", ["browserVersion"] = "150.0.1.0", ["platformName"] = "fixture" }, WebDriverResult.Success);
            Interlocked.Increment(ref commands);
            return new Response(command.SessionId?.ToString(), Title().GetAwaiter().GetResult(), WebDriverResult.Success);
        }
        public Task<Response> ExecuteAsync(Command command) => Task.FromResult(Execute(command));
        public void Dispose() { Interlocked.Increment(ref disposals); OnDispose?.Invoke(); }
    }
}
