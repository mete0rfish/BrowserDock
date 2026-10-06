using System.Text.Json;
using Microsoft.Extensions.Logging;
using BrowserDock.Patching;

namespace BrowserDock;

public enum BrowserState { Stopped, StartingChrome, ChromeReady, AttachingWebDriver, WebDriverAttached, Disconnecting, CdpOnly, Reattaching, Faulted, Disposing }
public enum NavigationMode { Standard, Detached, CdpOnly }
public enum NavigationWaitUntil { Commit, DOMContentLoaded, Load, NetworkIdle }
public enum TargetPolicy { CurrentControlled, ReplaceControlled }
public enum DriverPatchMode { Disabled, ValidateOnly, BinaryCompatibility }
public enum BrowserOwnership { Library }
public enum NavigationOutcome { Completed, Download }
public readonly record struct TargetKey(string Value);
public sealed record BrowserTarget(TargetKey Key, string Url, string Title, string? Opener, bool Controlled);
public sealed record ProfileOptions { public string? Directory { get; init; } }
public sealed record DriverArtifactOptions
{
    public required string ExecutablePath { get; init; }
    public string? CacheDirectory { get; init; }
    /// <summary>The collection is copied at startup. Strategy objects remain caller-owned; see IDriverPatchStrategy.</summary>
    public IReadOnlyList<IDriverPatchStrategy> PatchStrategies { get; init; } = [];
}
public sealed record BrowserTimeouts
{
    public TimeSpan ChromeStart { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan DevToolsEndpointDiscovery { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan CdpConnect { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan DriverProcessStart { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan WebDriverSessionCreate { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan Navigation { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan Command { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan DisconnectDrain { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan Reconnect { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan GracefulShutdown { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ForceKillWait { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ProfileCleanup { get; init; } = TimeSpan.FromSeconds(5);
    internal void Validate()
    {
        foreach (var value in new[] { ChromeStart, DevToolsEndpointDiscovery, CdpConnect, DriverProcessStart, WebDriverSessionCreate, Navigation, Command, DisconnectDrain, Reconnect, GracefulShutdown, ForceKillWait, ProfileCleanup })
            if (value <= TimeSpan.Zero || value > TimeSpan.FromDays(1)) throw new BrowserDockException(ErrorCategory.ConfigurationError, "Timeouts must be positive and at most one day.");
    }
}
/// <summary>
/// Startup copies and validates the option collections before its first asynchronous wait.
/// Do not modify the inputs during capture. Later list changes do not affect this browser.
/// Custom patch strategies and the logger are shared objects, not deep copies.
/// </summary>
public sealed record BrowserOptions
{
    public string? ChromeBinaryPath { get; init; }
    public required DriverArtifactOptions Driver { get; init; }
    public ProfileOptions Profile { get; init; } = new();
    public BrowserOwnership BrowserOwnership { get; init; } = BrowserOwnership.Library;
    public DriverPatchMode PatchMode { get; init; }
    public BrowserTimeouts Timeouts { get; init; } = new();
    public IReadOnlyList<string> ChromeArguments { get; init; } = [];
    public IReadOnlyList<string> AdditionalUrlSchemes { get; init; } = [];
    public IReadOnlyList<string> NewDocumentScripts { get; init; } = [];
    private bool? cdcPropertyRemoval;
    public bool RemoveDiscoveredCdcProperties { get => cdcPropertyRemoval ?? false; init => cdcPropertyRemoval = value; }
    internal bool? CdcPropertyRemovalOverride => cdcPropertyRemoval;
    /// <summary>Null preserves ordinary startup. Calling Browser.Uc never enables this profile implicitly.</summary>
    public UcLaunchProfile? UcProfile { get; init; }
    public ILogger? Logger { get; init; }
    internal Func<string, CancellationToken, ValueTask>? StageHook { get; init; }
}
public sealed record NavigationOptions
{
    public NavigationMode Mode { get; init; }
    /// <summary>Creates a new attachment after Detached navigation. Other modes reject true.</summary>
    public bool ReconnectAfterNavigation { get; init; }
    public NavigationWaitUntil WaitUntil { get; init; } = NavigationWaitUntil.Load;
    public TargetKey? Target { get; init; }
    public TargetPolicy TargetPolicy { get; init; }
    /// <summary>
    /// Post-navigation delay, only for Detached with reconnection. Null/zero adds no hold.
    /// Must be nonnegative and less than BrowserTimeouts.Navigation; consumes that budget,
    /// independently of the subsequent BrowserTimeouts.Reconnect attachment timeout.
    /// </summary>
    public TimeSpan? ReconnectDelay { get; init; }
}
public sealed record NavigationResult(Uri FinalUrl, IReadOnlyList<string> RedirectChain, NavigationOutcome Outcome, long SessionGeneration, long AttachmentEpoch, bool BrowserMayHaveAdvanced = false);
public sealed record ShutdownOptions;
// .NET Framework requires serializable values in Exception.Data. Command
// cancellation carries this immutable snapshot through that existing channel.
[Serializable]
public sealed record BrowserHealthSnapshot(BrowserState State, long SessionGeneration, long AttachmentEpoch,
    int? ChromePid, int? DriverPid, string Chrome, string Cdp, string Attachment, DateTimeOffset? LastProbe,
    IReadOnlyList<string> CleanupFailures);
public enum LocatorKind { Css, XPath, Id, Name, TagName, LinkText }
public sealed record Locator(LocatorKind Kind, string Value)
{
    public static Locator Css(string value) => new(LocatorKind.Css, value);
    public static Locator XPath(string value) => new(LocatorKind.XPath, value);
    public static Locator Id(string value) => new(LocatorKind.Id, value);
}
public sealed record FindOptions
{
    public TargetKey? Target { get; init; }
    public IReadOnlyList<Locator> FramePath { get; init; } = [];
    public bool ReacquireOnSessionChange { get; init; }
}
public sealed record BrowserCookie(string Name, string Value, string? Domain = null, string Path = "/", DateTime? Expiry = null, bool Secure = false, bool HttpOnly = false, string? SameSite = null);
public interface IBrowserCommands
{
    ValueTask<string> GetUrlAsync(CancellationToken cancellationToken = default);
    ValueTask<string> GetTitleAsync(CancellationToken cancellationToken = default);
    ValueTask NavigateAsync(Uri url, CancellationToken cancellationToken = default);
    ValueTask<ElementRef> FindAsync(Locator locator, FindOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask<ElementRef> WaitForExistsAsync(Locator locator, ElementWaitOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask<ElementRef> WaitForVisibleAsync(Locator locator, ElementWaitOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask WaitForAbsentAsync(Locator locator, ElementWaitOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask ClickAsync(Locator locator, ElementWaitOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask TypeAsync(Locator locator, string text, ElementWaitOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask SendKeysAsync(Locator locator, string text, ElementWaitOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask<JsonElement> ExecuteScriptAsync(string script, IReadOnlyList<object?>? arguments = null, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<BrowserCookie>> GetCookiesAsync(CancellationToken cancellationToken = default);
    ValueTask AddCookieAsync(BrowserCookie cookie, CancellationToken cancellationToken = default);
    ValueTask DeleteCookieAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<string>> GetWindowHandlesAsync(CancellationToken cancellationToken = default);
    ValueTask SwitchToWindowAsync(string handle, CancellationToken cancellationToken = default);
    ValueTask SwitchToFrameAsync(Locator? frame, CancellationToken cancellationToken = default);
    ValueTask ResizeWindowAsync(int width, int height, CancellationToken cancellationToken = default);
}
