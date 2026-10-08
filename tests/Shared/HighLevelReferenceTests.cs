using NUnit.Framework;
using BrowserDock.TestFixtures;

namespace BrowserDock.Tests;

[TestFixture, Category("Windows"), Category("ReferenceInteraction"), NonParallelizable]
public sealed class HighLevelReferenceTests
{
    [Test]
    public async Task HighLevelScenarioMatchesManifest()
    {
        if (!Platform.IsWindows || !Platform.IsX64Process || Platform.WindowsBuild < 22000) Assert.Ignore("Requires the Windows reference fixture.");
        var configuredUrl = Environment.GetEnvironmentVariable("BROWSERDOCK_REFERENCE_URL");
        using var fixture = configuredUrl is null ? await FrameworkTests.FixtureServer.StartAsync() : null;
        var report = HighLevelInteractionReference.Report(); Browser? browser = null; Exception? failure = null;
        var chromePath = Environment.GetEnvironmentVariable("BROWSERDOCK_CHROME");
        var driverPath = Environment.GetEnvironmentVariable("BROWSERDOCK_DRIVER")!;
        try
        {
            browser = await Browser.StartAsync(new() { ChromeBinaryPath = chromePath, Driver = new() { ExecutablePath = driverPath } });
            using var process = System.Diagnostics.Process.GetProcessById(browser.Health.ChromePid!.Value); var created = process.StartTime;
            await browser.NavigateAsync(new Uri((configuredUrl ?? fixture!.Url.ToString().TrimEnd('/')) + "/reference/interactions"));
            var observed = await HighLevelInteractionReference.ObserveAsync(browser, Environment.GetEnvironmentVariable("BROWSERDOCK_REFERENCE_FACADE") == "legacy");
            report["observed"] = observed; HighLevelInteractionReference.Check(observed);
            await HighLevelInteractionReference.CheckBrowserVersionAsync(browser, report);
            report["chromePreserved"] = !process.HasExited && process.StartTime == created;
            Assert.That((bool)report["chromePreserved"]!, Is.True);
            report["sessionChanged"] = false; report["passed"] = true;
        }
        catch (Exception error) { failure = error; report["error"] = error.GetType().Name; throw; }
        finally
        {
            try
            {
                if (browser is not null) await browser.DisposeAsync();
                report["cleanupPassed"] = browser is not null && browser.State == BrowserState.Stopped;
            }
            catch (Exception error) { report["cleanupPassed"] = false; report["passed"] = false; report["cleanupError"] = error.GetType().Name; if (failure is null) throw; }
            finally { await HighLevelInteractionReference.WriteAsync(report); }
        }
    }
}
