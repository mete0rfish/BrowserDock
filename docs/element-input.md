# Guarded text input

Core and Legacy expose these methods on `Browser` and `lease.Commands`:

| Method | Effect after the first matching element becomes editable |
|---|---|
| `TypeAsync(locator, text, options, token)` | Clear the existing value, then send the text once. Empty text clears only. |
| `SendKeysAsync(locator, text, options, token)` | Preserve the value, move to its end with native Ctrl+End, release modifiers, and send the text once. Empty text performs no input. |

Both use `ElementWaitOptions` and return no element (`ValueTask` in Core, `Task`
in Legacy). The existing `ElementRef.ClearAsync` and `ElementRef.SendKeysAsync`
remain raw single-attempt primitives; they do not inherit waiting, caret movement
or trailing-newline handling.

```csharp
await browser.TypeAsync(Locator.Id("name"), "새 이름", new ElementWaitOptions
{
    Timeout = TimeSpan.FromSeconds(10),
    FramePath = new[] { Locator.Id("editor-frame") }
}, cancellationToken);
await browser.SendKeysAsync(Locator.Id("notes"), " appended", null, cancellationToken);
```

The example also works with `using BrowserDock.Legacy` in C# 7.3. Target and frame
options are captured on invocation and follow the [element wait context rules](element-waits.md).
An empty frame path means the top document. Options never implicitly reconnect
WebDriver or switch to CDP.

## Editable state and native input

The first match must be displayed, enabled and editable. Supported targets are
textarea, contenteditable elements, and input types text/search/tel/url/email/
password/number. Input and textarea must not be read-only, even if they also have
a contenteditable attribute. Other types (including file, checkbox and date
controls) are outside this text-input subset and keep polling until the deadline
unless their state/type changes. Hidden, disabled, read-only, missing and stale
observations can be retried before any side effect.

Input uses WebDriver clear/key commands; there is no JavaScript assignment to the
value, typing-speed simulation or input fallback. Text, including Unicode and
interior line breaks, is preserved. Native element constraints, keyboard handlers
and browser behavior still apply. Append explicitly moves to the end so an
existing caret or selection does not replace earlier text. This native Ctrl+End
behavior targets BrowserDock's supported Windows environment; Linux Chromium is
used for investigation evidence. It is not a macOS compatibility claim.

## Trailing newline and submission

A final LF or CRLF is removed and requests exactly one form-submission dispatch
after input. `"value\n"` types/appends `value`, then submits. `"\n"` submits without
sending an Enter key; `TypeAsync` still clears first. Interior newlines and special
key characters retain native key semantics and can trigger application handlers.

Submission follows the selected Selenium-style contract: locate the element's
associated or containing form, dispatch a cancelable bubbling `submit` event, and
call the native form `submit()` only if the event is not canceled. It uses one
guarded script command, does not click a submit button, and does not run native
constraint validation like `requestSubmit()`. A canceled submit event is an
acknowledged operation. A missing form throws `ElementInteractionFailure` after
any preceding input has already occurred. Native `PressKeysAsync` semantics for
a trailing Enter remain separate follow-up work.

Completion acknowledges input/submission, not navigation or an application result.
Use a separate explicit wait for the resulting state. This selected subset is an
intentional deviation from upstream retries/fallbacks and caret behavior; it does
not establish SeleniumBase equivalence.

## Deadlines, cancellation and partial completion

One overall deadline covers queueing, context restoration, observation, polling,
clear, input and submission. Each command also retains `BrowserTimeouts.Command`.
The target/frame gate remains held from a successful observation through the last
side effect. Before **each** clear, key command and submit, both deadlines and
lease/attachment validity are checked.

Once a side effect starts, there is no automatic reacquisition, fallback or replay,
including after stale DOM, a lost response, cancellation or timeout. For example,
if clear triggers a rerender, the replacement element is not automatically typed
into. If cancellation follows clear, input and submission are stopped; clearing
cannot be undone. If an input/submission response is lost, application state may
already have changed. Inspect it before deciding whether to retry. Existing
bounded command recovery and caller-token preservation apply.

## Scope and verification

This is partial progress on [#32](https://github.com/mete0rfish/BrowserDock/issues/32).
Core/Legacy `IBrowserCommands` implementers must add the two new members during
this 0.x API expansion. Press-keys helpers, additional waits and compatibility
context remain follow-up work.

[ElementInputTests](../tests/Shared/ElementInputTests.cs) exercises ordered
replace/append, newline submission, readiness changes, partial failures,
cancellation and command-expiry boundaries through the controlled W3C executor.
[ElementInputBrowserTests](../tests/Shared/ElementInputBrowserTests.cs) verifies
real values/key events, Unicode, existing carets/selections, form events,
textarea/contenteditable, nested frames and no replay after clear-triggered DOM
replacement. Its shared assertions run in the Linux investigation harness.
Windows actual-browser execution and corresponding high-level SeleniumBase
comparisons ([#36](https://github.com/mete0rfish/BrowserDock/issues/36)) remain
unverified; common Windows/.NET Framework CI is separate evidence.
