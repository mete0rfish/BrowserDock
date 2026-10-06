using NUnit.Framework;
using Legacy = BrowserDock.Legacy;

namespace BrowserDock.Tests;

[TestFixture, Category("Windows"), NonParallelizable]
public sealed class ElementWaitBrowserTests
{
    [TestCase(false), TestCase(true)]
    public async Task WaitsAndValuesFollowLiveDomInExplicitFrames(bool legacy)
    {
        if (!Platform.IsWindows || !Platform.IsX64Process || Platform.WindowsBuild < 22000) Assert.Ignore("Requires the Windows browser fixture.");
        var chrome = Environment.GetEnvironmentVariable("BROWSERDOCK_CHROME");
        var driver = Environment.GetEnvironmentVariable("BROWSERDOCK_DRIVER");
        Assert.That(File.Exists(chrome) && File.Exists(driver), Is.True, "Supply the pinned Chrome/driver fixture.");
        await using var browser = await Browser.StartAsync(new() { ChromeBinaryPath = chrome, Driver = new() { ExecutablePath = driver! } });
        await VerifyAsync(browser, legacy);
    }

    // Also callable by a local Linux investigation harness that supplies its own
    // Chrome/driver processes; that is not Windows hosting acceptance evidence.
    internal static async Task VerifyAsync(Browser browser, bool legacy)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        await using var lease = await browser.GetWebDriverAsync(token);
        var commands = lease.Commands;
        var facade = new Legacy.Browser(browser);
        await commands.ExecuteScriptAsync("document.body.innerHTML = '';", cancellationToken: token);
        async Task<object> Wait(bool displayed)
        {
            if (legacy) return await (displayed
                ? facade.WaitForVisibleAsync(Legacy.Locator.Id("subject"), cancellationToken: token)
                : facade.WaitForExistsAsync(Legacy.Locator.Id("subject"), cancellationToken: token));
            return await (displayed
                ? browser.WaitForVisibleAsync(Locator.Id("subject"), cancellationToken: token)
                : browser.WaitForExistsAsync(Locator.Id("subject"), cancellationToken: token));
        }
        var insertion = Wait(false);
        await commands.ExecuteScriptAsync("document.body.innerHTML = '<input id=subject value=original style=display:none>';", cancellationToken: token);
        var existing = await insertion;
        if (legacy) Assert.That(await ((Legacy.ElementRef)existing).IsDisplayedAsync(token), Is.False);
        else Assert.That(await ((ElementRef)existing).IsDisplayedAsync(token), Is.False);

        var visible = Wait(true);
        await commands.ExecuteScriptAsync("document.getElementById('subject').style.display='block'; document.getElementById('subject').value='edited';", cancellationToken: token);
        var element = await visible;
        if (legacy)
        {
            var input = (Legacy.ElementRef)element;
            Assert.That(await input.IsDisplayedAsync(token), Is.True);
            Assert.That(await input.GetAttributeAsync("value", token), Is.EqualTo("original"));
            Assert.That(await input.GetDomAttributeAsync("value", token), Is.EqualTo("original"));
            Assert.That(await input.GetDomPropertyAsync("value", token), Is.EqualTo("edited"));
            Assert.That(await input.GetValueAsync(token), Is.EqualTo("edited"));
        }
        else
        {
            var input = (ElementRef)element;
            Assert.That(await input.IsDisplayedAsync(token), Is.True);
            Assert.That(await input.GetAttributeAsync("value", token), Is.EqualTo("original"));
            Assert.That(await input.GetDomAttributeAsync("value", token), Is.EqualTo("original"));
            Assert.That(await input.GetDomPropertyAsync("value", token), Is.EqualTo("edited"));
            Assert.That(await input.GetValueAsync(token), Is.EqualTo("edited"));
        }
        var absent = legacy ? facade.WaitForAbsentAsync(Legacy.Locator.Id("subject"), cancellationToken: token)
            : browser.WaitForAbsentAsync(Locator.Id("subject"), cancellationToken: token).AsTask();
        await commands.ExecuteScriptAsync("document.getElementById('subject').remove();", cancellationToken: token);
        await absent;
        if (legacy)
            Assert.That(Assert.ThrowsAsync<Legacy.BrowserDockException>(async () => await ((Legacy.ElementRef)element).GetValueAsync(token))!.Category, Is.EqualTo(Legacy.ErrorCategory.StaleDomElement));
        else
            Assert.That(Assert.ThrowsAsync<BrowserDockException>(async () => await ((ElementRef)element).GetValueAsync(token))!.Category, Is.EqualTo(ErrorCategory.StaleDomElement));

        await commands.ExecuteScriptAsync("""
            document.body.innerHTML = '<input id=subject value=top><iframe id=outer></iframe>';
            const outer = document.getElementById('outer').contentDocument;
            outer.body.innerHTML = '<iframe id=inner></iframe>';
            outer.getElementById('inner').contentDocument.body.innerHTML = '<input id=subject value=nested>';
            """, cancellationToken: token);
        if (legacy)
        {
            var nested = await facade.WaitForVisibleAsync(Legacy.Locator.Id("subject"), new Legacy.ElementWaitOptions
            { FramePath = new[] { Legacy.Locator.Id("outer"), Legacy.Locator.Id("inner") } }, token);
            Assert.That(await nested.GetValueAsync(token), Is.EqualTo("nested"));
            var top = await facade.WaitForVisibleAsync(Legacy.Locator.Id("subject"), cancellationToken: token);
            Assert.That(await top.GetValueAsync(token), Is.EqualTo("top"));
        }
        else
        {
            var nested = await browser.WaitForVisibleAsync(Locator.Id("subject"), new ElementWaitOptions
            { FramePath = new[] { Locator.Id("outer"), Locator.Id("inner") } }, token);
            Assert.That(await nested.GetValueAsync(token), Is.EqualTo("nested"));
            var top = await browser.WaitForVisibleAsync(Locator.Id("subject"), cancellationToken: token);
            Assert.That(await top.GetValueAsync(token), Is.EqualTo("top"));
        }
    }
}
