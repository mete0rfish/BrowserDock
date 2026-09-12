using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace BrowserDock.Tests;

[TestFixture, Category("Windows"), Category("Reference"), NonParallelizable]
public sealed class ReferenceTests
{
    private const string Observation = """
        JSON.stringify({path:location.pathname+location.hash,title:document.title,
        button:document.querySelector('#button').textContent,input:document.querySelector('#input').value,
        frame:document.querySelector('#frame').contentDocument.querySelector('#frame-input').value,
        storage:localStorage.getItem('fixture'),cookie:document.cookie.split('; ').includes('fixture=kept'),
        marker:document.body.dataset.marker||''})
        """;

    [TestCase("SB-01"), TestCase("SB-02"), TestCase("SB-03"), TestCase("SB-04")]
    public async Task SharedScenarioMatchesManifest(string scenarioId)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess || Environment.OSVersion.Version.Build < 22000)
            Assert.Ignore("Requires Windows 11 x64 interactive desktop.");
        var chromePath = Environment.GetEnvironmentVariable("BROWSERDOCK_CHROME");
        var driverPath = Environment.GetEnvironmentVariable("BROWSERDOCK_DRIVER");
        Assert.That(File.Exists(chromePath) && File.Exists(driverPath), Is.True, "Set compatible BROWSERDOCK_CHROME and BROWSERDOCK_DRIVER paths.");
        await using var local = await WindowsTests.FixtureAsync();
        var url = new Uri(Environment.GetEnvironmentVariable("BROWSERDOCK_REFERENCE_URL") ?? local.Url.AbsoluteUri);
        Assert.That(url.Scheme == "http" && url.IsLoopback, Is.True, "Reference tests only accept a local HTTP fixture.");
        var report = new JsonObject { ["scenarioId"] = scenarioId, ["implementation"] = "BrowserDock", ["passed"] = false,
            ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription };
        var clock = Stopwatch.StartNew();
        Browser? browser = null;
        Process? chrome = null;
        Exception? failure = null;
        try
        {
            browser = await Browser.StartAsync(new() { ChromeBinaryPath = chromePath, Driver = new() { ExecutablePath = driverPath! } });
            chrome = Process.GetProcessById(browser.Health.ChromePid!.Value);
            var creation = chrome.StartTime;
            await browser.NavigateAsync(new Uri(url, "/reference/page"));
            await using var old = await browser.GetWebDriverAsync();
            await old.Commands.ExecuteScriptAsync("localStorage.setItem('fixture','kept');document.cookie='fixture=kept;path=/';document.body.dataset.marker='kept'");
            var before = browser.TestSnapshot;
            if (scenarioId == "SB-01")
            {
                await (await old.Commands.FindAsync(Locator.Id("button"))).ClickAsync();
                await (await old.Commands.FindAsync(Locator.Id("input"))).SendKeysAsync("reference");
                await (await old.Commands.FindAsync(Locator.Id("frame-input"), new() { FramePath = [Locator.Id("frame")] })).SendKeysAsync("frame-value");
                await old.Commands.SwitchToFrameAsync(null);
            }
            else if (scenarioId == "SB-02")
            {
                await browser.DisconnectWebDriverAsync();
                await browser.ExecuteCdpAsync("Browser.getVersion");
                await using var fresh = await browser.ReconnectWebDriverAsync();
                Assert.That(browser.TestSnapshot.SessionId, Is.Not.EqualTo(before.SessionId));
                Assert.That(fresh.Generation, Is.EqualTo(old.Generation + 1));
                Assert.ThrowsAsync<StaleAttachmentException>(async () => await old.Commands.GetTitleAsync());
                report["sessionChanged"] = true;
            }
            else if (scenarioId == "SB-03")
            {
                await browser.NavigateAsync(new Uri(url, "/reference/redirect"), new()
                { Mode = NavigationMode.Detached, TargetPolicy = TargetPolicy.ReplaceControlled, ReconnectAfterNavigation = true });
                Assert.That(browser.State, Is.EqualTo(BrowserState.WebDriverAttached));
            }
            else
            {
                await browser.EnterCdpOnlyAsync();
                await browser.NavigateAsync(new Uri(url, "/reference/page?phase=cdp#cdp"), new() { Mode = NavigationMode.CdpOnly });
                Assert.That(browser.State, Is.EqualTo(BrowserState.CdpOnly));
            }
            // Read through independent CDP in every mode; this must not reconnect WebDriver.
            var value = await browser.ExecuteCdpAsync("Runtime.evaluate", new { expression = Observation, returnByValue = true });
            var actual = JsonNode.Parse(value.GetProperty("result").GetProperty("value").GetString()!);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "scenarios.json")));
            var expected = manifest.RootElement.GetProperty("scenarios").EnumerateArray().Single(x => x.GetProperty("id").GetString() == scenarioId).GetProperty("expected");
            report["observed"] = actual;
            Assert.That(JsonNode.DeepEquals(actual, JsonNode.Parse(expected.GetRawText())), Is.True, actual?.ToJsonString());
            Assert.That(chrome.HasExited, Is.False);
            Assert.That(chrome.StartTime, Is.EqualTo(creation));
            Assert.That(browser.TestSnapshot.Profile, Is.EqualTo(before.Profile));
            Assert.That(browser.TestSnapshot.Endpoint, Is.EqualTo(before.Endpoint));
            report["chromePreserved"] = true;
            report["browserVersion"] = JsonNode.Parse((await browser.ExecuteCdpAsync("Browser.getVersion")).GetRawText());
            report["generation"] = browser.SessionGeneration;
            report["passed"] = true;
        }
        catch (Exception e) { failure = e; report["error"] = e.ToString(); throw; }
        finally
        {
            try
            {
                if (browser is not null) await browser.DisposeAsync();
                var clean = chrome is not null && chrome.HasExited;
                report["cleanupPassed"] = clean;
                if (failure is null) Assert.That(clean, Is.True, "Owned Chrome did not exit.");
            }
            catch (Exception e)
            {
                report["cleanupPassed"] = false; report["cleanupError"] = e.ToString(); report["passed"] = false;
                if (failure is null) throw;
            }
            finally
            {
                chrome?.Dispose();
                report["elapsedMs"] = clock.ElapsedMilliseconds;
                var directory = Environment.GetEnvironmentVariable("BROWSERDOCK_REFERENCE_RESULTS") ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "reference-results");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, $"browserdock-{scenarioId}.json");
                await File.WriteAllTextAsync(path, report.ToJsonString(new() { WriteIndented = true }));
                TestContext.AddTestAttachment(path);
            }
        }
    }
}
