using OpenQA.Selenium;
using OpenQA.Selenium.Remote;

namespace BrowserDock;

public sealed partial class Browser
{
    /// <summary>Wait for an editable element, clear it, then input text once. A final LF submits its form.</summary>
    public ValueTask TypeAsync(Locator locator, string text, ElementWaitOptions? options = null, CancellationToken cancellationToken = default)
        => InputForLeaseAsync(locator, text, true, options, AttachmentEpoch, () => true, cancellationToken);

    /// <summary>Wait for an editable element, then append text once. A final LF submits its form.</summary>
    public ValueTask SendKeysAsync(Locator locator, string text, ElementWaitOptions? options = null, CancellationToken cancellationToken = default)
        => InputForLeaseAsync(locator, text, false, options, AttachmentEpoch, () => true, cancellationToken);

    internal ValueTask InputForLeaseAsync(Locator locator, string text, bool clear, ElementWaitOptions? wait, long epoch, Func<bool> valid, CancellationToken caller)
    {
        RuntimeCompatibility.NotNull(text, nameof(text));
        return InputCoreAsync();
        async ValueTask InputCoreAsync()
            => _ = await WaitForElementCoreAsync(locator, wait, clear ? ElementCondition.Type : ElementCondition.Append, epoch, valid, caller, text).ConfigureAwait(false);
    }

    private static bool IsEditable(RemoteWebDriver driver, IWebElement element)
    {
        var tag = element.TagName.ToLowerInvariant();
        if (tag is not ("input" or "textarea"))
            // Native Ctrl+End can escape even an independent nested host across
            // contenteditable=false. Only accept editors without editable ancestors.
            return driver.ExecuteScript("""
                /* browserDockEditingHost */
                const element = arguments[0];
                if (!element.isContentEditable) return false;
                for (let ancestor = element.parentElement; ancestor; ancestor = ancestor.parentElement) {
                    if (ancestor.isContentEditable) return false;
                }
                return true;
                """, element) is true;
        if (string.Equals(element.GetDomProperty("readOnly"), "true", StringComparison.OrdinalIgnoreCase)) return false;
        return tag == "textarea" || element.GetDomProperty("type") is "text" or "search" or "tel" or "url" or "email" or "password" or "number";
    }

    private static void PerformInput(RemoteWebDriver driver, IWebElement element, string text, bool clear, Action check)
    {
        var submit = text.EndsWith("\n", StringComparison.Ordinal);
        var value = submit ? text.Substring(0, text.Length - (text.EndsWith("\r\n", StringComparison.Ordinal) ? 2 : 1)) : text;
        if (clear) { check(); element.Clear(); }
        if (value.Length != 0)
        {
            check();
            // Append must not overwrite a selection or insert at a stale caret.
            // Use native keys, releasing modifiers before the caller's text.
            element.SendKeys(clear ? value : Keys.Control + Keys.End + Keys.Null + value);
        }
        if (submit)
        {
            check();
            // Match the selected Selenium-style submit contract, using one
            // dispatch after the guard. IWebElement.Submit performs an extra
            // attribute read before its side effect, leaving a cancellation gap.
            var submitted = driver.ExecuteScript("""
                /* browserDockSubmit */
                const element = arguments[0];
                const form = element.localName === 'input' || element.localName === 'textarea'
                    ? element.form : element.closest('form');
                if (!form) return false;
                const event = form.ownerDocument.createEvent('Event');
                event.initEvent('submit', true, true);
                if (form.dispatchEvent(event)) {
                    form.ownerDocument.defaultView.HTMLFormElement.prototype.submit.call(form);
                }
                return true;
                """, element);
            if (submitted is not true)
                throw new BrowserDockException(ErrorCategory.ElementInteractionFailure, "The input has no containing or associated form to submit.");
        }
        check();
    }
}
