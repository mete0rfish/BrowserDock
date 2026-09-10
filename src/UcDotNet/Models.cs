using System.Text.Json;
using Microsoft.Extensions.Logging;
using UcDotNet.Patching;

namespace UcDotNet;

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
            if (value <= TimeSpan.Zero || value > TimeSpan.FromDays(1)) throw new UcException(ErrorCategory.ConfigurationError, "Timeouts must be positive and at most one day.");
    }
}
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
    public bool RemoveDiscoveredCdcProperties { get; init; }
    public ILogger? Logger { get; init; }
    internal Func<string, CancellationToken, ValueTask>? StageHook { get; init; }
}
public sealed record NavigationOptions
{
    public NavigationMode Mode { get; init; }
    public bool ReconnectAfterNavigation { get; init; }
    public NavigationWaitUntil WaitUntil { get; init; } = NavigationWaitUntil.Load;
    public TargetKey? Target { get; init; }
    public TargetPolicy TargetPolicy { get; init; }
    public TimeSpan? ReconnectDelay { get; init; }
}
public sealed record NavigationResult(Uri FinalUrl, IReadOnlyList<string> RedirectChain, NavigationOutcome Outcome, long SessionGeneration, long AttachmentEpoch, bool BrowserMayHaveAdvanced = false);
public sealed record ShutdownOptions;
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
public interface IUcWebDriver
{
    ValueTask<string> GetUrlAsync(CancellationToken cancellationToken = default);
    ValueTask<string> GetTitleAsync(CancellationToken cancellationToken = default);
    ValueTask NavigateAsync(Uri url, CancellationToken cancellationToken = default);
    ValueTask<ElementRef> FindAsync(Locator locator, FindOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask<JsonElement> ExecuteScriptAsync(string script, IReadOnlyList<object?>? arguments = null, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<BrowserCookie>> GetCookiesAsync(CancellationToken cancellationToken = default);
    ValueTask AddCookieAsync(BrowserCookie cookie, CancellationToken cancellationToken = default);
    ValueTask DeleteCookieAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<string>> GetWindowHandlesAsync(CancellationToken cancellationToken = default);
    ValueTask SwitchToWindowAsync(string handle, CancellationToken cancellationToken = default);
    ValueTask SwitchToFrameAsync(Locator? frame, CancellationToken cancellationToken = default);
    ValueTask ResizeWindowAsync(int width, int height, CancellationToken cancellationToken = default);
}
