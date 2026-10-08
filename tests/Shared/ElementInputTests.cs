using BrowserDock.FrameworkTests;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using Legacy = BrowserDock.Legacy;
using Fixture = BrowserDock.Tests.ElementWaitTests.Fixture;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class ElementInputTests
{
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(15);
    private static readonly ElementWaitOptions Options = new() { Timeout = TimeSpan.FromSeconds(5), PollInterval = TimeSpan.FromMilliseconds(1) };
    private static readonly string AppendPrefix = Keys.Control + Keys.End + Keys.Null;
    private static Task Bounded(Task task) => TaskCompatibility.WaitAsync(task, Watchdog);
    private static Response Reply(object? value) => new("fixture-session", value!, WebDriverResult.Success);
    private static Response Ready(Command command) => command.Name switch
    {
        var n when n == DriverCommand.FindElement => Reply(new Dictionary<string, object> { ["element-6066-11e4-a52e-4f735466cecf"] = "input" }),
        var n when n == DriverCommand.ExecuteScript || n == DriverCommand.IsElementEnabled => Reply(true),
        var n when n == DriverCommand.GetElementTagName => Reply("input"),
        var n when n == DriverCommand.GetElementProperty => Reply((string)command.Parameters!["name"]! == "type" ? "text" : "false"),
        _ => Reply(null)
    };
    private static bool Submit(Command command) => command.Name == DriverCommand.ExecuteScript && ((string)command.Parameters!["script"]!).Contains("browserDockSubmit");
    private static string[] Actions(Fixture f) => f.Commands.Where(c => c.Name == DriverCommand.ClearElement || c.Name == DriverCommand.SendKeysToElement || Submit(c))
        .Select(c => Submit(c) ? "submit" : c.Name == DriverCommand.ClearElement ? "clear" : "send:" + (string)c.Parameters!["text"]!).ToArray();
    private static Task Input(Fixture f, bool legacy, bool clear, string text, bool lease = false, ElementWaitOptions? options = null, CancellationToken token = default)
    {
        options ??= Options;
        if (legacy)
        {
            var b = new Legacy.Browser(f.Core.Browser); var c = new Legacy.GuardedCommands(f.Core.Lease.Commands);
            var l = Legacy.Locator.Id("subject");
            var o = new Legacy.ElementWaitOptions { Timeout = options.Timeout, PollInterval = options.PollInterval,
                Target = options.Target is { } target ? new Legacy.TargetKey(target.Value) : null,
                FramePath = options.FramePath.Select(Legacy.Locator.FromCore).ToArray() };
            return clear ? lease ? c.TypeAsync(l, text, o, token) : b.TypeAsync(l, text, o, token)
                : lease ? c.SendKeysAsync(l, text, o, token) : b.SendKeysAsync(l, text, o, token);
        }
        var browser = f.Core.Browser; var commands = f.Core.Lease.Commands; var locator = Locator.Id("subject");
        return (clear ? lease ? commands.TypeAsync(locator, text, options, token) : browser.TypeAsync(locator, text, options, token)
            : lease ? commands.SendKeysAsync(locator, text, options, token) : browser.SendKeysAsync(locator, text, options, token)).AsTask();
    }
    private static async Task<Exception> Catch(Task task)
    {
        try { await Bounded(task); }
        catch (Exception e) when (e is not TimeoutException) { return e; }
        throw new AssertionException("Expected failure.");
    }
    private static ErrorCategory Category(Exception e) => (e is Legacy.BrowserDockException ? (BrowserDockException)e.InnerException! : (BrowserDockException)e).Category;

    [TestCase(false, false, false), TestCase(true, false, false), TestCase(false, true, false), TestCase(true, true, false)]
    [TestCase(false, false, true), TestCase(true, false, true), TestCase(false, true, true), TestCase(true, true, true)]
    public async Task ReplaceAndAppendHaveDistinctOrderedSideEffects(bool legacy, bool lease, bool clear)
    {
        await using var f = await Fixture.StartAsync(); f.OnCommand = Ready;
        await Bounded(Input(f, legacy, clear, "한글 e\u0301 😀", lease));
        Assert.That(Actions(f), Is.EqualTo(clear ? new[] { "clear", "send:한글 e\u0301 😀" } : new[] { "send:" + AppendPrefix + "한글 e\u0301 😀" }));
        Assert.That(f.Core.Browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
    }

    [TestCase(false, ""), TestCase(true, "")]
    [TestCase(false, "\n"), TestCase(true, "\n")]
    [TestCase(false, "text\n"), TestCase(true, "text\n")]
    [TestCase(false, "text\r\n"), TestCase(true, "text\r\n")]
    [TestCase(false, "a\nb"), TestCase(true, "a\nb")]
    public async Task FinalLineEndingSubmitsOnceAndInteriorTextIsPreserved(bool legacy, string text)
    {
        await using var f = await Fixture.StartAsync(); f.OnCommand = Ready;
        await Bounded(Input(f, legacy, false, text));
        var expected = text switch { "" => Array.Empty<string>(), "\n" => new[] { "submit" },
            "text\n" or "text\r\n" => new[] { "send:" + AppendPrefix + "text", "submit" }, _ => new[] { "send:" + AppendPrefix + text } };
        Assert.That(Actions(f), Is.EqualTo(expected));
    }

    [TestCase(false), TestCase(true)]
    public async Task ReadinessRetriesMissingHiddenDisabledReadOnlyAndStaleObservations(bool legacy)
    {
        await using var f = await Fixture.StartAsync(); var finds = 0;
        f.OnCommand = c =>
        {
            if (c.Name == DriverCommand.FindElement && ++finds == 1) throw new NoSuchElementException();
            if (c.Name == DriverCommand.ExecuteScript) return Reply(finds != 2);
            if (c.Name == DriverCommand.IsElementEnabled) return Reply(finds != 3);
            if (c.Name == DriverCommand.GetElementProperty && (string)c.Parameters!["name"]! == "readOnly")
                return finds == 5 ? throw new StaleElementReferenceException() : Reply(finds == 4 ? "true" : "false");
            return Ready(c);
        };
        await Bounded(Input(f, legacy, true, "ready"));
        Assert.That(finds, Is.EqualTo(6));
        Assert.That(Actions(f), Is.EqualTo(new[] { "clear", "send:ready" }));
    }

    [TestCase(false, "textarea"), TestCase(true, "textarea")]
    [TestCase(false, "contenteditable"), TestCase(true, "contenteditable")]
    public async Task SupportsTextareaAndContentEditable(bool legacy, string kind)
    {
        await using var f = await Fixture.StartAsync();
        f.OnCommand = c => c.Name == DriverCommand.GetElementTagName ? Reply(kind == "textarea" ? "textarea" : "div")
            : c.Name == DriverCommand.GetElementProperty && (string)c.Parameters!["name"]! == "isContentEditable" ? Reply(kind == "contenteditable" ? "true" : "false") : Ready(c);
        await Bounded(Input(f, legacy, true, "text"));
        Assert.That(Actions(f), Is.EqualTo(new[] { "clear", "send:text" }));
    }

    [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
    public async Task EditableDescendantCannotClearInputOrSubmit(bool legacy, bool clear)
    {
        var recoveries = 0;
        await using var f = await Fixture.StartAsync((stage, _) =>
        { if (stage == "command-recovery") Interlocked.Increment(ref recoveries); return default; });
        f.OnCommand = c => c.Name == DriverCommand.GetElementTagName ? Reply("span")
            : c.Name == DriverCommand.GetElementProperty && (string)c.Parameters!["name"]! == "isContentEditable" ? Reply("true")
            : c.Name == DriverCommand.ExecuteScript && ((string)c.Parameters!["script"]!).Contains("browserDockEditingHost") ? Reply(false)
            : Ready(c);
        Assert.That(Category(await Catch(Input(f, legacy, clear, "text\n", options: Options with
        { Timeout = TimeSpan.FromSeconds(1), PollInterval = TimeSpan.FromSeconds(5) }))), Is.EqualTo(ErrorCategory.OperationTimedOut));
        Assert.That(Actions(f), Is.Empty);
        AssertObservationTimeoutState(f, recoveries);
    }

    [TestCase(false, "file"), TestCase(true, "file")]
    [TestCase(false, "checkbox"), TestCase(true, "checkbox")]
    [TestCase(false, "date"), TestCase(true, "date")]
    [TestCase(false, "readonly"), TestCase(true, "readonly")]
    public async Task UnsupportedOrReadOnlyInputCannotBeMadeEditableByContentEditable(bool legacy, string kind)
    {
        var recoveries = 0;
        await using var f = await Fixture.StartAsync((stage, _) =>
        { if (stage == "command-recovery") Interlocked.Increment(ref recoveries); return default; });
        f.OnCommand = c =>
        {
            if (c.Name == DriverCommand.GetElementProperty)
                return Reply((string)c.Parameters!["name"]! switch
                { "isContentEditable" => "true", "readOnly" => kind == "readonly" ? "true" : "false", "type" => kind == "readonly" ? "text" : kind, _ => null });
            return Ready(c);
        };
        Assert.That(Category(await Catch(Input(f, legacy, true, "text", options: Options with
        { Timeout = TimeSpan.FromSeconds(1), PollInterval = TimeSpan.FromSeconds(5) }))), Is.EqualTo(ErrorCategory.OperationTimedOut));
        Assert.That(Actions(f), Is.Empty);
        AssertObservationTimeoutState(f, recoveries);
    }

    private static void AssertObservationTimeoutState(Fixture f, int recoveries)
    {
        // A deadline during polling retains the attachment. Expiry while the
        // observation command is still running requires bounded recovery instead.
        Assert.That(recoveries, Is.InRange(0, 1));
        Assert.That(f.Core.Executor.Disposals, Is.EqualTo(recoveries));
        Assert.That(f.Core.Browser.State, Is.EqualTo(recoveries == 0 ? BrowserState.WebDriverAttached : BrowserState.CdpOnly));
    }

    [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
    public async Task InFlightUnsupportedEditorObservationRecoversWithoutInput(bool legacy, bool clear)
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovering = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var f = await Fixture.StartAsync((stage, _) =>
        { if (stage == "command-recovery") recovering.TrySetResult(true); return default; });
        f.Core.Attachment.BeforeDispatchForTest = () => Task.CompletedTask;
        f.OnCommand = c => c.Name == DriverCommand.GetElementTagName ? Reply("span")
            : c.Name == DriverCommand.ExecuteScript && ((string)c.Parameters!["script"]!).Contains("browserDockEditingHost")
                ? HoldObservation() : Ready(c);
        try
        {
            var pending = Input(f, legacy, clear, "text\n", options: Options with
            { Timeout = TimeSpan.FromSeconds(1), PollInterval = TimeSpan.FromSeconds(5) });
            await Bounded(entered.Task); await Bounded(recovering.Task);
            Assert.That(Category(await Catch(pending)), Is.EqualTo(ErrorCategory.OperationTimedOut));
            Assert.That(Actions(f), Is.Empty);
            AssertObservationTimeoutState(f, 1);
        }
        finally
        {
            release.Set();
            if (f.Core.Attachment.InvocationForTest is { } worker)
                try { await Bounded(worker); } catch (OperationCanceledException) { }
        }

        Response HoldObservation() { entered.TrySetResult(true); release.Wait(); return Reply(false); }
    }

    [TestCase(false, "clear"), TestCase(true, "clear")]
    [TestCase(false, "send"), TestCase(true, "send")]
    [TestCase(false, "submit"), TestCase(true, "submit")]
    public async Task PartialInputOrSubmissionFailureIsNeverReplayed(bool legacy, string failedStage)
    {
        await using var f = await Fixture.StartAsync();
        f.OnCommand = c =>
        {
            if (failedStage == "clear" && c.Name == DriverCommand.ClearElement) throw new StaleElementReferenceException();
            if (failedStage == "send" && c.Name == DriverCommand.SendKeysToElement) throw new WebDriverException("response lost after input");
            if (failedStage == "submit" && Submit(c)) throw new WebDriverException("response lost after submit");
            return Ready(c);
        };
        var error = await Catch(Input(f, legacy, true, "text\n"));
        Assert.That(Category(error), Is.EqualTo(failedStage == "clear" ? ErrorCategory.StaleDomElement : ErrorCategory.AttachmentLost));
        Assert.That(Actions(f), Is.EqualTo(failedStage == "clear" ? new[] { "clear" } : failedStage == "send" ? new[] { "clear", "send:text" } : new[] { "clear", "send:text", "submit" }));
        Assert.That(f.Commands.Count(c => c.Name == DriverCommand.FindElement), Is.EqualTo(1));
    }

    [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
    public async Task CancellationOrLeaseDisposalAfterClearStopsInput(bool legacy, bool dispose)
    {
        await using var f = await Fixture.StartAsync(); using var caller = new CancellationTokenSource();
        f.OnCommand = c =>
        {
            if (c.Name == DriverCommand.ClearElement)
            { if (dispose) f.Core.Lease.DisposeAsync().GetAwaiter().GetResult(); else caller.Cancel(); }
            return Ready(c);
        };
        var error = await Catch(Input(f, legacy, true, "text\n", true, token: caller.Token));
        if (dispose) Assert.That(Category(error), Is.EqualTo(ErrorCategory.StaleAttachment));
        else Assert.That(((OperationCanceledException)error).CancellationToken, Is.EqualTo(caller.Token));
        Assert.That(Actions(f), Is.EqualTo(new[] { "clear" }));
    }

    [TestCase(false, "ready"), TestCase(true, "ready")]
    [TestCase(false, "clear"), TestCase(true, "clear")]
    [TestCase(false, "send"), TestCase(true, "send")]
    public async Task CommandDeadlinePreventsEveryRemainingSideEffect(bool legacy, string stage)
    {
        CancellationToken commandToken = default;
        using var expired = new ManualResetEventSlim(); using var resume = new ManualResetEventSlim();
        CancellationTokenRegistration registration = default;
        await using var f = await Fixture.StartAsync((name, token) =>
        { if (name == "command-dispatch") commandToken = token; return default; }, TimeSpan.FromSeconds(1));
        f.OnCommand = c =>
        {
            if ((stage == "ready" && c.Name == DriverCommand.GetElementProperty && (string)c.Parameters!["name"]! == "type") ||
                (stage == "clear" && c.Name == DriverCommand.ClearElement) || (stage == "send" && c.Name == DriverCommand.SendKeysToElement))
            {
                registration = commandToken.Register(() => { expired.Set(); resume.Wait(Watchdog); });
                if (!expired.Wait(Watchdog)) throw new AssertionException("Command deadline did not expire.");
            }
            return Ready(c);
        };
        try
        {
            Assert.That(Category(await Catch(Input(f, legacy, true, "text\n", options: Options with { Timeout = TimeSpan.FromSeconds(10) }))), Is.EqualTo(ErrorCategory.OperationTimedOut));
            Assert.That(Actions(f), Is.EqualTo(stage == "ready" ? Array.Empty<string>() : stage == "clear" ? new[] { "clear" } : new[] { "clear", "send:text" }));
        }
        finally { resume.Set(); registration.Dispose(); }
    }

    [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
    public async Task QueuedCancellationOrDeadlineCannotInput(bool legacy, bool cancel)
    {
        await using var f = await Fixture.StartAsync(); using var caller = new CancellationTokenSource();
        await f.Core.Browser.CommandsGateForTest.WaitAsync();
        try
        {
            var pending = Input(f, legacy, true, "text", options: Options with { Timeout = cancel ? Options.Timeout : TimeSpan.FromMilliseconds(200) }, token: caller.Token);
            if (cancel) caller.Cancel();
            var error = await Catch(pending);
            if (cancel) Assert.That(((OperationCanceledException)error).CancellationToken, Is.EqualTo(caller.Token));
            else Assert.That(Category(error), Is.EqualTo(ErrorCategory.OperationTimedOut));
            Assert.That(f.Core.Executor.Commands, Is.Zero); Assert.That(f.Core.Executor.Disposals, Is.Zero);
        }
        finally { f.Core.Browser.CommandsGateForTest.Release(); }
    }

    [TestCase(false), TestCase(true)]
    public async Task MissingFormFailsAfterSingleInputWithoutLosingAttachment(bool legacy)
    {
        await using var f = await Fixture.StartAsync(); f.OnCommand = c => Submit(c) ? Reply(false) : Ready(c);
        Assert.That(Category(await Catch(Input(f, legacy, false, "text\n"))), Is.EqualTo(ErrorCategory.ElementInteractionFailure));
        Assert.That(Actions(f), Is.EqualTo(new[] { "send:" + AppendPrefix + "text", "submit" }));
        Assert.That(f.Core.Executor.Disposals, Is.Zero);
    }
}
