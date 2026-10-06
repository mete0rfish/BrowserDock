using NUnit.Framework;
using System.Text.Json;
using Legacy = BrowserDock.Legacy;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class NavigationDiagnosticsTests
{
    [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
    public async Task CancellationAndTimeoutKeepOriginalCauseAndContext(bool legacy, bool timeout)
    {
        using var caller = new CancellationTokenSource();
        OperationCanceledException? original = null;
        await using var browser = Browser.NavigationFixtureForTest(new()
        {
            Driver = new() { ExecutablePath = "unused" },
            Timeouts = new() { Navigation = timeout ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(10) },
            StageHook = async (stage, token) =>
            {
                if (stage != "navigation-probe") return;
                if (!timeout) caller.Cancel();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException error)
                {
                    original = error;
                    error.Data["Navigation.Stage"] = "navigation-events";
                    error.Data["Navigation.SessionId"] = "fixture-session";
                    error.Data["Navigation.Trace"] = "fixture-trace";
                    throw new NavigationCanceledException(caller.Token, true, error);
                }
            }
        }, BrowserState.CdpOnly);
        Task Navigate() => legacy
            ? new Legacy.Browser(browser).NavigateAsync(new Uri("http://fixture/download"), new() { Mode = Legacy.NavigationMode.CdpOnly }, caller.Token)
            : browser.NavigateAsync(new Uri("http://fixture/download"), new() { Mode = NavigationMode.CdpOnly }, caller.Token).AsTask();
        var error = Assert.CatchAsync<Exception>(async () => await TaskCompatibility.WaitAsync(Navigate(), TimeSpan.FromSeconds(15)))!;
        Assert.That(original, Is.Not.Null);
        Assert.That(error.Data["Navigation.Stage"], Is.EqualTo("navigation-events"));
        Assert.That(error.Data["Navigation.SessionId"], Is.EqualTo("fixture-session"));
        Assert.That(error.Data["Navigation.Trace"], Is.EqualTo("fixture-trace"));
        var core = legacy ? error.InnerException! : error;
        NavigationCanceledException cancellation;
        if (timeout)
        {
            Assert.That(core, Is.InstanceOf<BrowserDockException>());
            var mapped = (BrowserDockException)core;
            Assert.That(mapped.Category, Is.EqualTo(ErrorCategory.OperationTimedOut));
            Assert.That(mapped.OperationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(mapped.BrowserMayHaveAdvanced, Is.True);
            cancellation = (NavigationCanceledException)mapped.InnerException!;
        }
        else
        {
            Assert.That(error, Is.InstanceOf<OperationCanceledException>());
            Assert.That(((OperationCanceledException)error).CancellationToken, Is.EqualTo(caller.Token));
            cancellation = (NavigationCanceledException)core;
        }
        Assert.That(cancellation.InnerException, Is.SameAs(original));
        Assert.That(cancellation.BrowserMayHaveAdvanced, Is.True);
        Assert.That(cancellation.Message, Does.Contain("navigation-events"));
        Assert.That(cancellation.CancellationToken, Is.EqualTo(caller.Token));
    }

    [Test]
    public void LegacyProtocolFailureKeepsChromeErrorDetails()
    {
        var original = new BrowserDockException(ErrorCategory.ProtocolError, "Chrome rejected navigation: net::ERR_ABORTED");
        original.Data["Navigation.Stage"] = "Page.navigate";
        original.Data["Navigation.CdpErrorText"] = "net::ERR_ABORTED";
        var error = Assert.ThrowsAsync<Legacy.BrowserDockException>(async () =>
            await Legacy.Api.Call(() => Task.FromException(original)))!;
        Assert.That(error.InnerException, Is.SameAs(original));
        Assert.That(error.Data["Navigation.Stage"], Is.EqualTo("Page.navigate"));
        Assert.That(error.Data["Navigation.CdpErrorText"], Is.EqualTo("net::ERR_ABORTED"));
    }

    [TestCase(false), TestCase(true)]
    public void CleanupFailureKeepsItsCauseAndTraceAcrossFacades(bool legacy)
    {
        var original = new IOException("injected cleanup failure");
        var browser = Browser.NavigationFixtureForTest(new()
        {
            Driver = new() { ExecutablePath = "unused" },
            StageHook = (stage, _) => stage == "cleanup" ? throw original : default
        }, BrowserState.CdpOnly);
        var error = Assert.CatchAsync<Exception>(async () =>
        {
            if (legacy) await new Legacy.Browser(browser).StopAsync();
            else await browser.StopAsync();
        })!;
        var core = (BrowserDockException)(legacy ? error.InnerException! : error);
        Assert.That(core.Category, Is.EqualTo(ErrorCategory.CleanupIncomplete));
        Assert.That(((AggregateException)core.InnerException!).InnerExceptions, Does.Contain(original));
        Assert.That(error.Data["Cleanup.Trace"], Is.EqualTo(core.Data["Cleanup.Trace"]));
        using var trace = JsonDocument.Parse((string)error.Data["Cleanup.Trace"]!);
        Assert.That(trace.RootElement.EnumerateArray().Select(x => x.GetProperty("event").GetString()),
            Does.Contain("cleanup-start").And.Contain("cleanup-failed"));
    }

    [Test]
    public void DiagnosticTraceIsBoundedAndExcludesProtocolPayloads()
    {
        var trace = new BoundedDiagnosticTrace();
        using var parameters = JsonDocument.Parse("{\"requestId\":\"request\",\"url\":\"secret-url\",\"headers\":{\"Authorization\":\"secret-header\"},\"postData\":\"secret-body\",\"expression\":\"secret-script\"}");
        for (var i = 0; i < 100; i++) trace.Cdp("Network.requestWillBeSent", new string('s', 200), parameters.RootElement);
        trace.Add("last");
        var snapshot = trace.Snapshot();
        using var json = JsonDocument.Parse(snapshot);
        Assert.That(json.RootElement.GetArrayLength(), Is.EqualTo(64));
        Assert.That(json.RootElement[0].GetProperty("session").GetString()!.Length, Is.EqualTo(128));
        Assert.That(json.RootElement[63].GetProperty("event").GetString(), Is.EqualTo("last"));
        Assert.That(snapshot, Does.Not.Contain("secret-"));
    }

    [Test]
    public void TestCleanupRetainsPrimaryFailureAndStillFailsWhenThereIsNoPrimaryFailure()
    {
        var absent = Path.Combine(Path.GetTempPath(), "BrowserDock-absent-" + Guid.NewGuid().ToString("N"));
        var primary = new InvalidOperationException("primary failure");
        Assert.DoesNotThrow(() => TestFixtures.FailureEvidence.DeleteDirectory(absent, primary));
        Assert.That(primary.Data["TestCleanupFailure"], Is.InstanceOf<DirectoryNotFoundException>());
        Assert.Throws<DirectoryNotFoundException>(() => TestFixtures.FailureEvidence.DeleteDirectory(absent, null));
    }
}
