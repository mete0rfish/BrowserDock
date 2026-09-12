namespace BrowserDock.Legacy;

public enum BrowserState { Stopped, StartingChrome, ChromeReady, AttachingWebDriver, WebDriverAttached, Disconnecting, CdpOnly, Reattaching, Faulted, Disposing }
public enum NavigationMode { Standard, Detached, CdpOnly }
public enum NavigationWaitUntil { Commit, DOMContentLoaded, Load, NetworkIdle }
public enum TargetPolicy { CurrentControlled, ReplaceControlled }
public enum DriverPatchMode { Disabled, ValidateOnly, BinaryCompatibility }
public enum BrowserOwnership { Library }
public enum NavigationOutcome { Completed, Download }
public enum LocatorKind { Css, XPath, Id, Name, TagName, LinkText }
public enum ErrorCategory { ConfigurationError, VersionMismatch, PatchMismatch, ChromeStartFailure, DevToolsEndpointFailure, ChromeExited, DriverProcessFailure, WebDriverSessionFailure, UnsupportedAttachedCommand, AmbiguousTarget, TargetClosed, TargetCrashed, StaleAttachment, AttachmentLost, StaleDomElement, OperationTimedOut, CleanupIncomplete, ProtocolError, ElementNotFound, ElementInteractionFailure }
