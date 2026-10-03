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
    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        => PreserveCancellation(browser.DisconnectWebDriverAsync(cancellationToken), cancellationToken);
    /// <summary>Create a session from CdpOnly. An already attached browser is rejected.</summary>
    public ValueTask<WebDriverLease> ConnectAsync(CancellationToken cancellationToken = default)
        => PreserveCancellation(browser.ReconnectWebDriverAsync(cancellationToken), cancellationToken);
    /// <summary>Disconnect if attached, hold the minimum duration, then create a fresh session. Uses the Reconnect operation budget.</summary>
    public ValueTask<WebDriverLease> ReconnectAsync(TimeSpan minimumDisconnectedDuration = default, CancellationToken cancellationToken = default)
        => PreserveCancellation(browser.UcReconnectAsync(minimumDisconnectedDuration, cancellationToken), cancellationToken);
    /// <summary>Ordinary current-page WebDriver navigation. No HTTP preflight, heuristic switching or detached hold.</summary>
    public ValueTask<NavigationResult> NavigateAsync(Uri url, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
        => PreserveCancellation(browser.NavigateAsync(url, new() { WaitUntil = options?.WaitUntil ?? NavigationWaitUntil.Load, Target = options?.Target }, cancellationToken), cancellationToken);
    /// <summary>Navigate the current page detached; reconnect only if initially attached.</summary>
    public ValueTask<NavigationResult> OpenAsync(Uri url, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
        => PreserveCancellation(browser.UcOpenAsync(UcOperation.Open, url, options ?? new(), TimeSpan.Zero, cancellationToken), cancellationToken);
    /// <summary>Replace only the controlled page; reconnect only if initially attached.</summary>
    public ValueTask<NavigationResult> OpenWithTabAsync(Uri url, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
        => PreserveCancellation(browser.UcOpenAsync(UcOperation.OpenWithTab, url, options ?? new(), TimeSpan.Zero, cancellationToken), cancellationToken);
    /// <summary>Replace/navigate, wait for the milestone and minimum disconnected duration, then attach even if initially disconnected.</summary>
    public ValueTask<NavigationResult> OpenWithReconnectAsync(Uri url, TimeSpan minimumDisconnectedDuration = default, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
        => PreserveCancellation(browser.UcOpenAsync(UcOperation.OpenWithReconnect, url, options ?? new(), minimumDisconnectedDuration, cancellationToken), cancellationToken);
    /// <summary>Replace/navigate and wait for the milestone and minimum disconnected duration; return disconnected without creating a driver.</summary>
    public ValueTask<NavigationResult> OpenWithDisconnectAsync(Uri url, TimeSpan minimumDisconnectedDuration = default, UcNavigationOptions? options = null, CancellationToken cancellationToken = default)
        => PreserveCancellation(browser.UcOpenAsync(UcOperation.OpenWithDisconnect, url, options ?? new(), minimumDisconnectedDuration, cancellationToken), cancellationToken);

    // Gate admission precedes the workflow's cancellation mapping. Normalize at
    // the facade boundary so queued and already-canceled calls retain the caller
    // token too, including the simple Core aliases. Timeout/lifetime failures and
    // already-normalized navigation exceptions keep their existing semantics.
    private static async ValueTask<T> PreserveCancellation<T>(ValueTask<T> operation, CancellationToken caller)
    {
        try { return await operation.ConfigureAwait(false); }
        catch (OperationCanceledException error) when (caller.IsCancellationRequested && error.CancellationToken != caller)
        { throw WithCallerToken(error, caller); }
    }
    private static async ValueTask PreserveCancellation(ValueTask operation, CancellationToken caller)
    {
        try { await operation.ConfigureAwait(false); }
        catch (OperationCanceledException error) when (caller.IsCancellationRequested && error.CancellationToken != caller)
        { throw WithCallerToken(error, caller); }
    }
    private static OperationCanceledException WithCallerToken(OperationCanceledException original, CancellationToken caller)
    {
        OperationCanceledException error = original is NavigationCanceledException navigation
            ? new NavigationCanceledException(caller, navigation.BrowserMayHaveAdvanced, original)
            : new OperationCanceledException("UC operation was canceled by its caller.", original, caller);
        foreach (System.Collections.DictionaryEntry entry in original.Data) error.Data[entry.Key] = entry.Value;
        return error;
    }
}
