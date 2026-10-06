using System.Text.Json;
using NUnit.Framework;
using Legacy = BrowserDock.Legacy;

namespace BrowserDock.Tests;

[TestFixture, Category("Windows"), NonParallelizable]
public sealed class ScriptErrorBrowserTests
{
    [TestCase(false), TestCase(true)]
    public async Task ThrowingScriptPreservesLiveSessionAndPartialEffects(bool legacy)
    {
        if (!Platform.IsWindows || !Platform.IsX64Process || Platform.WindowsBuild < 22000) Assert.Ignore("Requires the Windows browser fixture.");
        var chrome = Environment.GetEnvironmentVariable("BROWSERDOCK_CHROME");
        var driver = Environment.GetEnvironmentVariable("BROWSERDOCK_DRIVER");
        Assert.That(File.Exists(chrome) && File.Exists(driver), Is.True, "Supply the pinned Chrome/driver fixture.");
        await using var browser = await Browser.StartAsync(new() { ChromeBinaryPath = chrome, Driver = new() { ExecutablePath = driver! } });
        await VerifyAsync(browser, legacy);
    }

    internal static async Task VerifyAsync(Browser browser, bool legacy)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        await using var lease = await browser.GetWebDriverAsync(token);
        var facade = new Legacy.GuardedCommands(lease.Commands);
        Task<JsonElement> Script(string source) => legacy ? facade.ExecuteScriptAsync(source, cancellationToken: token)
            : lease.Commands.ExecuteScriptAsync(source, cancellationToken: token).AsTask();
        var epoch = browser.AttachmentEpoch; var generation = browser.SessionGeneration;
        await Script("window.scriptCalls=0");
        var expected = 0;
        foreach (var source in new[]
        {
            "window.scriptCalls++; throw new Error('script failed')",
            "window.scriptCalls++; throw new Error('operation not supported in page script')",
            "window.scriptCalls++; return ("
        })
        {
            if (!source.EndsWith("(")) expected++;
            if (legacy)
            {
                var error = Assert.ThrowsAsync<Legacy.BrowserDockException>(async () => await Script(source));
                Assert.That(error!.Category, Is.EqualTo(Legacy.ErrorCategory.ScriptExecutionFailure));
                Assert.That(error.OperationId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(error.Diagnostic, Is.Not.Null);
                Assert.That(error.CleanupFailures, Is.Empty);
            }
            else
            {
                var error = Assert.ThrowsAsync<BrowserDockException>(async () => await Script(source));
                Assert.That(error!.Category, Is.EqualTo(ErrorCategory.ScriptExecutionFailure));
                Assert.That(error.OperationId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(error.Diagnostic, Is.Not.Null);
                Assert.That(error.CleanupFailures, Is.Empty);
            }
            Assert.That(browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
            Assert.That(browser.AttachmentEpoch, Is.EqualTo(epoch));
            Assert.That(browser.SessionGeneration, Is.EqualTo(generation));
            Assert.That((await Script("return window.scriptCalls")).GetInt32(), Is.EqualTo(expected), "Side effects must not be undone or replayed.");
            _ = await (legacy ? facade.GetTitleAsync(token) : lease.Commands.GetTitleAsync(token).AsTask());
        }
        Assert.That((await Script("return 7")).GetInt32(), Is.EqualTo(7));
    }
}
