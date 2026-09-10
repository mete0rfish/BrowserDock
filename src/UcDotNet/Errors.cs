namespace UcDotNet;

public enum ErrorCategory { ConfigurationError, VersionMismatch, PatchMismatch, ChromeStartFailure, DevToolsEndpointFailure, ChromeExited, DriverProcessFailure, WebDriverSessionFailure, UnsupportedAttachedCommand, AmbiguousTarget, TargetClosed, TargetCrashed, StaleAttachment, AttachmentLost, StaleDomElement, OperationTimedOut, CleanupIncomplete, ProtocolError, ElementNotFound, ElementInteractionFailure }
public class UcException : Exception
{
    public ErrorCategory Category { get; }
    public Guid OperationId { get; internal set; }
    public BrowserHealthSnapshot? Diagnostic { get; internal set; }
    public bool Retryable { get; init; }
    public bool BrowserMayHaveAdvanced { get; internal set; }
    public IReadOnlyList<string> CleanupFailures { get; internal set; } = [];
    public UcException(ErrorCategory category, string message, Exception? inner = null) : base(message, inner) => Category = category;
}
public sealed class StaleAttachmentException() : UcException(ErrorCategory.StaleAttachment, "The attachment has changed; acquire a new lease or explicitly reacquire the element.");
public sealed class AmbiguousTargetException() : UcException(ErrorCategory.AmbiguousTarget, "Select an explicit page target before reconnecting.");
public sealed class BrowserExitedException() : UcException(ErrorCategory.ChromeExited, "The owned Chrome process exited. Automatic browser restart is disabled.");
public sealed class NavigationCanceledException : OperationCanceledException
{
    public bool BrowserMayHaveAdvanced { get; }
    public NavigationCanceledException(CancellationToken token, bool advanced) : base("Navigation was canceled.", token) => BrowserMayHaveAdvanced = advanced;
}
