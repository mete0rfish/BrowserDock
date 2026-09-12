using Microsoft.Extensions.Logging;
using Core = global::BrowserDock;

namespace BrowserDock.Legacy;

/// <summary>Mutable caller options. Each operation takes a defensive snapshot.</summary>
public sealed class BrowserOptions
{
    public string? ChromeBinaryPath { get; set; }
    public DriverArtifactOptions Driver { get; set; } = new();
    public ProfileOptions Profile { get; set; } = new();
    public BrowserOwnership BrowserOwnership { get; set; } = BrowserOwnership.Library;
    public DriverPatchMode PatchMode { get; set; }
    public BrowserTimeouts Timeouts { get; set; } = new();
    public IList<string> ChromeArguments { get; set; } = new List<string>();
    public IList<string> AdditionalUrlSchemes { get; set; } = new List<string>();
    public IList<string> NewDocumentScripts { get; set; } = new List<string>();
    public bool RemoveDiscoveredCdcProperties { get; set; }
    public ILogger? Logger { get; set; }
    internal Core.BrowserOptions Snapshot() => new()
    {
        ChromeBinaryPath = ChromeBinaryPath,
        Driver = (Driver ?? throw new ArgumentNullException(nameof(Driver))).Snapshot(),
        Profile = new() { Directory = (Profile ?? throw new ArgumentNullException(nameof(Profile))).Directory },
        BrowserOwnership = (Core.BrowserOwnership)BrowserOwnership, PatchMode = (Core.DriverPatchMode)PatchMode,
        Timeouts = (Timeouts ?? throw new ArgumentNullException(nameof(Timeouts))).Snapshot(),
        ChromeArguments = ChromeArguments.ToArray(), AdditionalUrlSchemes = AdditionalUrlSchemes.ToArray(), NewDocumentScripts = NewDocumentScripts.ToArray(),
        RemoveDiscoveredCdcProperties = RemoveDiscoveredCdcProperties, Logger = Logger
    };
}
public sealed class ProfileOptions { public string? Directory { get; set; } }
public sealed class DriverArtifactOptions
{
    public string ExecutablePath { get; set; } = "";
    public string? CacheDirectory { get; set; }
    public IList<DriverPatchRecipe> PatchRecipes { get; set; } = new List<DriverPatchRecipe>();
    internal Core.DriverArtifactOptions Snapshot() => new()
    {
        ExecutablePath = ExecutablePath, CacheDirectory = CacheDirectory,
        PatchStrategies = PatchRecipes.Select(recipe => recipe.Snapshot()).ToArray()
    };
}
public sealed class DriverPatchRecipe
{
    public string RecipeId { get; set; } = "";
    public Version MinimumVersion { get; set; } = new(0, 0);
    public Version MaximumVersion { get; set; } = new(0, 0);
    public IList<PatchPattern> Patterns { get; set; } = new List<PatchPattern>();
    internal Core.Patching.IDriverPatchStrategy Snapshot() => new RecipeSnapshot(RecipeId, MinimumVersion, MaximumVersion,
        Patterns.Select(p => new Core.Patching.PatchPattern(p.Id, p.Search.ToArray(), p.Replacement.ToArray(), p.ExpectedCount)).ToArray());
    private sealed class RecipeSnapshot(string id, Version minimum, Version maximum, IReadOnlyList<Core.Patching.PatchPattern> patterns) : Core.Patching.IDriverPatchStrategy
    {
        public string RecipeId => id;
        public IReadOnlyList<Core.Patching.PatchPattern> Patterns => patterns;
        public bool Supports(Version driverVersion) => driverVersion.CompareTo(minimum) >= 0 && driverVersion.CompareTo(maximum) <= 0;
    }
}
public sealed class PatchPattern
{
    public string Id { get; set; } = "";
    public byte[] Search { get; set; } = Array.Empty<byte>();
    public byte[] Replacement { get; set; } = Array.Empty<byte>();
    public int ExpectedCount { get; set; }
}
public sealed class NavigationOptions
{
    public NavigationMode Mode { get; set; }
    public bool ReconnectAfterNavigation { get; set; }
    public NavigationWaitUntil WaitUntil { get; set; } = NavigationWaitUntil.Load;
    public TargetKey? Target { get; set; }
    public TargetPolicy TargetPolicy { get; set; }
    public TimeSpan? ReconnectDelay { get; set; }
    internal Core.NavigationOptions Snapshot() => new()
    {
        Mode = (Core.NavigationMode)Mode, ReconnectAfterNavigation = ReconnectAfterNavigation,
        WaitUntil = (Core.NavigationWaitUntil)WaitUntil, Target = Target?.ToCore(), TargetPolicy = (Core.TargetPolicy)TargetPolicy, ReconnectDelay = ReconnectDelay
    };
}
public sealed class FindOptions
{
    public TargetKey? Target { get; set; }
    public IList<Locator> FramePath { get; set; } = new List<Locator>();
    public bool ReacquireOnSessionChange { get; set; }
    internal Core.FindOptions Snapshot() => new()
    {
        Target = Target?.ToCore(), FramePath = FramePath.Select(x => x.ToCore()).ToArray(), ReacquireOnSessionChange = ReacquireOnSessionChange
    };
}
public sealed class ShutdownOptions { }
public sealed class BrowserCookie
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Domain { get; set; }
    public string Path { get; set; } = "/";
    public DateTime? Expiry { get; set; }
    public bool Secure { get; set; }
    public bool HttpOnly { get; set; }
    public string? SameSite { get; set; }
    internal Core.BrowserCookie Snapshot() => new(Name, Value, Domain, Path, Expiry, Secure, HttpOnly, SameSite);
    internal static BrowserCookie FromCore(Core.BrowserCookie cookie) => new()
    {
        Name = cookie.Name, Value = cookie.Value, Domain = cookie.Domain, Path = cookie.Path,
        Expiry = cookie.Expiry, Secure = cookie.Secure, HttpOnly = cookie.HttpOnly, SameSite = cookie.SameSite
    };
}
