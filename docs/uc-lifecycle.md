# UC lifecycle and navigation

`Browser.Uc` is a non-owning facade over an existing browser. The Core surface uses
ValueTask; `BrowserDock.Legacy` has equivalent Task methods and mutable option
classes captured before asynchronous work. Accessing the facade does not enable
driver patching or a launch/script profile. Dispose the owning Browser for final
cleanup; disposing a lease only invalidates that lease. A separate optional
[UC launch profile](uc-launch-profile.md) configures startup and document scripts.

## Call contracts

`A` means WebDriverAttached and `C` means healthy CdpOnly. Other states are rejected.
All navigation accepts an absolute enabled Uri and an optional `UcNavigationOptions`
with `Target` and `WaitUntil` (default Load). Select an explicit TargetKey when
multiple pages exist; the facade never guesses from window ordering. Replacement
closes only the controlled page and preserves other pages.

| Method | Entry | Ordered behavior | Return |
| --- | --- | --- | --- |
| `DisconnectAsync()` | A/C | Invalidate old references, drain commands and terminate the driver if attached; confirm health | C |
| `ConnectAsync()` | C | Create and bind a new session; already A is ConfigurationError | A + fresh lease |
| `ReconnectAsync(duration)` | A/C | Ensure detached → minimum duration → create/bind fresh session | A + fresh lease |
| `NavigateAsync(url, options)` | A | Current-page W3C navigation → selected CDP milestone; same Core Standard contract | A + NavigationResult |
| `OpenAsync(url, options)` | A/C | Ensure detached → current-page CDP navigation → milestone; reattach only if initially A | Original attachment state + NavigationResult |
| `OpenWithTabAsync(url, options)` | A/C | Ensure detached → replace controlled page → CDP navigation → milestone; reattach only if initially A | Original attachment state + NavigationResult |
| `OpenWithReconnectAsync(url, duration, options)` | A/C | Ensure detached → replace/navigate → milestone and minimum duration → attach | A + NavigationResult |
| `OpenWithDisconnectAsync(url, duration, options)` | A/C | Ensure detached → replace/navigate → milestone and minimum duration; never attach | C + NavigationResult |

Open methods and composite reconnect own one lifecycle gate throughout. Concurrent
operations queue; queue waiting consumes their budget and cannot perform remote
actions before admission. Navigation operations use `BrowserTimeouts.Navigation`;
composite `ReconnectAsync` uses `BrowserTimeouts.Reconnect` for the whole sequence.
The attachment phase additionally has the Reconnect cap. Disconnect retains Core's
DisconnectDrain + bounded cleanup budget; Connect uses the Reconnect budget.

## Minimum disconnected duration

The TimeSpan argument defaults to zero. It must be nonnegative and strictly less
than the whole operation budget; invalid arguments fail before probing, selecting
targets or detaching. The duration starts when owned-driver termination is confirmed.
When already C, it starts after this operation confirms health and driver absence,
without credit for time spent disconnected before the call. A monotonic clock
measures elapsed time; replacement, navigation and post-exit probes count toward it.

For duration 3s and navigation taking 2s after driver exit, the remaining hold is
approximately 1s. If navigation takes 5s, no further hold is required. Attachment
itself takes additional time. Core `ReconnectDelay=3s` instead adds three seconds
*after* navigation; the facade does not use that option.

Reattachment/return requires both the requested milestone and duration. Missing
milestones time out even when the duration elapsed. Choose Commit explicitly if
waiting for a full load is inappropriate; the facade does not silently change Load
into Commit. Redirects and same-document navigation retain Core semantics. A
download is a terminal NavigationOutcome.Download and still observes the requested
hold and final attachment policy. Target closure/crash is an error, not a load.
Helpers that return disconnected probe Chrome/CDP and resolve the controlled target
again after the hold, within the same operation budget (`uc-verify-state`). A prior
load milestone cannot turn Chrome exit or target closure during the hold into a
successful result. No surviving tab is selected as a fallback.

## Cancellation and failure

Cancellation before mutation preserves the existing attachment. After detachment
begins, owned-resource cleanup finishes within its separate bounded budget. A
healthy browser remains C after cancellation during navigation/hold, with old
leases/elements invalid. A failed attachment is cleaned up by Core; health or
cleanup failures may leave Faulted. Successful attachment increments generation;
old references never revive. No navigation is automatically replayed.

Inspect NavigationCanceledException's caller token and BrowserMayHaveAdvanced, or
BrowserDockException's category, operation ID, health and cleanup details. UC
operations conservatively mark possible progress before disconnect, tab replacement,
navigation or attachment begins; an early failure need not have completed that step.
Timeout is OperationTimedOut, distinct from caller cancellation. Navigation.Stage
and Navigation.UcOperation in Exception.Data retain stage context through Legacy.
The more specific CDP navigation stage is retained when available.
All UC methods preserve the caller's CancellationToken even when cancellation
occurs while queued at the lifecycle gate or before admission. Such canceled calls
perform no remote work and retain the existing attachment. The UC boundary leaves
already-normalized navigation errors, timeout categories and progress metadata intact.

## Examples

```csharp
var uc = browser.Uc;
await uc.OpenWithDisconnectAsync(url, TimeSpan.FromSeconds(1));
await using var attached = await uc.ConnectAsync();
await using var fresh = await uc.ReconnectAsync(TimeSpan.FromSeconds(1));
// attached is stale; fresh belongs to the new generation.
```

C# 7.3 / Legacy:

```csharp
var uc = browser.Uc;
await uc.OpenWithDisconnectAsync(url, TimeSpan.FromSeconds(1));
var lease = await uc.ConnectAsync();
try { Console.WriteLine(await lease.Commands.GetTitleAsync()); }
finally { await lease.DisposeAsync(); }
// The application's outer finally must await browser.DisposeAsync().
```

Runnable source samples: [modern](../samples/UcLifecycle/Program.cs) and
[Framework 4.8.1 / C# 7.3](../samples/UcFramework481/Program.cs).

## Reference and evidence boundaries

This independently implemented facade follows the approved
[compatibility matrix](seleniumbase-compatibility.md), referencing SeleniumBase
4.53.7 at `4ee7dfc4ae83c19385f5ac129f2cda0cfa863d80`. Intentional differences include
detach-before-CDP navigation, explicit targets, zero default durations, typed
arguments, selected completion milestones, and reported failures. There is no
separate HTTP preflight, URL guessing, site-based mode switching, string sentinel,
or implicit reconnect from C except when explicitly requested by the helper.

`UcWorkflowTests` cover ordering, timing overlap, failure/cancellation, public
preflight, serialization and deadline/Legacy mapping. `UcBrowserTests` exercise
actual process/session/document identity, tab policy and cancellation on the Windows
fixture. Passing these tests is not a Python differential comparison; #36 tracks
same-candidate upstream evidence. #41 remains open for intermittent existing CDP
navigation failures. Launch/script integration and real patch recipes remain #30/#31.
