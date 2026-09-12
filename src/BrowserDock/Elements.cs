using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using BrowserDock.WebDriver;

namespace BrowserDock;

public sealed class WebDriverLease : IAsyncDisposable
{
    private int disposed;
    internal bool Disposed => Volatile.Read(ref disposed) != 0;
    public IBrowserCommands Commands { get; }
    public long Generation { get; }
    public long AttachmentEpoch { get; }
    internal WebDriverLease(Browser browser, long generation, long epoch)
    { Generation = generation; AttachmentEpoch = epoch; Commands = new GuardedCommands(browser, this); }
    public ValueTask DisposeAsync() { Interlocked.Exchange(ref disposed, 1); return default(ValueTask); }
}
internal sealed class GuardedCommands(Browser browser, WebDriverLease lease) : IBrowserCommands
{
    private ValueTask<T> Run<T>(Func<RemoteWebDriver, T> action, CancellationToken token) => browser.CommandAsync(lease.AttachmentEpoch, () => !lease.Disposed, action, token);
    private async ValueTask Run(Action<RemoteWebDriver> action, CancellationToken token) => await Run(d => { action(d); return true; }, token).ConfigureAwait(false);
    public ValueTask<string> GetUrlAsync(CancellationToken cancellationToken = default) => Run(d => d.Url, cancellationToken);
    public ValueTask<string> GetTitleAsync(CancellationToken cancellationToken = default) => Run(d => d.Title, cancellationToken);
    public async ValueTask NavigateAsync(Uri url, CancellationToken cancellationToken = default)
    {
        if (lease.Disposed || lease.AttachmentEpoch != browser.AttachmentEpoch) throw new StaleAttachmentException();
        await browser.NavigateFromLeaseAsync(url, lease.AttachmentEpoch, () => !lease.Disposed, cancellationToken).ConfigureAwait(false);
    }
    public ValueTask<ElementRef> FindAsync(Locator locator, FindOptions? options = null, CancellationToken cancellationToken = default)
        => browser.FindForLeaseAsync(locator, options ?? new(), lease.AttachmentEpoch, () => !lease.Disposed, cancellationToken);
    public ValueTask<JsonElement> ExecuteScriptAsync(string script, IReadOnlyList<object?>? arguments = null, CancellationToken cancellationToken = default)
        => Run(d => JsonSerializer.SerializeToElement(ScriptValues.Normalize(d.ExecuteScript(script, (arguments ?? []).Select(x => ScriptValues.Normalize(x)).ToArray()!))), cancellationToken);
    public ValueTask<IReadOnlyList<BrowserCookie>> GetCookiesAsync(CancellationToken cancellationToken = default)
        => Run<IReadOnlyList<BrowserCookie>>(d => d.Manage().Cookies.AllCookies.Select(c => new BrowserCookie(c.Name, c.Value, c.Domain, c.Path ?? "/", c.Expiry, c.Secure, c.IsHttpOnly, c.SameSite)).ToArray(), cancellationToken);
    public ValueTask AddCookieAsync(BrowserCookie cookie, CancellationToken cancellationToken = default)
        => Run(d => d.Manage().Cookies.AddCookie(new Cookie(cookie.Name, cookie.Value, cookie.Domain, cookie.Path, cookie.Expiry, cookie.Secure, cookie.HttpOnly, cookie.SameSite)), cancellationToken);
    public ValueTask DeleteCookieAsync(string name, CancellationToken cancellationToken = default) => Run(d => d.Manage().Cookies.DeleteCookieNamed(name), cancellationToken);
    public ValueTask<IReadOnlyList<string>> GetWindowHandlesAsync(CancellationToken cancellationToken = default) => Run<IReadOnlyList<string>>(d => d.WindowHandles.ToArray(), cancellationToken);
    public ValueTask SwitchToWindowAsync(string handle, CancellationToken cancellationToken = default) => browser.SwitchKnownWindowAsync(handle, lease.AttachmentEpoch, () => !lease.Disposed, cancellationToken);
    public ValueTask SwitchToFrameAsync(Locator? frame, CancellationToken cancellationToken = default) => Run(d => { if (frame is null) d.SwitchTo().DefaultContent(); else d.SwitchTo().Frame(d.FindElement(Attachment.By(frame))); }, cancellationToken);
    public ValueTask ResizeWindowAsync(int width, int height, CancellationToken cancellationToken = default) => Run(d => { d.Manage().Window.Size = new System.Drawing.Size(width, height); }, cancellationToken);
}
internal static class ScriptValues
{
    internal static object? Normalize(object? value, int depth = 0)
    {
        if (depth > 64) throw new BrowserDockException(ErrorCategory.ProtocolError, "Script value exceeds the nesting limit.");
        if (value is null or string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal) return value;
        if (value is IWebElement or IWebDriver) throw new BrowserDockException(ErrorCategory.ConfigurationError, "Script values cannot contain Selenium objects. Use locator-based ElementRef operations.");
        if (value is System.Collections.IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key) throw new BrowserDockException(ErrorCategory.ConfigurationError, "Script object keys must be strings.");
                result[key] = Normalize(entry.Value, depth + 1);
            }
            return result;
        }
        if (value is System.Collections.IEnumerable array) return array.Cast<object?>().Select(x => Normalize(x, depth + 1)).ToArray();
        throw new BrowserDockException(ErrorCategory.ConfigurationError, "Script values must be JSON-compatible scalars, arrays or string-keyed dictionaries.");
    }
}
public sealed class ElementRef
{
    private readonly Browser browser;
    private readonly string elementId;
    private readonly FindOptions options;
    public Locator Locator { get; }
    public TargetKey Target { get; }
    public long Generation { get; }
    public long AttachmentEpoch { get; }
    public IReadOnlyList<Locator> FramePath => Array.AsReadOnly(options.FramePath.ToArray());
    internal ElementRef(Browser browser, string id, Locator locator, FindOptions options, TargetKey target, long generation, long epoch)
    { this.browser = browser; elementId = id; Locator = locator; this.options = options with { FramePath = options.FramePath.ToArray(), Target = target }; Target = target; Generation = generation; AttachmentEpoch = epoch; }
    private async ValueTask<T> Run<T>(Func<IWebElement, T> action, CancellationToken token)
    {
        if (AttachmentEpoch != browser.AttachmentEpoch && options.ReacquireOnSessionChange)
        { var fresh = await ReacquireAsync(token).ConfigureAwait(false); return await fresh.Run(action, token).ConfigureAwait(false); }
        return await browser.CommandAsync(AttachmentEpoch, () => true, driver =>
        {
            browser.RestoreElementContext(driver, Target, options.FramePath);
            return action(new WebElement(driver, elementId));
        }, token).ConfigureAwait(false);
    }
    public ValueTask<ElementRef> ReacquireAsync(CancellationToken cancellationToken = default) => browser.FindAsync(Locator, options, cancellationToken);
    public async ValueTask ClickAsync(CancellationToken cancellationToken = default) => await Run(e => { e.Click(); return true; }, cancellationToken).ConfigureAwait(false);
    public async ValueTask ClearAsync(CancellationToken cancellationToken = default) => await Run(e => { e.Clear(); return true; }, cancellationToken).ConfigureAwait(false);
    public async ValueTask SendKeysAsync(string text, CancellationToken cancellationToken = default) => await Run(e => { e.SendKeys(text); return true; }, cancellationToken).ConfigureAwait(false);
    public ValueTask<string> GetTextAsync(CancellationToken cancellationToken = default) => Run(e => e.Text, cancellationToken);
    public ValueTask<string?> GetAttributeAsync(string name, CancellationToken cancellationToken = default) => Run<string?>(e => e.GetDomAttribute(name), cancellationToken);
    public ValueTask<bool> IsDisplayedAsync(CancellationToken cancellationToken = default) => Run(e => e.Displayed, cancellationToken);
}
