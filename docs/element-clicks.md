# Guarded element clicks

Core and Legacy expose `ClickAsync(locator, ElementWaitOptions, token)` on both
`Browser` and `lease.Commands`. The operation requires an attached WebDriver
session and returns no element. It waits for the **first matching element** to be
displayed and enabled, issues one WebDriver click and returns after the driver
acknowledges it. `ElementRef.ClickAsync(token)` retains its existing single-attempt
behavior, without lookup or automatic waiting.

```csharp
await browser.ClickAsync(Locator.Id("save"), new ElementWaitOptions
{
    Timeout = TimeSpan.FromSeconds(10),
    PollInterval = TimeSpan.FromMilliseconds(100),
    Target = selectedTarget,
    FramePath = new[] { Locator.Id("editor-frame") }
}, cancellationToken);
```

The same example works with `using BrowserDock.Legacy` in C# 7.3: Legacy returns
`Task`, while Core returns `ValueTask`. Options are snapshotted at invocation.
An omitted frame path means the top document. The attachment epoch is fixed at
invocation; an omitted target is fixed on the first observation. A disconnected
or replaced attachment and a disposed lease cannot continue the operation.

## Deadline and dispatch boundary

One overall deadline includes command queueing, restoring target/frame context,
locating the element, visibility/enabled checks, polling and the click response.
Defaults, validation and error/cancellation rules match [element waits](element-waits.md).
Each observation and its optional click also retain `BrowserTimeouts.Command`.
The command gate is released between observations. On a successful observation,
context restoration, readiness checks and click stay under the same gate so that
another library command cannot change the selected frame between them.

Before dispatch, missing elements, hidden/disabled elements and stale DOM
observations can be retried by locating again. Missing/replaced ancestor frames
can also be retried. An invalid selector, missing target or closed window fails.
Readiness does not freeze the DOM or prove that the element is unobscured; the
browser's native WebDriver hit test is authoritative at click time.

**Once the click is dispatched, it is never automatically repeated.** This includes
stale-element, intercepted-click, non-interactable-element, timeout and transport
errors. Intercepted/non-interactable clicks report `ElementInteractionFailure`;
a stale click reports `StaleDomElement`. There is no JavaScript click or operating
system input fallback. This deliberately differs from SeleniumBase fallback and
retry behavior; no equivalence claim is made.

A failed, canceled or timed-out click might already have changed the page. The
caller must inspect application state before deciding whether to try again.
Cancellation cannot undo a delivered event. In-flight cancellation/transport
failure retains existing bounded attachment recovery and may leave `CdpOnly`;
expiration while queued or polling preserves a healthy attachment.

Successful completion means the driver acknowledged the click. It does not wait
for navigation, loading indicators or application state to settle. Use a separate
explicit wait for the expected application result. There is no automatic
reconnection or CDP backend selection.

## Scope and evidence

This is another partial implementation of [#32](https://github.com/mete0rfish/BrowserDock/issues/32).
Typing, additional waits, multiple-element lookup and compatibility context remain
follow-up work. Adding the member to Core/Legacy `IBrowserCommands` requires custom
implementers to update their implementations during this 0.x API expansion.

- [ElementClickTests](../tests/Shared/ElementClickTests.cs) exercises Core/Legacy
  and browser/lease entry points through the real command gate and controlled W3C
  executor: readiness changes, stale DOM, nested frames, invalidation before click,
  side-effect failures, cancellation/timeouts and no replay or fallback.
- [ElementWaitTests](../tests/Shared/ElementWaitTests.cs) also runs click observation
  paths through queue/poll deadlines, cancellation, disposal and replacement cases.
- [ElementClickBrowserTests](../tests/Shared/ElementClickBrowserTests.cs) checks real
  DOM events, hidden/disabled state, an overlay and explicit nested-frame versus
  top-document selection. Its shared assertions are runnable through the Linux
  investigation harness. Windows browser execution remains unverified for this
  change; the optional Windows fixture requires the dedicated browser CI label.
- High-level differential evidence against the pinned SeleniumBase baseline remains
  [#36](https://github.com/mete0rfish/BrowserDock/issues/36).
