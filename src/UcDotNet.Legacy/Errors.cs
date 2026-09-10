using Core = global::UcDotNet;

namespace UcDotNet.Legacy;

public class UcException : Exception
{
    public ErrorCategory Category { get; }
    public Guid OperationId { get; }
    public BrowserHealthSnapshot? Diagnostic { get; }
    public bool Retryable { get; }
    public bool BrowserMayHaveAdvanced { get; }
    public IReadOnlyList<string> CleanupFailures { get; }
    internal UcException(Core.UcException error) : base(error.Message, error)
    {
        Category = (ErrorCategory)error.Category; OperationId = error.OperationId; Retryable = error.Retryable;
        Diagnostic = error.Diagnostic is null ? null : new(error.Diagnostic); BrowserMayHaveAdvanced = error.BrowserMayHaveAdvanced;
        CleanupFailures = Array.AsReadOnly(error.CleanupFailures.ToArray());
    }
}
public sealed class StaleAttachmentException : UcException { internal StaleAttachmentException(Core.UcException error) : base(error) { } }
public sealed class AmbiguousTargetException : UcException { internal AmbiguousTargetException(Core.UcException error) : base(error) { } }
public sealed class BrowserExitedException : UcException { internal BrowserExitedException(Core.UcException error) : base(error) { } }
public sealed class NavigationCanceledException : OperationCanceledException
{
    public bool BrowserMayHaveAdvanced { get; }
    internal NavigationCanceledException(Core.NavigationCanceledException error) : base(error.Message, error, error.CancellationToken) => BrowserMayHaveAdvanced = error.BrowserMayHaveAdvanced;
}
internal static class Api
{
    internal static Exception Translate(Core.UcException error) => error.Category switch
    {
        Core.ErrorCategory.StaleAttachment => new StaleAttachmentException(error),
        Core.ErrorCategory.AmbiguousTarget => new AmbiguousTargetException(error),
        Core.ErrorCategory.ChromeExited => new BrowserExitedException(error),
        _ => new UcException(error)
    };
    public static async Task<T> Call<T>(Func<Task<T>> action)
    {
        try { return await action().ConfigureAwait(false); }
        catch (Core.UcException error) { throw Translate(error); }
        catch (Core.NavigationCanceledException error) { throw new NavigationCanceledException(error); }
    }
    public static async Task Call(Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (Core.UcException error) { throw Translate(error); }
        catch (Core.NavigationCanceledException error) { throw new NavigationCanceledException(error); }
    }
}
