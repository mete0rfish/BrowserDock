using System.Collections;
using System.Diagnostics;
using OpenQA.Selenium;
using BrowserDock.WebDriver;

namespace BrowserDock;

public sealed partial class Browser
{
    /// <summary>Wait for the first matching element to exist, including hidden elements.</summary>
    public ValueTask<ElementRef> WaitForExistsAsync(Locator locator, ElementWaitOptions? options = null, CancellationToken cancellationToken = default)
        => WaitForElementForLeaseAsync(locator, options, false, AttachmentEpoch, () => true, cancellationToken);

    /// <summary>Wait for the first matching element to be displayed. Does not guarantee clickability.</summary>
    public ValueTask<ElementRef> WaitForVisibleAsync(Locator locator, ElementWaitOptions? options = null, CancellationToken cancellationToken = default)
        => WaitForElementForLeaseAsync(locator, options, true, AttachmentEpoch, () => true, cancellationToken);

    /// <summary>Wait until no element matches. A hidden element is still present.</summary>
    public ValueTask WaitForAbsentAsync(Locator locator, ElementWaitOptions? options = null, CancellationToken cancellationToken = default)
        => WaitForAbsentForLeaseAsync(locator, options, AttachmentEpoch, () => true, cancellationToken);

    internal async ValueTask<ElementRef> WaitForElementForLeaseAsync(Locator locator, ElementWaitOptions? wait, bool visible, long epoch, Func<bool> valid, CancellationToken caller)
        => (await WaitForElementCoreAsync(locator, wait, visible ? ElementCondition.Visible : ElementCondition.Exists, epoch, valid, caller).ConfigureAwait(false))!;

    internal async ValueTask WaitForAbsentForLeaseAsync(Locator locator, ElementWaitOptions? wait, long epoch, Func<bool> valid, CancellationToken caller)
        => _ = await WaitForElementCoreAsync(locator, wait, ElementCondition.Absent, epoch, valid, caller).ConfigureAwait(false);

    private enum ElementCondition { Exists, Visible, Absent }
    private async ValueTask<ElementRef?> WaitForElementCoreAsync(Locator locator, ElementWaitOptions? wait, ElementCondition condition, long epoch, Func<bool> valid, CancellationToken caller)
    {
        ElementWaitOptions.ValidateLocator(locator);
        var captured = (wait ?? new()).Capture();
        var find = new FindOptions { Target = captured.Target, FramePath = captured.FramePath };
        TargetKey? target = captured.Target;
        var id = Guid.NewGuid();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, lifetime.Token);
        using var deadline = new Deadline(captured.Timeout, linked.Token);
        var clock = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                CheckDeadline();
                var observation = await CommandAsync<(bool Satisfied, ElementRef? Element)>(epoch, valid, driver =>
                {
                    target ??= cdp!.Resolve().Key;
                    try { RestoreElementContext(driver, target.Value, captured.FramePath); }
                    catch (Exception e) when (e is NoSuchElementException or NoSuchFrameException or StaleElementReferenceException)
                    {
                        // A missing/replaced ancestor frame is not evidence that
                        // the requested element is absent in its intended context.
                        return (Satisfied: false, Element: (ElementRef?)null);
                    }
                    try
                    {
                        var element = driver.FindElement(Attachment.By(locator));
                        var elementId = attachment!.Executor.LastFoundElementId
                            ?? throw new BrowserDockException(ErrorCategory.ProtocolError, "W3C find-element response did not contain an element reference.");
                        if (condition == ElementCondition.Absent || (condition == ElementCondition.Visible && !element.Displayed))
                            return (false, (ElementRef?)null);
                        return (true, (ElementRef?)new ElementRef(this, elementId, locator, find, target.Value, SessionGeneration, epoch));
                    }
                    catch (NoSuchElementException) { return (condition == ElementCondition.Absent, (ElementRef?)null); }
                    catch (StaleElementReferenceException) { return (false, (ElementRef?)null); }
                }, caller, deadline.Token).ConfigureAwait(false);
                CheckDeadline();
                if (!valid() || AttachmentEpoch != epoch || State != BrowserState.WebDriverAttached)
                    throw new StaleAttachmentException { OperationId = id, Diagnostic = Health };
                if (observation.Satisfied) return observation.Element;
                // Release the command gate between observations so that other
                // commands can change the DOM and lifecycle operations can proceed.
                // The shared deadline bounds this delay. Clipping a TimeSpan to
                // its remaining fraction of a millisecond can round down to zero
                // in Task.Delay and dispatch extra polls just before expiry.
                await Task.Delay(captured.PollInterval, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException original)
        {
            // Polling uses a linked token; expose the original public caller
            // token and retain command recovery diagnostics when present.
            Exception error = caller.IsCancellationRequested
                ? new OperationCanceledException("Element wait was canceled by its caller.", original, caller)
                : lifetime.IsCancellationRequested
                    ? new OperationCanceledException("Browser lifetime ended during the element wait.", original, lifetime.Token)
                    : new BrowserDockException(ErrorCategory.OperationTimedOut, "Element wait exceeded its deadline.", original)
                    {
                        OperationId = original.Data["OperationId"] is Guid operationId ? operationId : id,
                        Diagnostic = original.Data["Diagnostic"] as BrowserHealthSnapshot ?? Health,
                        CleanupFailures = original.Data["CleanupFailures"] as IReadOnlyList<string> ?? []
                    };
            if (error is OperationCanceledException)
            {
                foreach (DictionaryEntry entry in original.Data) error.Data[entry.Key] = entry.Value;
                if (!error.Data.Contains("OperationId")) error.Data["OperationId"] = id;
                if (!error.Data.Contains("Diagnostic")) error.Data["Diagnostic"] = Health;
                if (!error.Data.Contains("CleanupFailures")) error.Data["CleanupFailures"] = Array.Empty<string>();
            }
            throw error;
        }

        void CheckDeadline()
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed >= captured.Timeout) throw new OperationCanceledException(deadline.Token);
        }
    }
}
