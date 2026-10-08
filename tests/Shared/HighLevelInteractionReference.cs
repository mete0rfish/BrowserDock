using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy = BrowserDock.Legacy;

namespace BrowserDock.TestFixtures;

// Shared by the Windows reference test and the explicit Linux investigation.
// This observes selected high-level contracts, not UC/hosting compatibility.
internal static class HighLevelInteractionReference
{
    internal const string Scenario = "SB-05";
    internal static JsonObject Report()
    {
        var report = new JsonObject { ["scenarioId"] = Scenario, ["implementation"] = "BrowserDock", ["passed"] = false };
        var metadata = Environment.GetEnvironmentVariable("BROWSERDOCK_REFERENCE_METADATA");
        if (metadata is null) return report; // Ordinary Windows contract execution has no upstream evidence envelope.
        var fixture = JsonNode.Parse(File.ReadAllText(metadata))!;
        foreach (var key in new[] { "browserdockCommit", "seleniumBaseVersion", "seleniumBaseCommit", "chromeSha256", "vendorDriverSha256", "framework", "project", "executionEnvironment", "fixtureId", "chromeVersion", "os", "architecture" })
            report[key] = fixture[key]!.DeepClone();
        string Hash(string variable)
        {
            using var hash = System.Security.Cryptography.SHA256.Create();
            using var binary = File.OpenRead(Environment.GetEnvironmentVariable(variable)!);
            return BitConverter.ToString(hash.ComputeHash(binary)).Replace("-", "").ToLowerInvariant();
        }
        report["driverSha256"] = Hash("BROWSERDOCK_DRIVER");
        report["vendorDriverSha256"] = report["driverSha256"]!.DeepClone();
        report["chromeSha256"] = Hash("BROWSERDOCK_CHROME");
        return report;
    }
    internal static async Task<JsonObject> ObserveAsync(Browser browser, bool legacy)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        await using var lease = await browser.GetWebDriverAsync(token);
        var core = lease.Commands; var facade = new Legacy.Browser(browser); var commands = new Legacy.GuardedCommands(core);
        var epoch = browser.AttachmentEpoch; var generation = browser.SessionGeneration;
        Task<JsonElement> Script(string source) => legacy ? commands.ExecuteScriptAsync(source, cancellationToken: token)
            : core.ExecuteScriptAsync(source, cancellationToken: token).AsTask();
        async Task<string> Text(bool exact, string selector, string text)
        {
            if (legacy)
            {
                var element = await (exact ? facade.WaitForExactTextAsync(Legacy.Locator.Id(selector), text, cancellationToken: token)
                    : facade.WaitForTextAsync(Legacy.Locator.Id(selector), text, cancellationToken: token));
                return selector == "input" ? (await element.GetValueAsync(token))! : await element.GetTextAsync(token);
            }
            var value = await (exact ? browser.WaitForExactTextAsync(Locator.Id(selector), text, cancellationToken: token)
                : browser.WaitForTextAsync(Locator.Id(selector), text, cancellationToken: token));
            return selector == "input" ? (await value.GetValueAsync(token))! : await value.GetTextAsync(token);
        }
        await Script("window.armStatus()"); var contains = await Text(false, "status", "ready");
        await Script("window.armExact()"); var exact = await Text(true, "status", " ready ");
        var initial = await Text(true, "input", "initial");
        await Script("window.armButton()");
        if (legacy) { await facade.ClickAsync(Legacy.Locator.Id("button"), cancellationToken: token); await facade.TypeAsync(Legacy.Locator.Id("input"), "reference", cancellationToken: token); await facade.SendKeysAsync(Legacy.Locator.Id("input"), " + appended", cancellationToken: token); }
        else { await browser.ClickAsync(Locator.Id("button"), cancellationToken: token); await browser.TypeAsync(Locator.Id("input"), "reference", cancellationToken: token); await browser.SendKeysAsync(Locator.Id("input"), " + appended", cancellationToken: token); }
        var input = await Text(true, "input", "reference + appended");
        string frame;
        if (legacy)
        {
            var options = new Legacy.ElementWaitOptions { FramePath = new[] { Legacy.Locator.Id("frame") } };
            await facade.TypeAsync(Legacy.Locator.Id("frame-input"), "frame-value", options, token);
            frame = (await (await facade.WaitForExactTextAsync(Legacy.Locator.Id("frame-input"), "frame-value", options, token)).GetValueAsync(token))!;
        }
        else
        {
            var options = new ElementWaitOptions { FramePath = new[] { Locator.Id("frame") } };
            await browser.TypeAsync(Locator.Id("frame-input"), "frame-value", options, token);
            frame = (await (await browser.WaitForExactTextAsync(Locator.Id("frame-input"), "frame-value", options, token)).GetValueAsync(token))!;
        }
        // Every subsequent operation must restore the top-level document.
        var top = await Text(true, "status", "ready");
        await Script("window.armHide()");
        if (legacy) await facade.WaitForHiddenAsync(Legacy.Locator.Id("marker"), cancellationToken: token);
        else await browser.WaitForHiddenAsync(Locator.Id("marker"), cancellationToken: token);
        var hidden = await Script("return !!document.getElementById('marker') && getComputedStyle(document.getElementById('marker')).display==='none'");
        await Script("window.armRemove()");
        if (legacy) await facade.WaitForAbsentAsync(Legacy.Locator.Id("marker"), cancellationToken: token);
        else await browser.WaitForAbsentAsync(Locator.Id("marker"), cancellationToken: token);
        var state = await Script("return {clicks:window.clicks,inputAttribute:document.getElementById('input').getAttribute('value'),absent:!document.getElementById('marker')}");
        var title = await (legacy ? commands.GetTitleAsync(token) : core.GetTitleAsync(token).AsTask());
        if (browser.State != BrowserState.WebDriverAttached || browser.AttachmentEpoch != epoch || browser.SessionGeneration != generation)
            throw new InvalidOperationException("High-level interactions replaced the original attachment.");
        return new JsonObject { ["containsText"] = contains, ["exactText"] = exact, ["initialValue"] = initial,
            ["input"] = input, ["frame"] = frame, ["topLevelText"] = top, ["hiddenPresent"] = hidden.GetBoolean(),
            ["clicks"] = state.GetProperty("clicks").GetInt32(), ["inputAttribute"] = state.GetProperty("inputAttribute").GetString(),
            ["absent"] = state.GetProperty("absent").GetBoolean(), ["title"] = title, ["originalLeaseUsable"] = true };
    }
    internal static void Check(JsonObject observed)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("BROWSERDOCK_REFERENCE_MANIFEST")
            ?? Path.Combine(AppContext.BaseDirectory, "scenarios.json")))!;
        var expected = manifest["scenarios"]!.AsArray().Single(x => (string?)x!["id"] == Scenario)!["expected"];
        if (!JsonNode.DeepEquals(observed, expected)) throw new InvalidOperationException("High-level observation differs from the manifest: " + observed.ToJsonString());
    }
    internal static async Task CheckBrowserVersionAsync(Browser browser, JsonObject report)
    {
        var actual = (await browser.ExecuteCdpAsync("Browser.getVersion")).GetProperty("product").GetString()!.Split('/').Last();
        report["browserVersion"] = actual;
        if (report["chromeVersion"] is not null && actual != (string?)report["chromeVersion"])
            throw new InvalidOperationException("Executed Chrome differs from fixture metadata.");
    }
    internal static Task WriteAsync(JsonObject report)
    {
        var output = Environment.GetEnvironmentVariable("BROWSERDOCK_REFERENCE_RESULTS");
        if (output is not null) File.WriteAllText(Path.Combine(output, "browserdock-" + Scenario + ".json"), report.ToJsonString(new() { WriteIndented = true }));
        return Task.CompletedTask;
    }
}
