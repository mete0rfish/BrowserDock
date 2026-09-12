using System.Text.Json;
using Core = global::BrowserDock;

namespace BrowserDock.Legacy;

public interface IBrowserCommands
{
    Task<string> GetUrlAsync(CancellationToken cancellationToken = default);
    Task<string> GetTitleAsync(CancellationToken cancellationToken = default);
    Task NavigateAsync(Uri url, CancellationToken cancellationToken = default);
    Task<ElementRef> FindAsync(Locator locator, FindOptions? options = null, CancellationToken cancellationToken = default);
    Task<JsonElement> ExecuteScriptAsync(string script, IReadOnlyList<object?>? arguments = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BrowserCookie>> GetCookiesAsync(CancellationToken cancellationToken = default);
    Task AddCookieAsync(BrowserCookie cookie, CancellationToken cancellationToken = default);
    Task DeleteCookieAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetWindowHandlesAsync(CancellationToken cancellationToken = default);
    Task SwitchToWindowAsync(string handle, CancellationToken cancellationToken = default);
    Task SwitchToFrameAsync(Locator? frame, CancellationToken cancellationToken = default);
    Task ResizeWindowAsync(int width, int height, CancellationToken cancellationToken = default);
}
internal sealed class GuardedCommands(Core.IBrowserCommands inner) : IBrowserCommands
{
    public Task<string> GetUrlAsync(CancellationToken cancellationToken = default) => Api.Call(() => inner.GetUrlAsync(cancellationToken).AsTask());
    public Task<string> GetTitleAsync(CancellationToken cancellationToken = default) => Api.Call(() => inner.GetTitleAsync(cancellationToken).AsTask());
    public Task NavigateAsync(Uri url, CancellationToken cancellationToken = default) => Api.Call(() => inner.NavigateAsync(url, cancellationToken).AsTask());
    public Task<ElementRef> FindAsync(Locator locator, FindOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (locator is null) throw new ArgumentNullException(nameof(locator));
        var coreLocator = locator.ToCore(); var snapshot = options?.Snapshot();
        return Api.Call(async () => new ElementRef(await inner.FindAsync(coreLocator, snapshot, cancellationToken).ConfigureAwait(false)));
    }
    public Task<JsonElement> ExecuteScriptAsync(string script, IReadOnlyList<object?>? arguments = null, CancellationToken cancellationToken = default)
    {
        var snapshot = arguments?.Select(x => SnapshotValue(x)).ToArray();
        return Api.Call(() => inner.ExecuteScriptAsync(script, snapshot, cancellationToken).AsTask());
    }
    internal static object? SnapshotValue(object? value, int depth = 0)
    {
        if (depth > 64) throw new ArgumentException("Script arguments exceed the nesting limit.");
        if (value is null or string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal) return value;
        if (value is System.Collections.IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key) throw new ArgumentException("Script object keys must be strings.");
                result[key] = SnapshotValue(entry.Value, depth + 1);
            }
            return result;
        }
        if (value is System.Collections.IEnumerable list) return list.Cast<object?>().Select(x => SnapshotValue(x, depth + 1)).ToArray();
        throw new ArgumentException("Script arguments must be JSON-compatible scalars, arrays or dictionaries.");
    }
    public Task<IReadOnlyList<BrowserCookie>> GetCookiesAsync(CancellationToken cancellationToken = default)
        => Api.Call<IReadOnlyList<BrowserCookie>>(async () => Array.AsReadOnly((await inner.GetCookiesAsync(cancellationToken).ConfigureAwait(false)).Select(BrowserCookie.FromCore).ToArray()));
    public Task AddCookieAsync(BrowserCookie cookie, CancellationToken cancellationToken = default)
    {
        if (cookie is null) throw new ArgumentNullException(nameof(cookie));
        var snapshot = cookie.Snapshot();
        return Api.Call(() => inner.AddCookieAsync(snapshot, cancellationToken).AsTask());
    }
    public Task DeleteCookieAsync(string name, CancellationToken cancellationToken = default) => Api.Call(() => inner.DeleteCookieAsync(name, cancellationToken).AsTask());
    public Task<IReadOnlyList<string>> GetWindowHandlesAsync(CancellationToken cancellationToken = default)
        => Api.Call<IReadOnlyList<string>>(async () => Array.AsReadOnly((await inner.GetWindowHandlesAsync(cancellationToken).ConfigureAwait(false)).ToArray()));
    public Task SwitchToWindowAsync(string handle, CancellationToken cancellationToken = default) => Api.Call(() => inner.SwitchToWindowAsync(handle, cancellationToken).AsTask());
    public Task SwitchToFrameAsync(Locator? frame, CancellationToken cancellationToken = default)
    {
        var snapshot = frame?.ToCore();
        return Api.Call(() => inner.SwitchToFrameAsync(snapshot, cancellationToken).AsTask());
    }
    public Task ResizeWindowAsync(int width, int height, CancellationToken cancellationToken = default) => Api.Call(() => inner.ResizeWindowAsync(width, height, cancellationToken).AsTask());
}
