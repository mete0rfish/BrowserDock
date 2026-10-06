# Element waits and value reads

Core and Legacy provide the same WebDriver operations. These require an attached
session; they do not reconnect WebDriver or select a CDP backend implicitly.

| Operation | Completion |
|---|---|
| `WaitForExistsAsync(locator, options, token)` | The first matching element exists, including hidden elements; returns a new `ElementRef`. |
| `WaitForVisibleAsync(locator, options, token)` | The first matching element is displayed according to Selenium; returns a new `ElementRef`. This does not establish enabled, unobscured or clickable state. |
| `WaitForAbsentAsync(locator, options, token)` | No element matches in the requested context; returns no element. A hidden element is still present. |
| `GetDomAttributeAsync(name, token)` | DOM content attribute, or null when absent. |
| `GetDomPropertyAsync(name, token)` | Current DOM property, using Selenium's string representation; null for a missing/null property. This is not a typed JSON accessor. |
| `GetValueAsync(token)` | Current `value` property, including user/JavaScript edits. |

`GetAttributeAsync` remains an alias for the content-attribute read. It does not
fall back to a property. `GetTextAsync` retains its rendered-text behavior; it
neither waits for visibility nor substitutes an input's live value.

## Core

```csharp
var input = await browser.WaitForVisibleAsync(Locator.Id("name"), new ElementWaitOptions
{
    Timeout = TimeSpan.FromSeconds(10),
    PollInterval = TimeSpan.FromMilliseconds(100),
    Target = selectedTarget,
    FramePath = new[] { Locator.Id("form-frame") }
}, cancellationToken);

string? initial = await input.GetDomAttributeAsync("value", cancellationToken);
string? current = await input.GetValueAsync(cancellationToken);
// <input value="initial"> changed through input.value = "edited":
// initial == "initial", current == "edited".

await browser.WaitForAbsentAsync(Locator.Css(".loading"), cancellationToken: cancellationToken);
```

All three waits are also available through `lease.Commands`. A disposed lease
cannot start or continue a wait. An element returned by a wait follows the same
reference lifetime as an element returned by `FindAsync`.

## Legacy / C# 7.3

With `using BrowserDock.Legacy`, options are mutable classes and all operations
return `Task`:

```csharp
var input = await browser.WaitForVisibleAsync(Locator.Id("name"), new ElementWaitOptions
{
    Timeout = TimeSpan.FromSeconds(10),
    PollInterval = TimeSpan.FromMilliseconds(100)
}, cancellationToken);
string initial = await input.GetDomAttributeAsync("value", cancellationToken);
string current = await input.GetValueAsync(cancellationToken);
await browser.WaitForAbsentAsync(Locator.Css(".loading"), null, cancellationToken);
```

## Deadline, context and reference rules

- Defaults are a 10-second overall timeout and a 100-ms polling interval. Both
  must be between 1 ms and `Int32.MaxValue` ms, inclusive. Infinite/zero waits
  are rejected with `ConfigurationError`; use `FindAsync` for a single lookup.
- The same overall deadline includes command queueing, restoring the window/frame
  context, lookup, visibility checks and polling delays. Each observation also
  retains the browser's existing `BrowserTimeouts.Command` cap. A polling interval
  longer than the timeout is allowed and is clipped to the remaining budget.
- Timeout throws `OperationTimedOut`; caller cancellation preserves the original
  cancellation token. Neither is reported as a false/success result. Expiry while
  queued or between observations preserves a healthy attachment. Cancellation or
  expiry during a running driver command retains the existing bounded attachment
  cleanup behavior, which can leave the browser in `CdpOnly`.
- Options and frame lists are captured at invocation. The attachment epoch is
  fixed at invocation; an omitted target is resolved at the first observation and
  then fixed. Each observation restores that target and the complete frame path
  from the top document. An empty path means the top document, irrespective of a
  prior `SwitchToFrameAsync` call. Concurrent target selection does not retarget
  a wait that has already resolved its target.
- Missing/replaced ancestor frames are retried within the original deadline;
  they never prove element absence. A missing target, closed window, invalid
  selector or failed attachment is propagated. DOM staleness during observation
  is retried by locating again, including after document replacement in the same
  target. A changed attachment or disposed lease fails with `StaleAttachment`.
  This also applies when the change happens during a polling delay that ends at
  the deadline; explicit caller/browser-lifetime cancellation still takes priority.
- Waits release the command gate between observations. They perform no click,
  typing, submission or navigation. Returned references can become stale after
  the observation; reads on an ordinary reference still report `StaleDomElement`
  and require explicit reacquisition, as before.
- All selectors use first-match semantics for presence/visibility. Absence means
  **no** matching element. Live properties/content attributes are snapshots of
  the current DOM, not a copy of the original HTTP markup.

## Scope and evidence

The [guarded click](element-clicks.md) and [text input](element-input.md) APIs build on these wait contracts.

This is the waits/value-read slice of [#32](https://github.com/mete0rfish/BrowserDock/issues/32).
Hidden-or-absent waits, text waits, multiple-element search, press-keys helpers,
ambient-frame compatibility, screenshots and assertion helpers remain follow-up
work. No SeleniumBase equivalence claim is added by this slice.

[ElementWaitTests](../tests/Shared/ElementWaitTests.cs) exercises the real Browser
command/lease path and Selenium serialization against a controlled W3C executor
and CDP fixture. It covers insertion, display changes, rerendering, absence,
nested-frame restoration, option capture, value/attribute routing, queue and
in-flight deadlines/cancellation, stale references and attachment replacement.
These common tests run in the existing label-triggered CI, including the Legacy
.NET Framework job. Windows actual-browser validation and pinned SeleniumBase
comparison are separate acceptance evidence.

[ElementWaitBrowserTests](../tests/Shared/ElementWaitBrowserTests.cs) supplies the
actual-DOM scenario for the optional Windows browser suite. The same scenario was
executed locally through a test-only Linux hosting harness with Chromium and
ChromeDriver **154.0.8037.57**, for both Core and Legacy: delayed insertion,
hidden-to-visible transition, content attribute versus edited value, removal,
stale-element rejection, nested frames and top-level context reset passed.
This does not expand the supported hosting platform or establish Windows browser
acceptance / SeleniumBase equivalence. Windows browser execution was excluded.
