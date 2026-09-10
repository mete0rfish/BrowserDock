using Core = global::UcDotNet;

namespace UcDotNet.Legacy;

public struct TargetKey : IEquatable<TargetKey>
{
    public string Value { get; }
    public TargetKey(string value) { Value = value ?? throw new ArgumentNullException(nameof(value)); }
    internal Core.TargetKey ToCore() => new(Value);
    public bool Equals(TargetKey other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is TargetKey other && Equals(other);
    public override int GetHashCode() => Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
    public static bool operator ==(TargetKey left, TargetKey right) => left.Equals(right);
    public static bool operator !=(TargetKey left, TargetKey right) => !left.Equals(right);
    public override string ToString() => Value ?? "";
}
public sealed class Locator
{
    public LocatorKind Kind { get; }
    public string Value { get; }
    public Locator(LocatorKind kind, string value) { Kind = kind; Value = value ?? throw new ArgumentNullException(nameof(value)); }
    public static Locator Css(string value) => new(LocatorKind.Css, value);
    public static Locator XPath(string value) => new(LocatorKind.XPath, value);
    public static Locator Id(string value) => new(LocatorKind.Id, value);
    internal Core.Locator ToCore() => new((Core.LocatorKind)Kind, Value);
    internal static Locator FromCore(Core.Locator locator) => new((LocatorKind)locator.Kind, locator.Value);
}
public sealed class BrowserTarget
{
    public TargetKey Key { get; }
    public string Url { get; }
    public string Title { get; }
    public string? Opener { get; }
    public bool Controlled { get; }
    internal BrowserTarget(Core.BrowserTarget source) { Key = new(source.Key.Value); Url = source.Url; Title = source.Title; Opener = source.Opener; Controlled = source.Controlled; }
}
public sealed class NavigationResult
{
    public Uri FinalUrl { get; }
    public IReadOnlyList<string> RedirectChain { get; }
    public NavigationOutcome Outcome { get; }
    public long SessionGeneration { get; }
    public long AttachmentEpoch { get; }
    public bool BrowserMayHaveAdvanced { get; }
    internal NavigationResult(Core.NavigationResult source)
    {
        FinalUrl = source.FinalUrl; RedirectChain = Array.AsReadOnly(source.RedirectChain.ToArray()); Outcome = (NavigationOutcome)source.Outcome;
        SessionGeneration = source.SessionGeneration; AttachmentEpoch = source.AttachmentEpoch; BrowserMayHaveAdvanced = source.BrowserMayHaveAdvanced;
    }
}
public sealed class BrowserHealthSnapshot
{
    public BrowserState State { get; }
    public long SessionGeneration { get; }
    public long AttachmentEpoch { get; }
    public int? ChromePid { get; }
    public int? DriverPid { get; }
    public string Chrome { get; }
    public string Cdp { get; }
    public string Attachment { get; }
    public DateTimeOffset? LastProbe { get; }
    public IReadOnlyList<string> CleanupFailures { get; }
    internal BrowserHealthSnapshot(Core.BrowserHealthSnapshot source)
    {
        State = (BrowserState)source.State; SessionGeneration = source.SessionGeneration; AttachmentEpoch = source.AttachmentEpoch;
        ChromePid = source.ChromePid; DriverPid = source.DriverPid; Chrome = source.Chrome; Cdp = source.Cdp; Attachment = source.Attachment;
        LastProbe = source.LastProbe; CleanupFailures = Array.AsReadOnly(source.CleanupFailures.ToArray());
    }
}
