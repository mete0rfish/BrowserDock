using Core = global::BrowserDock;

namespace BrowserDock.Legacy;

public sealed class UcNavigationOptions
{
    public NavigationWaitUntil WaitUntil { get; set; } = NavigationWaitUntil.Load;
    public TargetKey? Target { get; set; }
    internal Core.UcNavigationOptions Snapshot() => new() { WaitUntil = (Core.NavigationWaitUntil)WaitUntil, Target = Target?.ToCore() };
}

/// <summary>Task-based UC helpers. The owning Browser remains responsible for final disposal.</summary>
public sealed class UcBrowser
{
    private readonly Core.UcBrowser inner;
    internal UcBrowser(Core.UcBrowser inner) => this.inner = inner;
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Api.Call(() => inner.DisconnectAsync(cancellationToken).AsTask());
    public Task<WebDriverLease> ConnectAsync(CancellationToken cancellationToken = default)
        => Api.Call(async () => new WebDriverLease(await inner.ConnectAsync(cancellationToken).ConfigureAwait(false)));
    public Task<WebDriverLease> ReconnectAsync(TimeSpan minimumDisconnectedDuration = default, CancellationToken cancellationToken = default)
        => Api.Call(async () => new WebDriverLease(await inner.ReconnectAsync(minimumDisconnectedDuration, cancellationToken).ConfigureAwait(false)));
    public Task<NavigationResult> NavigateAsync(Uri url, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshot = options?.Snapshot();
        return Api.Call(async () => new NavigationResult(await inner.NavigateAsync(url, snapshot, cancellationToken).ConfigureAwait(false)));
    }
    public Task<NavigationResult> OpenAsync(Uri url, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshot = options?.Snapshot();
        return Api.Call(async () => new NavigationResult(await inner.OpenAsync(url, snapshot, cancellationToken).ConfigureAwait(false)));
    }
    public Task<NavigationResult> OpenWithTabAsync(Uri url, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshot = options?.Snapshot();
        return Api.Call(async () => new NavigationResult(await inner.OpenWithTabAsync(url, snapshot, cancellationToken).ConfigureAwait(false)));
    }
    public Task<NavigationResult> OpenWithReconnectAsync(Uri url, TimeSpan minimumDisconnectedDuration = default, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshot = options?.Snapshot();
        return Api.Call(async () => new NavigationResult(await inner.OpenWithReconnectAsync(url, minimumDisconnectedDuration, snapshot, cancellationToken).ConfigureAwait(false)));
    }
    public Task<NavigationResult> OpenWithDisconnectAsync(Uri url, TimeSpan minimumDisconnectedDuration = default, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshot = options?.Snapshot();
        return Api.Call(async () => new NavigationResult(await inner.OpenWithDisconnectAsync(url, minimumDisconnectedDuration, snapshot, cancellationToken).ConfigureAwait(false)));
    }
}
