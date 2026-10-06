using Core = global::BrowserDock;

namespace BrowserDock.Legacy;

public sealed class UcLaunchProfile
{
    public int Version { get; set; } = 1;
    public string Language { get; set; } = "en-US";
    public bool RemoveDiscoveredCdcProperties { get; set; } = true;
    public bool RequireBinaryPatch { get; set; }
    public IList<string> NewDocumentScripts { get; set; } = new List<string>();
    internal Core.UcLaunchProfile Snapshot() => new()
    {
        Version = Version, Language = Language, RemoveDiscoveredCdcProperties = RemoveDiscoveredCdcProperties,
        RequireBinaryPatch = RequireBinaryPatch, NewDocumentScripts = NewDocumentScripts is null ? null! : NewDocumentScripts.ToArray()
    };
}

public sealed class UcLaunchProfileInfo
{
    public string Id { get; }
    public string Language { get; }
    public bool RemoveDiscoveredCdcProperties { get; }
    public bool RequireBinaryPatch { get; }
    public DriverPatchMode PatchMode { get; }
    public int DocumentScriptCount { get; }
    internal UcLaunchProfileInfo(Core.UcLaunchProfileInfo value)
    {
        Id = value.Id; Language = value.Language; RemoveDiscoveredCdcProperties = value.RemoveDiscoveredCdcProperties;
        RequireBinaryPatch = value.RequireBinaryPatch; PatchMode = (DriverPatchMode)value.PatchMode;
        DocumentScriptCount = value.DocumentScriptCount;
    }
}
