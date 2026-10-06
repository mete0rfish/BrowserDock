using System.Text.Json;
using BrowserDock.FrameworkTests;
using NUnit.Framework;
using Legacy = BrowserDock.Legacy;

namespace BrowserDock.Tests;

// Compiled with the common build; execution requires the separate Windows browser gate.
[TestFixture, Category("Windows"), NonParallelizable]
public sealed class UcLaunchProfileBrowserTests
{
    [TestCase(false), TestCase(true)]
    public async Task DocumentScriptsKeepTheirOrderAcrossNavigationReplacementAndRecovery(bool legacy)
    {
        if (!Platform.IsWindows || !Platform.IsX64Process || Platform.WindowsBuild < 22000) Assert.Ignore("Requires the Windows browser fixture.");
        var chrome = Environment.GetEnvironmentVariable("BROWSERDOCK_CHROME");
        var driver = Environment.GetEnvironmentVariable("BROWSERDOCK_DRIVER");
        Assert.That(File.Exists(chrome) && File.Exists(driver), Is.True, "Supply the pinned Chrome/driver fixture.");
        const string profileScript = "globalThis.__profileOrder=['profile'];globalThis.__profileReady=document.readyState;globalThis.__profileRuns=(globalThis.__profileRuns||0)+1;globalThis.__profileCdc=Object.getOwnPropertyNames(globalThis).filter(k=>/^[a-z]{3}_[a-zA-Z0-9]{22}_(Array|Promise|Symbol|Object|Proxy|JSON|Window)$/.test(k))";
        const string callerScript = "globalThis.__profileOrder.push('caller')";
        var options = legacy
            ? new Legacy.BrowserOptions { ChromeBinaryPath = chrome, Driver = new() { ExecutablePath = driver! },
                UcProfile = new() { Language = "en-US", NewDocumentScripts = new[] { profileScript } }, NewDocumentScripts = new[] { profileScript, callerScript } }.Snapshot()
            : new BrowserOptions { ChromeBinaryPath = chrome, Driver = new() { ExecutablePath = driver! },
                UcProfile = new() { Language = "en-US", NewDocumentScripts = new[] { profileScript } }, NewDocumentScripts = new[] { profileScript, callerScript } };
        using var server = await FixtureServer.StartAsync();
        await using var browser = await Browser.StartAsync(options);
        var facade = new Legacy.Browser(browser);
        Assert.That(JsonSerializer.Serialize(facade.UcProfile), Is.EqualTo(JsonSerializer.Serialize(browser.UcProfile)));
        var initial = (await browser.ExecuteCdpAsync("Runtime.evaluate", new { expression = "globalThis.__profileRuns", returnByValue = true })).GetProperty("result");
        TestContext.Out.WriteLine("Initial about:blank observation: " + initial.GetRawText());
        Assert.That(initial.GetProperty("type").GetString() == "undefined" ||
            (initial.GetProperty("type").GetString() == "number" && initial.GetProperty("value").GetInt32() == 1), Is.True,
            "Initial blank may precede registration; if its document is created later, scripts must still run only once.");
        // Register an independent fixture script and seed the current document. The profile must
        // discover the name and remove it before caller document scripts, without removing this ID.
        const string seed = "globalThis.cdc_abcdefghijklmnopqrstuv_Array='fixture'";
        await browser.ExecuteCdpAsync("Page.addScriptToEvaluateOnNewDocument", new { source = seed });
        await browser.ExecuteCdpAsync("Runtime.evaluate", new { expression = seed });
        foreach (var phase in new[] { "standard", "attached-recovery", "detached", "replacement", "recovery", "reconnected-standard" })
        {
            var url = new Uri(server.Url, "/uc-profile?phase=" + phase);
            if (phase is "recovery" or "attached-recovery") browser.InterruptCdpForTest();
            if (phase == "reconnected-standard") await (await browser.Uc.ConnectAsync()).DisposeAsync();
            if (legacy)
            {
                if (phase is "standard" or "attached-recovery" or "reconnected-standard") await facade.Uc.NavigateAsync(url);
                else if (phase == "replacement") await facade.Uc.OpenWithDisconnectAsync(url);
                else await facade.Uc.OpenAsync(url);
            }
            else
            {
                if (phase is "standard" or "attached-recovery" or "reconnected-standard") await browser.Uc.NavigateAsync(url);
                else if (phase == "replacement") await browser.Uc.OpenWithDisconnectAsync(url);
                else await browser.Uc.OpenAsync(url);
            }
            var observation = await browser.ExecuteCdpAsync("Runtime.evaluate", new
            { expression = "({order:globalThis.__profileOrder,runs:globalThis.__profileRuns,ready:globalThis.__profileReady,pageCdc:globalThis.__pageCdc,profileCdc:globalThis.__profileCdc,cdc:'cdc_abcdefghijklmnopqrstuv_Array' in globalThis})", returnByValue = true });
            var value = observation.GetProperty("result").GetProperty("value");
            Assert.Multiple(() =>
            {
                Assert.That(value.GetProperty("order").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "profile", "caller" }), phase);
                Assert.That(value.GetProperty("runs").GetInt32(), Is.EqualTo(1), phase);
                Assert.That(value.GetProperty("ready").GetString(), Is.EqualTo("loading"), phase);
                Assert.That(value.GetProperty("cdc").GetBoolean(), Is.False, phase);
                Assert.That(value.GetProperty("pageCdc").GetArrayLength(), Is.Zero, phase + ": first inline page script");
                Assert.That(value.GetProperty("profileCdc").GetArrayLength(), Is.Zero, phase + ": profile script");
            });
        }
        await (await browser.Uc.ReconnectAsync()).DisposeAsync();
        Assert.That((await browser.ExecuteCdpAsync("Runtime.evaluate", new { expression = "globalThis.__profileRuns", returnByValue = true })).GetProperty("result").GetProperty("value").GetInt32(), Is.EqualTo(1), "Reattachment must not replay caller scripts in the current document.");
    }
}
