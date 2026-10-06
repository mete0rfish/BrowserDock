using NUnit.Framework;
using Legacy = BrowserDock.Legacy;

namespace BrowserDock.Tests;

[TestFixture, Category("Windows"), NonParallelizable]
public sealed class ElementClickBrowserTests
{
    [TestCase(false), TestCase(true)]
    public async Task ClickWaitsForReadinessAndUsesNativeHitTestingInExplicitFrames(bool legacy)
    {
        if (!Platform.IsWindows || !Platform.IsX64Process || Platform.WindowsBuild < 22000) Assert.Ignore("Requires the Windows browser fixture.");
        var chrome = Environment.GetEnvironmentVariable("BROWSERDOCK_CHROME");
        var driver = Environment.GetEnvironmentVariable("BROWSERDOCK_DRIVER");
        Assert.That(File.Exists(chrome) && File.Exists(driver), Is.True, "Supply the pinned Chrome/driver fixture.");
        await using var browser = await Browser.StartAsync(new() { ChromeBinaryPath = chrome, Driver = new() { ExecutablePath = driver! } });
        await VerifyAsync(browser, legacy);
    }

    // The same DOM assertions can run through the Linux investigation harness;
    // they do not establish Windows hosting or SeleniumBase parity.
    internal static async Task VerifyAsync(Browser browser, bool legacy)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        await using var lease = await browser.GetWebDriverAsync(token);
        var commands = lease.Commands;
        var facade = new Legacy.Browser(browser);
        Task Click(IReadOnlyList<Locator>? frames = null) => legacy
            ? facade.ClickAsync(Legacy.Locator.Id("subject"), new Legacy.ElementWaitOptions
            { FramePath = (frames ?? Array.Empty<Locator>()).Select(Legacy.Locator.FromCore).ToArray() }, token)
            : browser.ClickAsync(Locator.Id("subject"), new ElementWaitOptions { FramePath = frames ?? Array.Empty<Locator>() }, token).AsTask();
        await commands.ExecuteScriptAsync("document.body.innerHTML = ''; window.clicks = 0;", cancellationToken: token);
        var pending = Click();
        await commands.ExecuteScriptAsync("""
            document.body.innerHTML = '<button id=subject disabled style="display:none" onclick="window.clicks++">Click</button>';
            """, cancellationToken: token);
        Assert.That(pending.IsCompleted, Is.False);
        await commands.ExecuteScriptAsync("document.getElementById('subject').style.display='block';", cancellationToken: token);
        Assert.That(pending.IsCompleted, Is.False);
        await commands.ExecuteScriptAsync("document.getElementById('subject').disabled=false;", cancellationToken: token);
        await pending;
        Assert.That((await commands.ExecuteScriptAsync("return window.clicks", cancellationToken: token)).GetInt32(), Is.EqualTo(1));

        await commands.ExecuteScriptAsync("""
            window.clicks = 0;
            document.body.insertAdjacentHTML('beforeend', '<div id=overlay style="position:fixed;inset:0;z-index:99999;background:white"></div>');
            """, cancellationToken: token);
        if (legacy)
            Assert.That(Assert.ThrowsAsync<Legacy.BrowserDockException>(async () => await Click())!.Category, Is.EqualTo(Legacy.ErrorCategory.ElementInteractionFailure));
        else
            Assert.That(Assert.ThrowsAsync<BrowserDockException>(async () => await Click())!.Category, Is.EqualTo(ErrorCategory.ElementInteractionFailure));
        Assert.That((await commands.ExecuteScriptAsync("return window.clicks", cancellationToken: token)).GetInt32(), Is.Zero, "No JavaScript/native-input fallback through an overlay.");

        await commands.ExecuteScriptAsync("""
            window.topClicks = 0; window.nestedClicks = 0;
            document.body.innerHTML = '<button id=subject onclick="window.topClicks++">Top</button><iframe id=outer></iframe>';
            const outer = document.getElementById('outer').contentDocument;
            outer.body.innerHTML = '<iframe id=inner></iframe>';
            outer.getElementById('inner').contentDocument.body.innerHTML = '<button id=subject onclick="top.nestedClicks++">Nested</button>';
            """, cancellationToken: token);
        await Click(new[] { Locator.Id("outer"), Locator.Id("inner") });
        await Click();
        Assert.That((await commands.ExecuteScriptAsync("return [window.topClicks,window.nestedClicks]", cancellationToken: token)).EnumerateArray().Select(x => x.GetInt32()), Is.EqualTo(new[] { 1, 1 }));
    }
}
