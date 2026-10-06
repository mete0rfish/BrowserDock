namespace BrowserDock;

/// <summary>One overall deadline, including command queueing, context restoration and polling.</summary>
public sealed record ElementWaitOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public TargetKey? Target { get; init; }
    public IReadOnlyList<Locator> FramePath { get; init; } = [];

    internal ElementWaitOptions Capture()
    {
        // Framework timers use a signed 32-bit millisecond interval. Reject
        // sub-millisecond intervals too, rather than silently spinning at zero.
        if (Timeout.TotalMilliseconds < 1 || Timeout.TotalMilliseconds > int.MaxValue ||
            PollInterval.TotalMilliseconds < 1 || PollInterval.TotalMilliseconds > int.MaxValue)
            throw new BrowserDockException(ErrorCategory.ConfigurationError, "Element wait durations must be between 1 ms and Int32.MaxValue ms.");
        RuntimeCompatibility.NotNull(FramePath, nameof(FramePath));
        var frames = FramePath.ToArray();
        foreach (var frame in frames) ValidateLocator(frame);
        return this with { FramePath = Array.AsReadOnly(frames) };
    }

    internal static void ValidateLocator(Locator locator)
    {
        RuntimeCompatibility.NotNull(locator, nameof(locator));
        if (!Enum.IsDefined(typeof(LocatorKind), locator.Kind) || string.IsNullOrWhiteSpace(locator.Value))
            throw new BrowserDockException(ErrorCategory.ConfigurationError, "An element wait requires a supported locator with a nonempty value.");
    }
}
