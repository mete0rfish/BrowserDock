using BrowserDock.FrameworkTests;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using Legacy = BrowserDock.Legacy;
using Fixture = BrowserDock.Tests.ElementWaitTests.Fixture;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class ElementClickTests
{
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(15);
    private static readonly ElementWaitOptions Options = new() { Timeout = TimeSpan.FromSeconds(5), PollInterval = TimeSpan.FromMilliseconds(1) };
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Bounded(Task task) => TaskCompatibility.WaitAsync(task, Watchdog);
    private static Response Reply(object? value) => new("fixture-session", value!, WebDriverResult.Success);
    private static Response Element(string id = "subject") => Reply(new Dictionary<string, object> { ["element-6066-11e4-a52e-4f735466cecf"] = id });
    private static Response Ready(Command command) => command.Name == DriverCommand.FindElement ? Element()
        : Reply(command.Name == DriverCommand.ExecuteScript || command.Name == DriverCommand.IsElementEnabled ? true : null);
    private static async Task<Exception> Catch(Task task)
    {
        try { await Bounded(task); }
        catch (Exception error) when (error is not TimeoutException) { return error; }
        throw new AssertionException("Expected failure.");
    }
    private static ErrorCategory Category(Exception error) =>
        (error is Legacy.BrowserDockException ? (BrowserDockException)error.InnerException! : (BrowserDockException)error).Category;

    [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
    public async Task WaitsThroughMissingHiddenDisabledAndReplacedElementsThenClicksOnce(bool legacy, bool lease)
    {
        await using var fixture = await Fixture.StartAsync();
        var finds = 0; var clicks = 0;
        fixture.OnCommand = command =>
        {
            if (command.Name == DriverCommand.FindElement)
                return ++finds == 1 ? throw new NoSuchElementException() : Element("subject-" + finds);
            if (command.Name == DriverCommand.ExecuteScript) return Reply(finds != 2);
            if (command.Name == DriverCommand.IsElementEnabled)
                return finds == 4 ? throw new StaleElementReferenceException() : Reply(finds != 3);
            if (command.Name == DriverCommand.ClickElement)
            { clicks++; Assert.That(command.Parameters!["id"], Is.EqualTo("subject-5")); }
            return Reply(null);
        };
        await Bounded(fixture.Wait("click", legacy, lease));
        Assert.That(finds, Is.EqualTo(5));
        Assert.That(clicks, Is.EqualTo(1));
        Assert.That(fixture.Commands.Any(x => x.Name == DriverCommand.SetTimeouts || x.Name == DriverCommand.GetElementText), Is.False);
        Assert.That(fixture.Core.Browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
    }

    [TestCase(false, "stale", ErrorCategory.StaleDomElement), TestCase(true, "stale", ErrorCategory.StaleDomElement)]
    [TestCase(false, "missing", ErrorCategory.ElementNotFound), TestCase(true, "missing", ErrorCategory.ElementNotFound)]
    [TestCase(false, "intercepted", ErrorCategory.ElementInteractionFailure), TestCase(true, "intercepted", ErrorCategory.ElementInteractionFailure)]
    [TestCase(false, "not-interactable", ErrorCategory.ElementInteractionFailure), TestCase(true, "not-interactable", ErrorCategory.ElementInteractionFailure)]
    [TestCase(false, "transport", ErrorCategory.AttachmentLost), TestCase(true, "transport", ErrorCategory.AttachmentLost)]
    [TestCase(false, "timeout", ErrorCategory.OperationTimedOut), TestCase(true, "timeout", ErrorCategory.OperationTimedOut)]
    public async Task DispatchedClickIsNeverRetriedOrReplacedByFallback(bool legacy, string failure, ErrorCategory expected)
    {
        await using var fixture = await Fixture.StartAsync();
        var clicks = 0;
        fixture.OnCommand = command =>
        {
            if (command.Name != DriverCommand.ClickElement) return Ready(command);
            clicks++; // A response can be lost after the browser already applied the side effect.
            throw failure switch
            {
                "stale" => new StaleElementReferenceException(),
                "missing" => new NoSuchElementException(),
                "intercepted" => new ElementClickInterceptedException(),
                "not-interactable" => new ElementNotInteractableException(),
                "timeout" => new WebDriverTimeoutException(),
                _ => new WebDriverException("response lost after click")
            };
        };
        Assert.That(Category(await Catch(fixture.Wait("click", legacy))), Is.EqualTo(expected));
        Assert.That(clicks, Is.EqualTo(1));
        Assert.That(fixture.Commands.Count(x => x.Name == DriverCommand.FindElement), Is.EqualTo(1));
        Assert.That(fixture.Commands.Count(x => x.Name == DriverCommand.ExecuteScript), Is.EqualTo(1), "Only the visibility atom is allowed; no JavaScript click fallback.");
        Assert.That(fixture.Core.Executor.Disposals, Is.EqualTo(failure is "transport" or "timeout" ? 1 : 0));
    }

    [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
    public async Task InFlightClickCancellationOrTimeoutCannotRepeatSideEffect(bool legacy, bool cancel)
    {
        await using var fixture = await Fixture.StartAsync();
        var entered = Signal(); var release = Signal(); var finished = Signal(); var clicks = 0;
        using var caller = new CancellationTokenSource();
        fixture.OnCommand = command =>
        {
            if (command.Name != DriverCommand.ClickElement) return Ready(command);
            Interlocked.Increment(ref clicks);
            // Cancel at the side-effect boundary itself. Scheduling the test's
            // continuation must not race the independent timeout scenario.
            if (cancel) caller.Cancel();
            entered.TrySetResult(true);
            try { release.Task.GetAwaiter().GetResult(); return Reply(null); }
            finally { finished.TrySetResult(true); }
        };
        try
        {
            var pending = fixture.Wait("click", legacy, options: Options with
            { Timeout = cancel ? Options.Timeout : TimeSpan.FromSeconds(1) }, token: caller.Token);
            await Bounded(entered.Task);
            var error = await Catch(pending);
            if (cancel) Assert.That(((OperationCanceledException)error).CancellationToken, Is.EqualTo(caller.Token));
            else Assert.That(Category(error), Is.EqualTo(ErrorCategory.OperationTimedOut));
            Assert.That(fixture.Core.Browser.State, Is.EqualTo(BrowserState.CdpOnly));
            Assert.That(fixture.Core.Executor.Disposals, Is.EqualTo(1));
            release.TrySetResult(true); await Bounded(finished.Task);
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(fixture.Commands.Count(x => x.Name == DriverCommand.FindElement), Is.EqualTo(1));
        }
        finally { release.TrySetResult(true); }
    }

    [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
    public async Task ExpiredCommandCannotClickBeforeCancellationCleanupRuns(bool legacy, bool lease)
    {
        CancellationToken commandToken = default;
        using var expired = new ManualResetEventSlim();
        using var resumeCancellation = new ManualResetEventSlim();
        CancellationTokenRegistration registration = default;
        await using var fixture = await Fixture.StartAsync((stage, token) =>
        { if (stage == "command-dispatch") commandToken = token; return default; }, TimeSpan.FromSeconds(1));
        fixture.OnCommand = command =>
        {
            if (command.Name == DriverCommand.IsElementEnabled)
            {
                // Hold the cancellation callbacks after the token is signaled,
                // before executor detachment. This exposes the dispatch race
                // deterministically instead of depending on thread scheduling.
                registration = commandToken.Register(() =>
                { expired.Set(); resumeCancellation.Wait(Watchdog); });
                if (!expired.Wait(Watchdog)) throw new AssertionException("Command deadline did not expire.");
            }
            return Ready(command);
        };
        try
        {
            var error = await Catch(fixture.Wait("click", legacy, lease, Options with { Timeout = TimeSpan.FromSeconds(10) }));
            Assert.That(Category(error), Is.EqualTo(ErrorCategory.OperationTimedOut));
            Assert.That(fixture.Commands.Count(x => x.Name == DriverCommand.ClickElement), Is.Zero);
            Assert.That(fixture.Core.Browser.State, Is.EqualTo(BrowserState.CdpOnly));
            Assert.That(fixture.Core.Executor.Disposals, Is.EqualTo(1));
        }
        finally { resumeCancellation.Set(); registration.Dispose(); }
    }

    [TestCase(false, "dispose"), TestCase(true, "dispose")]
    [TestCase(false, "replace"), TestCase(true, "replace")]
    [TestCase(false, "cancel"), TestCase(true, "cancel")]
    public async Task InvalidationAfterReadinessPreventsClick(bool legacy, string change)
    {
        await using var fixture = await Fixture.StartAsync();
        var replacement = change == "replace" ? await fixture.Core.AddAttachmentAsync() : default;
        using var caller = new CancellationTokenSource();
        fixture.OnCommand = command =>
        {
            if (command.Name == DriverCommand.IsElementEnabled)
            {
                if (change == "dispose") fixture.Core.Lease.DisposeAsync().GetAwaiter().GetResult();
                else if (change == "replace") fixture.Core.Browser.ReplaceAttachmentForTest(replacement.Attachment!);
                else caller.Cancel();
            }
            return Ready(command);
        };
        var error = await Catch(fixture.Wait("click", legacy, true, token: caller.Token));
        if (change == "cancel") Assert.That(((OperationCanceledException)error).CancellationToken, Is.EqualTo(caller.Token));
        else Assert.That(Category(error), Is.EqualTo(ErrorCategory.StaleAttachment));
        Assert.That(fixture.Commands.Count(x => x.Name == DriverCommand.ClickElement), Is.Zero);
        if (change == "replace") Assert.That(replacement.Executor!.Commands, Is.Zero);
    }

    [TestCase(false), TestCase(true)]
    public async Task ClickKeepsCapturedNestedFramesAndCommandGateThroughAcknowledgement(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        var frames = new List<Locator> { Locator.Id("outer"), Locator.Id("inner") };
        var entered = Signal(); var release = Signal();
        var outer = 0; var enteredFrames = new List<string>();
        fixture.OnCommand = command =>
        {
            if (command.Name == DriverCommand.FindElement)
            {
                var selector = (string)command.Parameters!["value"]!;
                if (selector.Contains("outer"))
                {
                    frames.Clear();
                    if (++outer == 1) throw new NoSuchFrameException();
                    return Element("outer");
                }
                return Element(selector.Contains("inner") ? "inner" : "subject");
            }
            if (command.Name == DriverCommand.SwitchToFrame && command.Parameters!["id"] is Dictionary<string, object> id)
                enteredFrames.Add((string)id["element-6066-11e4-a52e-4f735466cecf"]);
            if (command.Name == DriverCommand.ClickElement)
            { entered.TrySetResult(true); release.Task.GetAwaiter().GetResult(); }
            return Ready(command);
        };
        try
        {
            var pending = fixture.Wait("click", legacy, options: Options with { FramePath = frames });
            await Bounded(entered.Task);
            var otherCommand = fixture.Core.Lease.Commands.SwitchToFrameAsync(null).AsTask();
            Assert.That(otherCommand.IsCompleted, Is.False, "Context changes cannot interleave with a click.");
            release.TrySetResult(true);
            await Bounded(pending); await Bounded(otherCommand);
            Assert.That(outer, Is.EqualTo(2));
            Assert.That(enteredFrames, Is.EqualTo(new[] { "outer", "inner" }));
            var commands = fixture.Commands.ToArray();
            Assert.That(commands[commands.Length - 2].Name, Is.EqualTo(DriverCommand.ClickElement));
            Assert.That(commands[commands.Length - 1].Name, Is.EqualTo(DriverCommand.SwitchToFrame));
        }
        finally { release.TrySetResult(true); }
    }

    [TestCase(false), TestCase(true)]
    public async Task InvalidSelectorOrClosedTargetFailsWithoutClick(bool legacy)
    {
        await using var fixture = await Fixture.StartAsync();
        fixture.OnCommand = command => command.Name == DriverCommand.FindElement ? throw new InvalidSelectorException() : Ready(command);
        Assert.That(Category(await Catch(fixture.Wait("click", legacy))), Is.EqualTo(ErrorCategory.ConfigurationError));
        Assert.That(Category(await Catch(fixture.Wait("click", legacy, options: Options with { Target = new TargetKey("closed") }))), Is.EqualTo(ErrorCategory.TargetClosed));
        Assert.That(fixture.Commands.Count(x => x.Name == DriverCommand.FindElement), Is.EqualTo(1));
        Assert.That(fixture.Commands.Count(x => x.Name == DriverCommand.ClickElement), Is.Zero);
    }
}
