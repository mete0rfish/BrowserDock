namespace BrowserDock;

/// <summary>Explicit UC navigation policy. Does not enable launch scripts or driver patching.</summary>
public sealed record UcNavigationOptions
{
    public NavigationWaitUntil WaitUntil { get; init; } = NavigationWaitUntil.Load;
    public TargetKey? Target { get; init; }
}

/// <summary>
/// Non-owning, opt-in lifecycle/navigation facade. Dispose the owning Browser for final cleanup.
/// Durations are minimum time without a driver, not operation timeouts or post-load delays.
/// </summary>
public sealed class UcBrowser
{
    private readonly Browser browser;
    internal UcBrowser(Browser browser) => this.browser = browser;
    /// <summary>Idempotent for a healthy disconnected browser; retains Chrome and its pages.</summary>
    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) => browser.DisconnectWebDriverAsync(cancellationToken);
    /// <summary>Create a session from CdpOnly. An already attached browser is rejected.</summary>
    public ValueTask<WebDriverLease> ConnectAsync(CancellationToken cancellationToken = default) => browser.ReconnectWebDriverAsync(cancellationToken);
    /// <summary>Disconnect if attached, hold the minimum duration, then create a fresh session. Uses the Reconnect operation budget.</summary>
    public ValueTask<WebDriverLease> ReconnectAsync(TimeSpan minimumDisconnectedDuration = default, CancellationToken cancellationToken = default)
        => browser.UcReconnectAsync(minimumDisconnectedDuration, cancellationToken);
    /// <summary>Ordinary current-page WebDriver navigation. No HTTP preflight, heuristic switching or detached hold.</summary>
    public ValueTask<NavigationResult> NavigateAsync(Uri url, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
        => browser.NavigateAsync(url, new() { WaitUntil = options?.WaitUntil ?? NavigationWaitUntil.Load, Target = options?.Target }, cancellationToken);
    /// <summary>Navigate the current page detached; reconnect only if initially attached.</summary>
    public ValueTask<NavigationResult> OpenAsync(Uri url, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
        => browser.UcOpenAsync(UcOperation.Open, url, options ?? new(), TimeSpan.Zero, cancellationToken);
    /// <summary>Replace only the controlled page; reconnect only if initially attached.</summary>
    public ValueTask<NavigationResult> OpenWithTabAsync(Uri url, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
        => browser.UcOpenAsync(UcOperation.OpenWithTab, url, options ?? new(), TimeSpan.Zero, cancellationToken);
    /// <summary>Replace/navigate, wait for the milestone and minimum disconnected duration, then attach even if initially disconnected.</summary>
    public ValueTask<NavigationResult> OpenWithReconnectAsync(Uri url, TimeSpan minimumDisconnectedDuration = default, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
        => browser.UcOpenAsync(UcOperation.OpenWithReconnect, url, options ?? new(), minimumDisconnectedDuration, cancellationToken);
    /// <summary>Replace/navigate and wait for the milestone and minimum disconnected duration; return disconnected without creating a driver.</summary>
    public ValueTask<NavigationResult> OpenWithDisconnectAsync(Uri url, TimeSpan minimumDisconnectedDuration = default, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
        => browser.UcOpenAsync(UcOperation.OpenWithDisconnect, url, options ?? new(), minimumDisconnectedDuration, cancellationToken);
}
