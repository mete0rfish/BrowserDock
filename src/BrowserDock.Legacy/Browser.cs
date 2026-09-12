using System.Text.Json;
using Core = global::BrowserDock;

namespace BrowserDock.Legacy;

/// <summary>Task-based facade usable from C# 7.3. Await StopAsync/DisposeAsync in finally.</summary>
public sealed class Browser
{
    private readonly Core.Browser inner;
    internal Browser(Core.Browser inner) => this.inner = inner;
    public BrowserState State => (BrowserState)inner.State;
    public long SessionGeneration => inner.SessionGeneration;
    public long AttachmentEpoch => inner.AttachmentEpoch;
    public BrowserHealthSnapshot Health => new(inner.Health);
    public static Task<Browser> StartAsync(BrowserOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        var snapshot = options.Snapshot();
        return Api.Call(async () => new Browser(await Core.Browser.StartAsync(snapshot, cancellationToken).ConfigureAwait(false)));
    }
    public Task<NavigationResult> NavigateAsync(Uri url, NavigationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshot = options?.Snapshot();
        return Api.Call(async () => new NavigationResult(await inner.NavigateAsync(url, snapshot, cancellationToken).ConfigureAwait(false)));
    }
    public Task DisconnectWebDriverAsync(CancellationToken cancellationToken = default) => Api.Call(() => inner.DisconnectWebDriverAsync(cancellationToken).AsTask());
    public Task EnterCdpOnlyAsync(CancellationToken cancellationToken = default) => Api.Call(() => inner.EnterCdpOnlyAsync(cancellationToken).AsTask());
    public Task<WebDriverLease> ReconnectWebDriverAsync(CancellationToken cancellationToken = default)
        => Api.Call(async () => new WebDriverLease(await inner.ReconnectWebDriverAsync(cancellationToken).ConfigureAwait(false)));
    public Task<WebDriverLease> GetWebDriverAsync(CancellationToken cancellationToken = default)
        => Api.Call(async () => new WebDriverLease(await inner.GetWebDriverAsync(cancellationToken).ConfigureAwait(false)));
    public Task<IReadOnlyList<BrowserTarget>> GetTargetsAsync(CancellationToken cancellationToken = default)
        => Api.Call<IReadOnlyList<BrowserTarget>>(async () => Array.AsReadOnly((await inner.GetTargetsAsync(cancellationToken).ConfigureAwait(false)).Select(x => new BrowserTarget(x)).ToArray()));
    public Task SelectTargetAsync(TargetKey target, CancellationToken cancellationToken = default) => Api.Call(() => inner.SelectTargetAsync(target.ToCore(), cancellationToken).AsTask());
    public Task<ElementRef> FindAsync(Locator locator, FindOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (locator is null) throw new ArgumentNullException(nameof(locator));
        var coreLocator = locator.ToCore(); var snapshot = options?.Snapshot();
        return Api.Call(async () => new ElementRef(await inner.FindAsync(coreLocator, snapshot, cancellationToken).ConfigureAwait(false)));
    }
    public Task<JsonElement> ExecuteCdpAsync(string method, object? parameters = null, TargetKey? target = null, CancellationToken cancellationToken = default)
    {
        // Clone arbitrary caller-owned parameter objects before asynchronous work starts.
        object? snapshot = parameters is null ? null : JsonSerializer.SerializeToElement(parameters);
        return Api.Call(() => inner.ExecuteCdpAsync(method, snapshot, target?.ToCore(), cancellationToken).AsTask());
    }
    public Task StopAsync(ShutdownOptions? options = null, CancellationToken cancellationToken = default) => Api.Call(() => inner.StopAsync(cancellationToken: cancellationToken).AsTask());
    public Task DisposeAsync() => Api.Call(() => inner.DisposeAsync().AsTask());
}
public sealed class WebDriverLease
{
    private readonly Core.WebDriverLease inner;
    public IBrowserCommands Commands { get; }
    public long Generation => inner.Generation;
    public long AttachmentEpoch => inner.AttachmentEpoch;
    internal WebDriverLease(Core.WebDriverLease inner) { this.inner = inner; Commands = new GuardedCommands(inner.Commands); }
    public Task DisposeAsync() => Api.Call(() => inner.DisposeAsync().AsTask());
}
public sealed class ElementRef
{
    private readonly Core.ElementRef inner;
    internal ElementRef(Core.ElementRef inner) => this.inner = inner;
    public Locator Locator => Locator.FromCore(inner.Locator);
    public TargetKey Target => new(inner.Target.Value);
    public long Generation => inner.Generation;
    public long AttachmentEpoch => inner.AttachmentEpoch;
    public IReadOnlyList<Locator> FramePath => Array.AsReadOnly(inner.FramePath.Select(Locator.FromCore).ToArray());
    public Task<ElementRef> ReacquireAsync(CancellationToken cancellationToken = default)
        => Api.Call(async () => new ElementRef(await inner.ReacquireAsync(cancellationToken).ConfigureAwait(false)));
    public Task ClickAsync(CancellationToken cancellationToken = default) => Api.Call(() => inner.ClickAsync(cancellationToken).AsTask());
    public Task ClearAsync(CancellationToken cancellationToken = default) => Api.Call(() => inner.ClearAsync(cancellationToken).AsTask());
    public Task SendKeysAsync(string text, CancellationToken cancellationToken = default) => Api.Call(() => inner.SendKeysAsync(text, cancellationToken).AsTask());
    public Task<string> GetTextAsync(CancellationToken cancellationToken = default) => Api.Call(() => inner.GetTextAsync(cancellationToken).AsTask());
    public Task<string?> GetAttributeAsync(string name, CancellationToken cancellationToken = default) => Api.Call(() => inner.GetAttributeAsync(name, cancellationToken).AsTask());
    public Task<bool> IsDisplayedAsync(CancellationToken cancellationToken = default) => Api.Call(() => inner.IsDisplayedAsync(cancellationToken).AsTask());
}
