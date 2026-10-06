using NUnit.Framework;
using Legacy = BrowserDock.Legacy;

namespace BrowserDock.Tests;

[TestFixture, Category("Windows"), NonParallelizable]
public sealed class ElementInputBrowserTests
{
    [TestCase(false), TestCase(true)]
    public async Task ReplaceAppendAndSubmitFollowLiveDom(bool legacy)
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
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45)); var token = deadline.Token;
        await using var lease = await browser.GetWebDriverAsync(token); var commands = lease.Commands;
        var facade = new Legacy.Browser(browser);
        Task Input(string text, bool clear = true, IReadOnlyList<Locator>? frames = null)
        {
            if (legacy)
            {
                var options = new Legacy.ElementWaitOptions { FramePath = (frames ?? Array.Empty<Locator>()).Select(Legacy.Locator.FromCore).ToArray() };
                return clear ? facade.TypeAsync(Legacy.Locator.Id("subject"), text, options, token) : facade.SendKeysAsync(Legacy.Locator.Id("subject"), text, options, token);
            }
            var core = new ElementWaitOptions { FramePath = frames ?? Array.Empty<Locator>() };
            return (clear ? browser.TypeAsync(Locator.Id("subject"), text, core, token) : browser.SendKeysAsync(Locator.Id("subject"), text, core, token)).AsTask();
        }
        async Task<string?> Value() => await (await browser.FindAsync(Locator.Id("subject"), cancellationToken: token)).GetValueAsync(token);
        await commands.ExecuteScriptAsync("""
            window.submits = 0; window.inputs = 0; window.keydowns = 0;
            document.body.innerHTML = '<form onsubmit="window.submits++;event.preventDefault()"><input id=subject value=original readonly disabled style="display:none" oninput="window.inputs++" onkeydown="window.keydowns++"></form>';
            """, cancellationToken: token);
        var pending = Input("한글 e\u0301 😀");
        await commands.ExecuteScriptAsync("document.getElementById('subject').style.display='block';document.getElementById('subject').disabled=false;", cancellationToken: token);
        Assert.That(pending.IsCompleted, Is.False);
        await commands.ExecuteScriptAsync("document.getElementById('subject').readOnly=false;", cancellationToken: token);
        await pending; Assert.That(await Value(), Is.EqualTo("한글 e\u0301 😀"));
        await commands.ExecuteScriptAsync("document.getElementById('subject').setSelectionRange(0,0);", cancellationToken: token);
        await Input("!", false); Assert.That(await Value(), Is.EqualTo("한글 e\u0301 😀!"));
        Assert.That((await commands.ExecuteScriptAsync("return window.inputs>0 && window.keydowns>0", cancellationToken: token)).GetBoolean(), Is.True);
        await Input(""); Assert.That(await Value(), Is.EqualTo(""));
        await Input("replace\r\n"); Assert.That(await Value(), Is.EqualTo("replace"));
        await Input("+append\n", false); Assert.That(await Value(), Is.EqualTo("replace+append"));
        Assert.That((await commands.ExecuteScriptAsync("return window.submits", cancellationToken: token)).GetInt32(), Is.EqualTo(2));

        await commands.ExecuteScriptAsync("document.body.innerHTML='<textarea id=subject>old</textarea>';", cancellationToken: token);
        await Input("line1\nline2"); Assert.That(await Value(), Is.EqualTo("line1\nline2"));
        await commands.ExecuteScriptAsync("document.getElementById('subject').setSelectionRange(0,3);", cancellationToken: token);
        await Input("!", false); Assert.That(await Value(), Is.EqualTo("line1\nline2!"));
        await commands.ExecuteScriptAsync("document.body.innerHTML='<div id=subject contenteditable=true>old</div>';", cancellationToken: token);
        await Input("editable");
        Assert.That((await commands.ExecuteScriptAsync("return document.getElementById('subject').textContent", cancellationToken: token)).GetString(), Is.EqualTo("editable"));
        await commands.ExecuteScriptAsync("getSelection().collapse(document.getElementById('subject').firstChild,0);", cancellationToken: token);
        await Input("!", false);
        Assert.That((await commands.ExecuteScriptAsync("return document.getElementById('subject').textContent", cancellationToken: token)).GetString(), Is.EqualTo("editable!"));

        await commands.ExecuteScriptAsync("""
            document.body.innerHTML = '<input id=subject value=top><iframe id=outer></iframe>';
            const outer = document.getElementById('outer').contentDocument;
            outer.body.innerHTML = '<iframe id=inner></iframe>';
            outer.getElementById('inner').contentDocument.body.innerHTML = '<input id=subject value=old>';
            """, cancellationToken: token);
        await Input("nested", frames: new[] { Locator.Id("outer"), Locator.Id("inner") });
        await Input("!", false); Assert.That(await Value(), Is.EqualTo("top!"));
        var nested = await browser.FindAsync(Locator.Id("subject"), new FindOptions { FramePath = new[] { Locator.Id("outer"), Locator.Id("inner") } }, token);
        Assert.That(await nested.GetValueAsync(token), Is.EqualTo("nested"));
        await commands.SwitchToFrameAsync(null, token);
        await commands.ExecuteScriptAsync("""
            document.body.innerHTML = '<input id=subject value=old>';
            document.getElementById('subject').onchange = function() { this.outerHTML = '<input id=subject value=fresh>'; };
            """, cancellationToken: token);
        if (legacy)
            Assert.That(Assert.ThrowsAsync<Legacy.BrowserDockException>(async () => await Input("must-not-replay"))!.Category, Is.EqualTo(Legacy.ErrorCategory.StaleDomElement));
        else
            Assert.That(Assert.ThrowsAsync<BrowserDockException>(async () => await Input("must-not-replay"))!.Category, Is.EqualTo(ErrorCategory.StaleDomElement));
        Assert.That(await Value(), Is.EqualTo("fresh"));
    }
}
