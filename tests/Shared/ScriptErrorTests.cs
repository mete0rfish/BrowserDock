using System.Text.Json;
using BrowserDock.FrameworkTests;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using Legacy = BrowserDock.Legacy;
using Fixture = BrowserDock.Tests.ElementWaitTests.Fixture;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class ScriptErrorTests
{
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(15);
    private static Response Reply(object? value) => new("fixture-session", value!, WebDriverResult.Success);
    private static Response Failure(string message, WebDriverResult status = WebDriverResult.UnexpectedJavaScriptError)
        => new("fixture-session", new Dictionary<string, object> { ["message"] = message }, status);
    private static bool Submit(Command c) => c.Name == DriverCommand.ExecuteScript && ((string)c.Parameters!["script"]!).Contains("browserDockSubmit");
    private static Task<JsonElement> Script(Fixture f, bool legacy, string script)
        => legacy ? new Legacy.GuardedCommands(f.Core.Lease.Commands).ExecuteScriptAsync(script)
            : f.Core.Lease.Commands.ExecuteScriptAsync(script).AsTask();
    private static async Task<BrowserDockException> Error(Task task, bool legacy)
    {
        Exception? error = null;
        try { await TaskCompatibility.WaitAsync(task, Watchdog); }
        catch (Exception e) when (e is not TimeoutException) { error = e; }
        Assert.That(error, Is.InstanceOf(legacy ? typeof(Legacy.BrowserDockException) : typeof(BrowserDockException)));
        if (legacy)
        {
            var facade = (Legacy.BrowserDockException)error!;
            var core = (BrowserDockException)facade.InnerException!;
            Assert.That((int)facade.Category, Is.EqualTo((int)core.Category));
            Assert.That(facade.OperationId, Is.EqualTo(core.OperationId));
            Assert.That(facade.Diagnostic, Is.Not.Null);
            Assert.That(facade.Retryable, Is.False);
            Assert.That(facade.CleanupFailures, Is.Empty);
            return core;
        }
        return (BrowserDockException)error!;
    }

    [TestCase(false, "script failed"), TestCase(true, "script failed")]
    [TestCase(false, "operation not supported in page script"), TestCase(true, "operation not supported in page script")]
    public async Task ScriptErrorPreservesAttachmentDiagnosticsAndOriginalLease(bool legacy, string message)
    {
        await using var f = await Fixture.StartAsync();
        var epoch = f.Core.Browser.AttachmentEpoch; var generation = f.Core.Browser.SessionGeneration; var attempts = 0;
        var fail = true;
        f.OnCommand = c =>
        {
            if (c.Name != DriverCommand.ExecuteScript) return Reply("fixture");
            attempts++; return fail ? Failure(message) : Reply(7);
        };
        var error = await Error(Script(f, legacy, "throw new Error('script failed')"), legacy);
        Assert.Multiple(() =>
        {
            Assert.That(error.Category, Is.EqualTo(ErrorCategory.ScriptExecutionFailure));
            Assert.That(error.InnerException, Is.InstanceOf<JavaScriptException>());
            Assert.That(error.InnerException!.Message, Does.Contain(message));
            Assert.That(error.OperationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(error.Diagnostic, Is.Not.Null);
            Assert.That(error.CleanupFailures, Is.Empty);
            Assert.That(error.Retryable, Is.False);
            Assert.That(attempts, Is.EqualTo(1), "A script may have changed the page before throwing; never replay it.");
            Assert.That(f.Core.Browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
            Assert.That(f.Core.Browser.AttachmentEpoch, Is.EqualTo(epoch));
            Assert.That(f.Core.Browser.SessionGeneration, Is.EqualTo(generation));
            Assert.That(f.Core.Executor.Disposals, Is.Zero);
            Assert.That(f.Core.Attachment.Process.Alive, Is.True);
        });
        fail = false;
        Assert.That((await TaskCompatibility.WaitAsync(Script(f, legacy, "return 7"), Watchdog)).GetInt32(), Is.EqualTo(7));
        Assert.That(await TaskCompatibility.WaitAsync(f.Core.Title(legacy), Watchdog), Is.EqualTo("fixture"));
        Assert.That(attempts, Is.EqualTo(2));
    }

    [TestCase(false, false, false), TestCase(true, false, false)]
    [TestCase(false, true, false), TestCase(true, true, false)]
    [TestCase(false, false, true), TestCase(true, false, true)]
    [TestCase(false, true, true), TestCase(true, true, true)]
    public async Task SubmissionScriptErrorAfterInputIsNotReplayed(bool legacy, bool clear, bool lease)
    {
        await using var f = await Fixture.StartAsync();
        f.OnCommand = c => c.Name switch
        {
            var n when n == DriverCommand.FindElement => Reply(new Dictionary<string, object> { ["element-6066-11e4-a52e-4f735466cecf"] = "input" }),
            var n when n == DriverCommand.ExecuteScript => Submit(c) ? Failure("submission script failed") : Reply(true),
            var n when n == DriverCommand.IsElementEnabled => Reply(true),
            var n when n == DriverCommand.GetElementTagName => Reply("input"),
            var n when n == DriverCommand.GetElementProperty => Reply((string)c.Parameters!["name"]! == "type" ? "text" : "false"),
            _ => Reply("fixture")
        };
        Task Input()
        {
            if (legacy)
            {
                var b = new Legacy.Browser(f.Core.Browser); var commands = new Legacy.GuardedCommands(f.Core.Lease.Commands);
                var locator = Legacy.Locator.Id("subject");
                return clear ? lease ? commands.TypeAsync(locator, "value\n") : b.TypeAsync(locator, "value\n")
                    : lease ? commands.SendKeysAsync(locator, "value\n") : b.SendKeysAsync(locator, "value\n");
            }
            var l = Locator.Id("subject");
            return (clear ? lease ? f.Core.Lease.Commands.TypeAsync(l, "value\n") : f.Core.Browser.TypeAsync(l, "value\n")
                : lease ? f.Core.Lease.Commands.SendKeysAsync(l, "value\n") : f.Core.Browser.SendKeysAsync(l, "value\n")).AsTask();
        }
        var error = await Error(Input(), legacy);
        Assert.That(error.Category, Is.EqualTo(ErrorCategory.ScriptExecutionFailure));
        Assert.That(f.Commands.Count(c => c.Name == DriverCommand.FindElement), Is.EqualTo(1));
        Assert.That(f.Commands.Count(c => c.Name == DriverCommand.ClearElement), Is.EqualTo(clear ? 1 : 0));
        Assert.That(f.Commands.Count(c => c.Name == DriverCommand.SendKeysToElement), Is.EqualTo(1));
        Assert.That(f.Commands.Count(Submit), Is.EqualTo(1));
        Assert.That(f.Core.Executor.Disposals, Is.Zero);
        Assert.That(f.Core.Browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
        Assert.That(await TaskCompatibility.WaitAsync(f.Core.Title(legacy), Watchdog), Is.EqualTo("fixture"));
    }

    [TestCase(false), TestCase(true)]
    public async Task UnclassifiedDriverFailureStillInvalidatesAttachment(bool legacy)
    {
        await using var f = await Fixture.StartAsync();
        f.OnCommand = c => c.Name == DriverCommand.ExecuteScript ? Failure("JavaScript response was lost", WebDriverResult.UnknownError) : Reply("fixture");
        var error = await Error(Script(f, legacy, "return 7"), legacy);
        Assert.That(error.Category, Is.EqualTo(ErrorCategory.AttachmentLost));
        Assert.That(f.Core.Browser.State, Is.EqualTo(BrowserState.CdpOnly));
        Assert.That(f.Core.Executor.Disposals, Is.EqualTo(1));
        Assert.That(f.Core.Attachment.Process.Alive, Is.False);
        var sent = f.Core.Executor.Commands;
        var stale = await Error(Script(f, legacy, "return 7"), legacy);
        Assert.That(stale.Category, Is.EqualTo(ErrorCategory.StaleAttachment));
        Assert.That(f.Core.Executor.Commands, Is.EqualTo(sent));
    }
}
