using System.Collections.Concurrent;
using BrowserDock.Cdp;
using BrowserDock.FrameworkTests;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using Legacy = BrowserDock.Legacy;
using CommandFixture = BrowserDock.Tests.CommandDeadlineTests.CommandFixture;

namespace BrowserDock.Tests;

// Real Browser admission, command deadlines and Selenium serialization; a
// controlled W3C executor supplies DOM changes without a desktop/browser.
[TestFixture]
public sealed class ElementWaitTests
{
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(15);
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Bounded(Task task) => TaskCompatibility.WaitAsync(task, Watchdog);
    private static Task<T> Bounded<T>(Task<T> task) => TaskCompatibility.WaitAsync(task, Watchdog);
    private static Response Reply(object? value) => new("fixture-session", value!, WebDriverResult.Success);
    private static Response Element(string id = "element") => Reply(new Dictionary<string, object> { ["element-6066-11e4-a52e-4f735466cecf"] = id });
    private static readonly ElementWaitOptions WaitOptions = new() { Timeout = TimeSpan.FromSeconds(5), PollInterval = TimeSpan.FromMilliseconds(1) };

    [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
    public async Task ExistsWaitsForInsertionAndReturnsUsableReference(bool legacy, bool lease)
    {
        await using var fixture = await Fixture.StartAsync();
        var finds = 0;
        fixture.OnCommand = command => command.Name switch
        {
            var name when name == DriverCommand.FindElement => ++finds < 3 ? throw new NoSuchElementException() : Element(),
            var name when name == DriverCommand.GetElementText => Reply("inserted"),
            _ => Reply(null)
        };
        Assert.That(await Bounded(fixture.Wait("exists", legacy, lease)), Is.EqualTo("inserted"));
        Assert.That(finds, Is.EqualTo(3));
        Assert.That(fixture.Commands.All(x => x.Name != DriverCommand.ClickElement && x.Name != DriverCommand.SetTimeouts), Is.True);
        Assert.That(fixture.Core.Browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
    }

    [TestCase(false), TestCase(true)]
    public async Task VisibleReacquiresAfterRerenderAndWaitsForDisplay(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        var finds = 0; var displays = 0;
        fixture.OnCommand = command => command.Name switch
        {
            var name when name == DriverCommand.FindElement => Element("element-" + ++finds),
            var name when name == DriverCommand.ExecuteScript => ++displays == 1 ? throw new StaleElementReferenceException() : Reply(displays >= 3),
            var name when name == DriverCommand.GetElementText => Reply(command.Parameters!["id"]),
            _ => Reply(null)
        };
        Assert.That(await Bounded(fixture.Wait("visible", legacy)), Is.EqualTo("element-3"));
        Assert.That(finds, Is.EqualTo(3));
        Assert.That(fixture.Core.Executor.Disposals, Is.Zero);
    }

    [TestCase(false), TestCase(true)]
    public async Task AbsentRequiresNoMatchRatherThanHiddenOrStale(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        var finds = 0;
        fixture.OnCommand = command => command.Name switch
        {
            var name when name == DriverCommand.FindElement => ++finds switch
            {
                1 => Element(), 2 => throw new StaleElementReferenceException(), _ => throw new NoSuchElementException()
            },
            var name when name == DriverCommand.ExecuteScript => throw new AssertionException("Absence must not be inferred from visibility."),
            _ => Reply(null)
        };
        await Bounded(fixture.Wait("absent", legacy));
        Assert.That(finds, Is.EqualTo(3));
    }

    [TestCase("missing"), TestCase("stale"), TestCase("not-frame")]
    public async Task MissingAncestorIsNotAbsenceAndNestedFramesAreRestoredOnEveryPoll(string failure)
    {
        await using var fixture = await Fixture.StartAsync();
        var frames = new List<Locator> { Locator.Id("outer"), Locator.Id("inner") };
        var options = WaitOptions with { FramePath = frames };
        var outer = 0; var entered = new List<string>(); var leaf = 0;
        fixture.OnCommand = command =>
        {
            if (command.Name == DriverCommand.FindElement)
            {
                var value = (string)command.Parameters!["value"]!;
                if (value.Contains("outer"))
                {
                    if (++outer == 1)
                    {
                        frames.Clear();
                        throw failure switch { "missing" => new NoSuchElementException(), "stale" => new StaleElementReferenceException(), _ => new NoSuchFrameException() };
                    }
                    return Element("outer");
                }
                if (value.Contains("inner")) return Element("inner");
                leaf++; throw new NoSuchElementException();
            }
            if (command.Name == DriverCommand.SwitchToFrame && command.Parameters!["id"] is Dictionary<string, object> id)
                entered.Add((string)id["element-6066-11e4-a52e-4f735466cecf"]);
            return Reply(null);
        };
        await Bounded(fixture.Core.Browser.WaitForAbsentAsync(Locator.Id("subject"), options).AsTask());
        Assert.That(outer, Is.EqualTo(2));
        Assert.That(entered, Is.EqualTo(new[] { "outer", "inner" }));
        Assert.That(leaf, Is.EqualTo(1));
        Assert.That(fixture.Commands.Count(x => x.Name == DriverCommand.SwitchToWindow), Is.EqualTo(2));
    }

    [TestCase(false), TestCase(true)]
    public async Task InvalidSelectorFailsWithoutRetryAndWindowClosureIsNotAbsence(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        var finds = 0;
        fixture.OnCommand = command => command.Name == DriverCommand.FindElement
            ? throw (++finds == 1 ? new InvalidSelectorException() : new NoSuchWindowException()) : Reply(null);
        Assert.That(Category(await Catch(fixture.Wait("exists", legacy))), Is.EqualTo(ErrorCategory.ConfigurationError));
        Assert.That(Category(await Catch(fixture.Wait("absent", legacy))), Is.EqualTo(ErrorCategory.TargetClosed));
        Assert.That(finds, Is.EqualTo(2));
        Assert.That(fixture.Core.Executor.Disposals, Is.Zero);
    }

    [TestCase(false), TestCase(true)]
    public async Task QueuedTimeoutUsesWaitBudgetAndDoesNotInvalidateAttachment(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        await fixture.Core.Browser.CommandsGateForTest.WaitAsync();
        try
        {
            var error = await Catch(fixture.Wait("exists", legacy, options: WaitOptions with { Timeout = TimeSpan.FromMilliseconds(200) }));
            Assert.That(Category(error), Is.EqualTo(ErrorCategory.OperationTimedOut));
            Assert.That(CoreError(error).OperationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(CoreError(error).Diagnostic, Is.Not.Null);
            Assert.That(fixture.Core.Executor.Commands, Is.Zero);
            Assert.That(fixture.Core.Executor.Disposals, Is.Zero);
            Assert.That(fixture.Core.Browser.CommandsGateForTest.CurrentCount, Is.Zero);
        }
        finally { fixture.Core.Browser.CommandsGateForTest.Release(); }
        Assert.That(await fixture.Core.Lease.Commands.GetTitleAsync(), Is.EqualTo("fixture"));
    }

    [TestCase(false), TestCase(true)]
    public async Task QueuedCancellationPreservesCallerToken(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        using var caller = new CancellationTokenSource();
        await fixture.Core.Browser.CommandsGateForTest.WaitAsync();
        try
        {
            var pending = fixture.Wait("exists", legacy, token: caller.Token); caller.Cancel();
            var error = (OperationCanceledException)await Catch(pending);
            Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
            Assert.That(error.Data["OperationId"], Is.Not.EqualTo(Guid.Empty));
            Assert.That(fixture.Core.Executor.Commands, Is.Zero);
            Assert.That(fixture.Core.Executor.Disposals, Is.Zero);
        }
        finally { fixture.Core.Browser.CommandsGateForTest.Release(); }
    }

    [TestCase(false), TestCase(true)]
    public async Task PollDelayReleasesGateAndRetainsOneDeadline(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        var observed = Signal();
        fixture.OnCommand = command =>
        {
            if (command.Name == DriverCommand.FindElement) { observed.TrySetResult(true); throw new NoSuchElementException(); }
            return Reply("fixture");
        };
        var pending = fixture.Wait("exists", legacy, options: WaitOptions with { Timeout = TimeSpan.FromSeconds(1), PollInterval = TimeSpan.FromSeconds(10) });
        await Bounded(observed.Task);
        Assert.That(await Bounded(fixture.Core.Lease.Commands.GetTitleAsync().AsTask()), Is.EqualTo("fixture"));
        Assert.That(Category(await Catch(pending)), Is.EqualTo(ErrorCategory.OperationTimedOut));
        Assert.That(fixture.Commands.Count(x => x.Name == DriverCommand.FindElement), Is.EqualTo(1));
        Assert.That(fixture.Core.Executor.Disposals, Is.Zero);
    }

    [TestCase(false), TestCase(true)]
    public async Task InFlightCancellationRetainsCallerTokenAndCommandRecovery(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        var entered = Signal(); var release = Signal();
        fixture.OnCommand = command =>
        {
            if (command.Name == DriverCommand.FindElement) { entered.TrySetResult(true); release.Task.GetAwaiter().GetResult(); return Element(); }
            return Reply(null);
        };
        using var caller = new CancellationTokenSource();
        try
        {
            var pending = fixture.Wait("exists", legacy, token: caller.Token);
            await Bounded(entered.Task); caller.Cancel();
            var error = (OperationCanceledException)await Catch(pending);
            Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
            Assert.That(error.Data["Diagnostic"], Is.InstanceOf<BrowserHealthSnapshot>());
            Assert.That(fixture.Core.Browser.State, Is.EqualTo(BrowserState.CdpOnly));
            Assert.That(fixture.Core.Executor.Disposals, Is.EqualTo(1));
        }
        finally { release.TrySetResult(true); }
    }

    [TestCase(false), TestCase(true)]
    public async Task DisposedLeaseIsNotRetriedAgainstAnotherLease(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        var finds = 0;
        fixture.OnCommand = command =>
        {
            if (command.Name == DriverCommand.FindElement)
            { finds++; fixture.Core.Lease.DisposeAsync().GetAwaiter().GetResult(); throw new NoSuchElementException(); }
            return Reply(null);
        };
        Assert.That(Category(await Catch(fixture.Wait("exists", legacy, true))), Is.EqualTo(ErrorCategory.StaleAttachment));
        Assert.That(finds, Is.EqualTo(1));
        Assert.That(fixture.Core.Executor.Disposals, Is.Zero);
    }

    [TestCase(false), TestCase(true)]
    public async Task ValueIsLivePropertyWhileAttributeAndAliasRemainContentAttributes(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        fixture.OnCommand = command => command.Name switch
        {
            var name when name == DriverCommand.FindElement => Element(),
            var name when name == DriverCommand.GetElementAttribute => Reply((string)command.Parameters!["name"]! == "value" ? "original" : null),
            var name when name == DriverCommand.GetElementProperty => Reply((string)command.Parameters!["name"]! == "value" ? "edited" : null),
            _ => Reply(null)
        };
        if (legacy)
        {
            var element = await new Legacy.Browser(fixture.Core.Browser).WaitForExistsAsync(Legacy.Locator.Id("subject"));
            Assert.That(await element.GetAttributeAsync("value"), Is.EqualTo("original"));
            Assert.That(await element.GetDomAttributeAsync("value"), Is.EqualTo("original"));
            Assert.That(await element.GetDomPropertyAsync("value"), Is.EqualTo("edited"));
            Assert.That(await element.GetValueAsync(), Is.EqualTo("edited"));
            Assert.That(await element.GetDomAttributeAsync("missing"), Is.Null);
            Assert.That(await element.GetDomPropertyAsync("missing"), Is.Null);
        }
        else
        {
            var element = await fixture.Core.Browser.WaitForExistsAsync(Locator.Id("subject"));
            Assert.That(await element.GetAttributeAsync("value"), Is.EqualTo("original"));
            Assert.That(await element.GetDomAttributeAsync("value"), Is.EqualTo("original"));
            Assert.That(await element.GetDomPropertyAsync("value"), Is.EqualTo("edited"));
            Assert.That(await element.GetValueAsync(), Is.EqualTo("edited"));
            Assert.That(await element.GetDomAttributeAsync("missing"), Is.Null);
            Assert.That(await element.GetDomPropertyAsync("missing"), Is.Null);
        }
        Assert.That(fixture.Commands.Count(x => x.Name == DriverCommand.GetElementProperty), Is.EqualTo(3));
    }

    [TestCase(false), TestCase(true)]
    public async Task LeaseDisposedDuringSuccessfulObservationCannotReturnSuccess(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        fixture.OnCommand = command =>
        {
            if (command.Name == DriverCommand.FindElement)
            { fixture.Core.Lease.DisposeAsync().GetAwaiter().GetResult(); return Element(); }
            return Reply(null);
        };
        var error = await Catch(fixture.Wait("exists", legacy, true));
        Assert.That(Category(error), Is.EqualTo(ErrorCategory.StaleAttachment));
        Assert.That(CoreError(error).OperationId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(fixture.Core.Executor.Disposals, Is.Zero);
    }

    [TestCase(false), TestCase(true)]
    public async Task InvalidWaitOptionsFailBeforeDispatch(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        foreach (var bad in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(-1), TimeSpan.FromTicks(1), TimeSpan.FromDays(30) })
        {
            Assert.That(Category(await Catch(fixture.Wait("exists", legacy, options: WaitOptions with { Timeout = bad }))), Is.EqualTo(ErrorCategory.ConfigurationError));
            Assert.That(Category(await Catch(fixture.Wait("absent", legacy, options: WaitOptions with { PollInterval = bad }))), Is.EqualTo(ErrorCategory.ConfigurationError));
        }
        Assert.That(fixture.Core.Executor.Commands, Is.Zero);
    }

    [TestCase(false), TestCase(true)]
    public async Task ValueReadDoesNotSilentlyReacquireStaleDomOrAttachment(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        var stale = false;
        fixture.OnCommand = command => command.Name == DriverCommand.FindElement ? Element()
            : command.Name == DriverCommand.GetElementProperty && stale ? throw new StaleElementReferenceException() : Reply(null);
        var core = await fixture.Core.Browser.WaitForExistsAsync(Locator.Id("subject"));
        var facade = new Legacy.ElementRef(core);
        Task<string?> Read() => legacy ? facade.GetValueAsync() : core.GetValueAsync().AsTask();
        stale = true;
        Assert.That(Category(await Catch(Read())), Is.EqualTo(ErrorCategory.StaleDomElement));
        var replacement = await fixture.Core.AddAttachmentAsync();
        fixture.Core.Browser.ReplaceAttachmentForTest(replacement.Attachment);
        var sent = fixture.Commands.Count;
        Assert.That(Category(await Catch(Read())), Is.EqualTo(ErrorCategory.StaleAttachment));
        Assert.That(fixture.Commands.Count, Is.EqualTo(sent));
        Assert.That(replacement.Executor.Commands, Is.Zero);
    }

    [TestCase(false), TestCase(true)]
    public async Task ReplacementBetweenPollsCannotCompleteTheOriginalWait(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        var replacement = await fixture.Core.AddAttachmentAsync();
        fixture.OnCommand = command =>
        {
            if (command.Name == DriverCommand.FindElement)
            { fixture.Core.Browser.ReplaceAttachmentForTest(replacement.Attachment); throw new NoSuchElementException(); }
            return Reply(null);
        };
        Assert.That(Category(await Catch(fixture.Wait("exists", legacy))), Is.EqualTo(ErrorCategory.StaleAttachment));
        Assert.That(replacement.Executor.Commands, Is.Zero);
        Assert.That(replacement.Executor.Disposals, Is.Zero);
    }

    [TestCase(false), TestCase(true)]
    public async Task PollingCancellationPreservesTokenWithoutInvalidatingAttachment(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        var observed = Signal();
        fixture.OnCommand = command =>
        {
            if (command.Name == DriverCommand.FindElement) { observed.TrySetResult(true); throw new NoSuchElementException(); }
            return Reply("fixture");
        };
        using var caller = new CancellationTokenSource();
        var pending = fixture.Wait("exists", legacy, options: WaitOptions with { PollInterval = TimeSpan.FromSeconds(10) }, token: caller.Token);
        await Bounded(observed.Task);
        // The follow-up command acquires the gate after the completed observation.
        await Bounded(fixture.Core.Lease.Commands.GetTitleAsync().AsTask());
        caller.Cancel();
        var error = (OperationCanceledException)await Catch(pending);
        Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
        Assert.That(fixture.Core.Executor.Disposals, Is.Zero);
    }

    [TestCase(false, "dispose"), TestCase(true, "dispose")]
    [TestCase(false, "disconnect"), TestCase(true, "disconnect")]
    [TestCase(false, "replace"), TestCase(true, "replace")]
    public async Task PollingDeadlineReportsInvalidatedAttachment(bool legacy, string change)
    {
        var polling = Signal(); var resume = Signal();
        await using var fixture = await Fixture.StartAsync(async (stage, token) =>
        {
            if (stage == "element-wait-poll") { polling.TrySetResult(true); await TaskCompatibility.WaitAsync(resume.Task, token); }
        });
        var replacement = change == "replace" ? await fixture.Core.AddAttachmentAsync() : default;
        fixture.OnCommand = command => command.Name == DriverCommand.FindElement ? throw new NoSuchElementException() : Reply(null);
        var pending = fixture.Wait("exists", legacy, true, WaitOptions with
        { Timeout = TimeSpan.FromSeconds(2), PollInterval = TimeSpan.FromSeconds(10) });
        try
        {
            await Bounded(polling.Task);
            if (change == "dispose") await fixture.Core.Lease.DisposeAsync();
            else if (change == "disconnect") await Bounded(fixture.Core.Browser.DisconnectWebDriverAsync().AsTask());
            else fixture.Core.Browser.ReplaceAttachmentForTest(replacement.Attachment!);
            var sent = fixture.Commands.Count;
            resume.TrySetResult(true);
            var error = await Catch(pending);
            Assert.That(Category(error), Is.EqualTo(ErrorCategory.StaleAttachment));
            Assert.That(CoreError(error).OperationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(CoreError(error).Diagnostic, Is.Not.Null);
            Assert.That(fixture.Commands.Count, Is.EqualTo(sent), "Invalidation must not trigger another remote observation.");
            Assert.That(fixture.Core.Executor.Disposals, Is.EqualTo(change == "disconnect" ? 1 : 0));
            if (change == "replace") Assert.That(replacement.Executor!.Commands, Is.Zero);
        }
        finally { resume.TrySetResult(true); }
    }

    [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
    public async Task PollingCancellationTakesPrecedenceOverDisposedLease(bool legacy, bool stop)
    {
        var polling = Signal(); var resume = Signal();
        await using var fixture = await Fixture.StartAsync(async (stage, token) =>
        {
            if (stage == "element-wait-poll") { polling.TrySetResult(true); await TaskCompatibility.WaitAsync(resume.Task, token); }
        });
        fixture.OnCommand = command => command.Name == DriverCommand.FindElement ? throw new NoSuchElementException() : Reply(null);
        using var caller = new CancellationTokenSource();
        var pending = fixture.Wait("exists", legacy, true, token: caller.Token);
        try
        {
            await Bounded(polling.Task);
            await fixture.Core.Lease.DisposeAsync();
            if (stop) await Bounded(fixture.Core.Browser.StopAsync().AsTask()); else caller.Cancel();
            var error = await Catch(pending);
            Assert.That(error, Is.InstanceOf<OperationCanceledException>());
            if (!stop) Assert.That(((OperationCanceledException)error).CancellationToken, Is.EqualTo(caller.Token));
        }
        finally { resume.TrySetResult(true); }
    }

    [TestCase(false), TestCase(true)]
    public async Task InFlightWaitTimeoutRetainsDeadlineReasonDuringCleanup(bool legacy)
    {
        var recovering = Signal(); var releaseRecovery = Signal(); var entered = Signal(); var release = Signal();
        await using var fixture = await Fixture.StartAsync(async (stage, token) =>
        {
            if (stage == "command-recovery") { recovering.TrySetResult(true); await TaskCompatibility.WaitAsync(releaseRecovery.Task, token); }
        });
        fixture.OnCommand = command =>
        {
            if (command.Name == DriverCommand.FindElement) { entered.TrySetResult(true); release.Task.GetAwaiter().GetResult(); return Element(); }
            return Reply(null);
        };
        using var caller = new CancellationTokenSource();
        try
        {
            var pending = fixture.Wait("exists", legacy, options: WaitOptions with { Timeout = TimeSpan.FromSeconds(1) }, token: caller.Token);
            await Bounded(entered.Task); await Bounded(recovering.Task);
            caller.Cancel(); releaseRecovery.TrySetResult(true);
            Assert.That(Category(await Catch(pending)), Is.EqualTo(ErrorCategory.OperationTimedOut));
            Assert.That(fixture.Core.Executor.Disposals, Is.EqualTo(1));
            Assert.That(fixture.Core.Browser.State, Is.EqualTo(BrowserState.CdpOnly));
        }
        finally { release.TrySetResult(true); releaseRecovery.TrySetResult(true); }
    }

    [Test]
    public async Task LegacyOptionsAreCapturedBeforeQueueing()
    {
        await using var fixture = await Fixture.StartAsync();
        var frames = new List<Legacy.Locator> { Legacy.Locator.Id("original-frame") };
        var options = new Legacy.ElementWaitOptions { Timeout = TimeSpan.FromSeconds(5), FramePath = frames };
        fixture.OnCommand = command => command.Name == DriverCommand.FindElement ? Element() : Reply(null);
        await fixture.Core.Browser.CommandsGateForTest.WaitAsync();
        Task<Legacy.ElementRef> pending;
        try
        {
            pending = new Legacy.Browser(fixture.Core.Browser).WaitForExistsAsync(Legacy.Locator.Id("subject"), options);
            frames.Clear(); options.Timeout = TimeSpan.Zero; options.Target = new Legacy.TargetKey("unknown");
        }
        finally { fixture.Core.Browser.CommandsGateForTest.Release(); }
        var element = await Bounded(pending);
        Assert.That(element.FramePath.Single().Value, Is.EqualTo("original-frame"));
    }

    [Test]
    public async Task ExplicitClosedTargetDoesNotSatisfyAbsence()
    {
        await using var fixture = await Fixture.StartAsync();
        var error = await Catch(fixture.Core.Browser.WaitForAbsentAsync(Locator.Id("subject"), WaitOptions with { Target = new TargetKey("unknown") }).AsTask());
        Assert.That(Category(error), Is.EqualTo(ErrorCategory.TargetClosed));
        Assert.That(fixture.Core.Executor.Commands, Is.Zero);
    }

    private static async Task<Exception> Catch(Task task)
    {
        try { await Bounded(task); }
        catch (Exception error) when (error is not TimeoutException) { return error; }
        throw new AssertionException("Expected failure.");
    }
    private static BrowserDockException CoreError(Exception error) => error is Legacy.BrowserDockException ? (BrowserDockException)error.InnerException! : (BrowserDockException)error;
    private static ErrorCategory Category(Exception error) => CoreError(error).Category;

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly FixtureServer server;
        public CommandFixture Core { get; }
        public ConcurrentQueue<Command> Commands { get; } = new();
        public Func<Command, Response> OnCommand { get; set; } = command => Reply(command.Name == DriverCommand.GetTitle ? "fixture" : null);
        private Fixture(FixtureServer server, CommandFixture core)
        {
            this.server = server; Core = core;
            core.Executor.Command = command => { Commands.Enqueue(command); return OnCommand(command); };
        }
        public static async Task<Fixture> StartAsync(Func<string, CancellationToken, ValueTask>? stage = null)
        {
            var server = await FixtureServer.StartAsync();
            CommandFixture? core = null;
            var controller = new CdpController(new UriBuilder(server.Url) { Scheme = "ws", Path = "/cdp" }.Uri, []);
            try
            {
                core = await CommandFixture.StartAsync(stage: stage);
                using var deadline = new CancellationTokenSource(Watchdog);
                await controller.InitializeAsync(deadline.Token);
                core.Browser.BindElementTargetForTest(controller, controller.Controlled!.Value, "window");
                return new(server, core);
            }
            catch { if (core is not null) await core.DisposeAsync(); await controller.DisposeAsync(); server.Dispose(); throw; }
        }
        public async Task<string> Wait(string kind, bool legacy, bool lease = false, ElementWaitOptions? options = null, CancellationToken token = default)
        {
            options ??= WaitOptions;
            if (legacy)
            {
                var browser = new Legacy.Browser(Core.Browser);
                var commands = new Legacy.GuardedCommands(Core.Lease.Commands);
                var snapshot = new Legacy.ElementWaitOptions { Timeout = options.Timeout, PollInterval = options.PollInterval,
                    Target = options.Target is { } target ? new Legacy.TargetKey(target.Value) : null,
                    FramePath = options.FramePath.Select(Legacy.Locator.FromCore).ToArray() };
                var locator = Legacy.Locator.Id("subject");
                if (kind == "absent")
                { await (lease ? commands.WaitForAbsentAsync(locator, snapshot, token) : browser.WaitForAbsentAsync(locator, snapshot, token)); return ""; }
                var element = await (kind == "visible"
                    ? lease ? commands.WaitForVisibleAsync(locator, snapshot, token) : browser.WaitForVisibleAsync(locator, snapshot, token)
                    : lease ? commands.WaitForExistsAsync(locator, snapshot, token) : browser.WaitForExistsAsync(locator, snapshot, token));
                return await element.GetTextAsync();
            }
            else
            {
                var browser = Core.Browser; var commands = Core.Lease.Commands; var locator = Locator.Id("subject");
                if (kind == "absent")
                { await (lease ? commands.WaitForAbsentAsync(locator, options, token) : browser.WaitForAbsentAsync(locator, options, token)); return ""; }
                var element = await (kind == "visible"
                    ? lease ? commands.WaitForVisibleAsync(locator, options, token) : browser.WaitForVisibleAsync(locator, options, token)
                    : lease ? commands.WaitForExistsAsync(locator, options, token) : browser.WaitForExistsAsync(locator, options, token));
                return await element.GetTextAsync();
            }
        }
        public async ValueTask DisposeAsync() { try { await Core.DisposeAsync(); } finally { server.Dispose(); } }
    }
}
