using System.Text.RegularExpressions;

namespace BrowserDock;

/// <summary>Opt-in, versioned launch/script policy. This is not a binary patch or a promise of upstream parity.</summary>
public sealed record UcLaunchProfile
{
    public int Version { get; init; } = 1;
    public string Language { get; init; } = "en-US";
    public bool RemoveDiscoveredCdcProperties { get; init; } = true;
    public bool RequireBinaryPatch { get; init; }
    /// <summary>Runs after profile CDC cleanup, before BrowserOptions.NewDocumentScripts, on future documents.</summary>
    public IReadOnlyList<string> NewDocumentScripts { get; init; } = [];
}

/// <summary>Redacted effective policy; contains no caller arguments, profile paths or script bodies.</summary>
public sealed record UcLaunchProfileInfo(string Id, string Language, bool RemoveDiscoveredCdcProperties,
    bool RequireBinaryPatch, DriverPatchMode PatchMode, int DocumentScriptCount);

internal static class UcProfileResolver
{
    internal const string Id = "browserdock-uc-v1";
    internal static UcLaunchProfile? Capture(UcLaunchProfile? profile)
    {
        if (profile is null) return null;
        if (profile.NewDocumentScripts is null) throw Invalid("UC document scripts cannot be null.");
        return profile with { NewDocumentScripts = Array.AsReadOnly(profile.NewDocumentScripts.ToArray()) };
    }
    internal static BrowserOptions Resolve(BrowserOptions captured)
    {
        var profile = captured.UcProfile;
        if (profile is null) return captured;
        if (profile.Version != 1) throw Invalid("Unsupported UC launch profile version.");
        if (profile.Language is null || !Regex.IsMatch(profile.Language, "\\A[a-zA-Z]{2,8}(?:-[a-zA-Z0-9]{1,8})*\\z"))
            throw Invalid("UC language must be a single language tag, such as en-US.");
        if (captured.CdcPropertyRemovalOverride is { } removal && removal != profile.RemoveDiscoveredCdcProperties)
            throw Invalid("UC profile conflicts with explicit CDC-property removal.");
        if (profile.RequireBinaryPatch && captured.PatchMode != DriverPatchMode.BinaryCompatibility)
            throw Invalid("UC profile requires BinaryCompatibility patching; it cannot fall back to an unpatched driver.");
        if (profile.RequireBinaryPatch && captured.Driver.PatchStrategies.Count == 0)
            throw Invalid("UC profile requires an explicit binary patch strategy (#31).");
        var arguments = new List<string>();
        foreach (var argument in captured.ChromeArguments)
        {
            if (argument is null) throw Invalid("Chrome arguments cannot contain null.");
            var separator = argument.IndexOf('=');
            var name = separator < 0 ? argument : argument.Substring(0, separator);
            // Reject whitespace spellings rather than letting Chrome reinterpret a managed option.
            if (name.Any(char.IsWhiteSpace)) throw Invalid("UC arguments must use --name=value syntax.");
            if (name.Equals("--enable-automation", StringComparison.OrdinalIgnoreCase))
                throw Invalid("UC profile conflicts with --enable-automation.");
            if (name.Equals("--lang", StringComparison.OrdinalIgnoreCase))
            {
                if (separator < 0 || !argument.Substring(separator + 1).Equals(profile.Language, StringComparison.OrdinalIgnoreCase))
                    throw Invalid("UC profile conflicts with explicit --lang.");
                continue;
            }
            arguments.Add(argument);
        }
        arguments.Add("--lang=" + profile.Language);
        var scripts = profile.NewDocumentScripts.Concat(captured.NewDocumentScripts).ToArray();
        if (scripts.Any(string.IsNullOrWhiteSpace)) throw Invalid("UC document scripts must not be null or blank.");
        return captured with
        {
            ChromeArguments = Array.AsReadOnly(arguments.ToArray()),
            NewDocumentScripts = Array.AsReadOnly(scripts.Distinct(StringComparer.Ordinal).ToArray()),
            RemoveDiscoveredCdcProperties = profile.RemoveDiscoveredCdcProperties
        };
    }
    internal static UcLaunchProfileInfo? Describe(BrowserOptions captured) => captured.UcProfile is { } profile
        ? new(Id, profile.Language, captured.RemoveDiscoveredCdcProperties, profile.RequireBinaryPatch,
            captured.PatchMode, captured.NewDocumentScripts.Distinct(StringComparer.Ordinal).Count())
        : null;
    private static BrowserDockException Invalid(string message) => new(ErrorCategory.ConfigurationError, message);
}
