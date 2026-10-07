using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using Legacy = BrowserDock.Legacy;
using Fixture = BrowserDock.Tests.ElementWaitTests.Fixture;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class TextWaitTests
{
    private static readonly ElementWaitOptions Options = new() { Timeout = TimeSpan.FromSeconds(5), PollInterval = TimeSpan.FromMilliseconds(1) };
    private static Task Bounded(Task task) => TaskCompatibility.WaitAsync(task, TimeSpan.FromSeconds(15));
    private static Response Reply(object? value) => new("fixture-session", value!, WebDriverResult.Success);
    private static Response Element(string id = "element") => Reply(new Dictionary<string, object> { ["element-6066-11e4-a52e-4f735466cecf"] = id });
    private static async Task Wait(Fixture f, string kind, bool legacy, bool lease = false, string text = "ready", ElementWaitOptions? options = null, CancellationToken token = default)
    {
        options ??= Options;
        if (legacy)
        {
            var b = new Legacy.Browser(f.Core.Browser); var c = new Legacy.GuardedCommands(f.Core.Lease.Commands); var l = Legacy.Locator.Id("subject");
            var o = new Legacy.ElementWaitOptions { Timeout = options.Timeout, PollInterval = options.PollInterval,
                Target = options.Target is { } t ? new Legacy.TargetKey(t.Value) : null, FramePath = options.FramePath.Select(Legacy.Locator.FromCore).ToArray() };
            if (kind == "hidden") { await (lease ? c.WaitForHiddenAsync(l, o, token) : b.WaitForHiddenAsync(l, o, token)); return; }
            var element = await (kind == "text" ? lease ? c.WaitForTextAsync(l, text, o, token) : b.WaitForTextAsync(l, text, o, token)
                : lease ? c.WaitForExactTextAsync(l, text, o, token) : b.WaitForExactTextAsync(l, text, o, token));
            Assert.That(element.Locator.Value, Is.EqualTo("subject"));
            Assert.That(await element.GetTextAsync(token), Is.Not.Null);
        }
        else
        {
            var b = f.Core.Browser; var c = f.Core.Lease.Commands; var l = Locator.Id("subject");
            if (kind == "hidden") { await (lease ? c.WaitForHiddenAsync(l, options, token) : b.WaitForHiddenAsync(l, options, token)); return; }
            var element = await (kind == "text" ? lease ? c.WaitForTextAsync(l, text, options, token) : b.WaitForTextAsync(l, text, options, token)
                : lease ? c.WaitForExactTextAsync(l, text, options, token) : b.WaitForExactTextAsync(l, text, options, token));
            Assert.That(element.Locator.Value, Is.EqualTo("subject"));
            Assert.That(await element.GetTextAsync(token), Is.Not.Null);
        }
    }
    private static async Task<Exception> Error(Task task)
    {
        try { await Bounded(task); }
        catch (Exception e) when (e is not TimeoutException) { return e; }
        throw new AssertionException("Expected failure.");
    }
    private static BrowserDockException CoreError(Exception e) => e is Legacy.BrowserDockException ? (BrowserDockException)e.InnerException! : (BrowserDockException)e;

    [TestCase(false, false, "text"), TestCase(true, false, "text"), TestCase(false, true, "text"), TestCase(true, true, "text")]
    [TestCase(false, false, "exact"), TestCase(true, false, "exact"), TestCase(false, true, "exact"), TestCase(true, true, "exact")]
    public async Task TextWaitsForVisibilityReacquiresStaleNodesAndKeepsCase(bool legacy, bool lease, string kind)
    {
        await using var f = await Fixture.StartAsync(); var finds = 0; var reads = 0;
        f.OnCommand = c => c.Name switch
        {
            var n when n == DriverCommand.FindElement => Element("element-" + ++finds),
            var n when n == DriverCommand.ExecuteScript => Reply(finds > 1),
            var n when n == DriverCommand.GetElementTagName => Reply("div"),
            var n when n == DriverCommand.GetElementText => ++reads switch
                { 1 => throw new StaleElementReferenceException(), 2 => Reply("READY"), _ => Reply(kind == "text" ? "already ready now" : "\t ready \n") },
            _ => Reply(null)
        };
        await Bounded(Wait(f, kind, legacy, lease));
        Assert.That(finds, Is.EqualTo(4));
        Assert.That(f.Commands.Any(c => c.Name == DriverCommand.ClickElement || c.Name == DriverCommand.ClearElement || c.Name == DriverCommand.SendKeysToElement), Is.False);
        Assert.That(f.Core.Executor.Disposals, Is.Zero);
    }

    [TestCase(false, "input", "text"), TestCase(true, "input", "text"), TestCase(false, "textarea", "text"), TestCase(true, "textarea", "text")]
    [TestCase(false, "input", "exact"), TestCase(true, "input", "exact"), TestCase(false, "textarea", "exact"), TestCase(true, "textarea", "exact")]
    public async Task InputWaitReadsLiveValueRatherThanTextOrAttribute(bool legacy, string tag, string kind)
    {
        await using var f = await Fixture.StartAsync(); var reads = 0;
        f.OnCommand = c => c.Name switch
        {
            var n when n == DriverCommand.FindElement => Element(),
            var n when n == DriverCommand.ExecuteScript => Reply(true),
            var n when n == DriverCommand.GetElementTagName => Reply(tag),
            var n when n == DriverCommand.GetElementProperty => Reply(++reads == 1 ? "original" : "ready"),
            var n when n == DriverCommand.GetElementText => Reply("unrelated"),
            _ => Reply(null)
        };
        await Bounded(Wait(f, kind, legacy, true));
        Assert.That(reads, Is.EqualTo(2));
        Assert.That(f.Commands.Any(c => c.Name == DriverCommand.GetElementAttribute), Is.False);
    }

    [TestCase(false, false, false), TestCase(true, false, false), TestCase(false, true, false), TestCase(true, true, false)]
    [TestCase(false, false, true), TestCase(true, false, true), TestCase(false, true, true), TestCase(true, true, true)]
    public async Task HiddenAcceptsHiddenOrRemovedButNotStale(bool legacy, bool lease, bool removed)
    {
        await using var f = await Fixture.StartAsync(); var finds = 0;
        f.OnCommand = c => c.Name switch
        {
            var n when n == DriverCommand.FindElement => ++finds switch
                { 1 => throw new StaleElementReferenceException(), 3 when removed => throw new NoSuchElementException(), _ => Element() },
            var n when n == DriverCommand.ExecuteScript => Reply(finds == 2),
            _ => Reply(null)
        };
        await Bounded(Wait(f, "hidden", legacy, lease));
        Assert.That(finds, Is.EqualTo(3));
        Assert.That(f.Core.Executor.Disposals, Is.Zero);
    }

    [TestCase(false, "text"), TestCase(true, "text"), TestCase(false, "exact"), TestCase(true, "exact"), TestCase(false, "hidden"), TestCase(true, "hidden")]
    public async Task MissingFrameTimesOutRatherThanSatisfyingCondition(bool legacy, string kind)
    {
        // Expire during the polling stage, after the observation releases the
        // command gate, so this specifically tests missing context, not cleanup
        // for an independently expiring running command.
        await using var f = await Fixture.StartAsync((stage, token) => stage == "element-wait-poll"
            ? new ValueTask(Task.Delay(Timeout.Infinite, token)) : default);
        f.OnCommand = c => c.Name == DriverCommand.FindElement ? throw new NoSuchElementException() : Reply(null);
        var error = CoreError(await Error(Wait(f, kind, legacy, options: Options with
            { Timeout = TimeSpan.FromSeconds(1), FramePath = new[] { Locator.Id("missing-frame") } })));
        Assert.That(error.Category, Is.EqualTo(ErrorCategory.OperationTimedOut));
        Assert.That(f.Core.Executor.Disposals, Is.Zero);
    }

    [TestCase(false, "text"), TestCase(true, "text"), TestCase(false, "exact"), TestCase(true, "exact"), TestCase(false, "hidden"), TestCase(true, "hidden")]
    public async Task QueuedCancellationPreservesCallerTokenWithoutDispatch(bool legacy, string kind)
    {
        await using var f = await Fixture.StartAsync(); using var caller = new CancellationTokenSource();
        await f.Core.Browser.CommandsGateForTest.WaitAsync();
        try
        {
            var waiting = Wait(f, kind, legacy, true, token: caller.Token); caller.Cancel();
            var error = (OperationCanceledException)await Error(waiting);
            Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
            Assert.That(f.Core.Executor.Commands, Is.Zero);
            Assert.That(f.Core.Executor.Disposals, Is.Zero);
        }
        finally { f.Core.Browser.CommandsGateForTest.Release(); }
    }

    [TestCase(false, "text"), TestCase(true, "text"), TestCase(false, "exact"), TestCase(true, "exact"), TestCase(false, "hidden"), TestCase(true, "hidden")]
    public async Task ClosedTargetAndInvalidSelectorRemainErrors(bool legacy, string kind)
    {
        await using var f = await Fixture.StartAsync();
        f.OnCommand = c => c.Name == DriverCommand.FindElement ? throw new InvalidSelectorException() : Reply(null);
        Assert.That(CoreError(await Error(Wait(f, kind, legacy))).Category, Is.EqualTo(ErrorCategory.ConfigurationError));
        Assert.That(CoreError(await Error(Wait(f, kind, legacy, options: Options with { Target = new TargetKey("closed") }))).Category, Is.EqualTo(ErrorCategory.TargetClosed));
    }

    [TestCase(false, "text"), TestCase(true, "text"), TestCase(false, "exact"), TestCase(true, "exact"), TestCase(false, "hidden"), TestCase(true, "hidden")]
    public async Task ReplacementBetweenPollsCannotCompleteOriginalWait(bool legacy, string kind)
    {
        Action? replace = null;
        await using var f = await Fixture.StartAsync((stage, _) => { if (stage == "element-wait-poll") replace!(); return default; });
        var replacement = await f.Core.AddAttachmentAsync();
        replace = () => f.Core.Browser.ReplaceAttachmentForTest(replacement.Attachment);
        f.OnCommand = c => c.Name switch
        {
            var n when n == DriverCommand.FindElement => Element(),
            var n when n == DriverCommand.ExecuteScript => Reply(true),
            var n when n == DriverCommand.GetElementTagName => Reply("div"),
            _ => Reply("pending")
        };
        Assert.That(CoreError(await Error(Wait(f, kind, legacy, true))).Category, Is.EqualTo(ErrorCategory.StaleAttachment));
        Assert.That(replacement.Executor.Commands, Is.Zero);
    }
}
