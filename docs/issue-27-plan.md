# Issue 27 command timeout and cancellation plan

This draft describes the proposed fix for [issue 27](https://github.com/mete0rfish/BrowserDock/issues/27).
Queued commands must share the operation deadline, report timeout separately from
caller cancellation, and leave a healthy attachment intact if execution never
started. Failed commands need bounded recovery that cannot remove a newer
attachment. Implementation and portable regression tests are now checked in. Windows browser
acceptance remains a separate, pending validation step.

## Evidence and limits

Analysis used commit `83c38e4` on branch
`fix/27-command-timeout-cancellation`. In `Browser.CommandAsync`, the command gate
is acquired before the error-handling block. Recovery acquires the lifecycle gate
without a cancellation token, before the ten-second cleanup scope starts.

A temporary .NET 10 console probe invoked the actual `CommandAsync` while holding
its command gate. Reflection configured a Browser instance for this isolated
queue test; no Chrome, attachment, or remote driver was created. Each scenario
ran three times with a 150 ms command budget:

| Scenario | Observed result |
| --- | --- |
| Queue deadline expiration | `OperationCanceledException`, rather than `OperationTimedOut`, on all three runs; elapsed times were 168, 153, and 157 ms. |
| Caller cancellation while queued | `OperationCanceledException` on all three runs; its token differed from the caller token. Cancellation was not translated into a timeout. |
| Queue invariants in both scenarios | Action delegate was never invoked, the externally held semaphore remained acquired, and state and epoch were unchanged. |

The probe exited with failure because it asserted timeout normalization and
caller-token preservation. It was temporary and is not a checked-in regression
test. These results do not establish real attachment health, remote request
counts, process cleanup, lifecycle contention, or Legacy parity. Those require
the acceptance tests below. No complete solution build or browser acceptance run
was performed for this draft.

## Implementation

1. Create the operation identifier and deadline before queueing. Put validation,
   command-gate acquisition, admission, and execution inside the common error
   boundary. Track gate acquisition and execution admission separately; release
   a semaphore only when this operation acquired it. Recheck cancellation and
   reference validity before dispatching work.
2. Distinguish caller cancellation, browser lifetime cancellation, operation
   expiration, and driver failure before asynchronous recovery. Preserve caller
   cancellation and its token. Map deadline expiration to `OperationTimedOut`;
   do not misreport Stop/Dispose as an operation timeout. Preserve the original
   exception and operation identifier in the resulting diagnostics.
3. Only initiate failed-command recovery when execution actually started and the
   failure requires it. A queue timeout or cancellation must not detach, kill,
   or invalidate a healthy attachment. Ordinary element/argument failures retain
   their existing error mappings.
4. Capture the attachment used by the command and its epoch. Prevent new work
   from using that failed attachment before releasing the command gate. Recovery
   must check identity and epoch before changing shared state or removing an
   attachment. An old failure must not close admission for a replacement.
5. Start one independent cleanup scope before waiting for the lifecycle gate.
   Its existing ten-second cap includes gate acquisition, owned-driver cleanup,
   and health probing; helpers must not restart that budget. Keep the original
   command error and record cleanup failures and the resulting health snapshot.
6. Preserve the existing distinction between ending an await and stopping driver
   I/O. `Attachment.InvokeAsync` already detaches and terminates its owned driver
   when an executing command is canceled. Recovery and Stop/Dispose must have
   coordinated ownership so they cannot destroy the same resources unsafely.

Do not hold the command gate while waiting for the lifecycle gate:
`SelectTargetAsync` and window switching already acquire lifecycle before
commands. Introducing the reverse order would create a deadlock risk.

## Recovery decisions to verify

Lifecycle-gate acquisition can itself exhaust the cleanup budget. That path
must return within the budget, preserve the original failure, record incomplete
cleanup, and prevent reuse of the failed attachment. Shared state changes must
remain conditional on attachment identity and must not overwrite a newer
attachment or a completed Stop/Dispose transition. A timeout token alone is not
a complete solution to this case.

Use an identity-aware admission invalidation step and a coordinated cleanup
owner for the failed attachment. Resolve the exact helper boundary and the
Stop/Dispose handoff with deterministic contention tests before implementation
is considered ready. Define precedence when caller cancellation and lifetime
cancellation race, and document how cancellation exposes required diagnostics.

## Acceptance tests

Use explicit entry/release barriers to establish queueing and contention, with
an outer watchdog to catch hangs. Extend the fixture host where needed to stall
an ordinary WebDriver command after session creation. Its existing fake-driver
mode only stalls new-session creation.

- [ ] Queue expiration reports `OperationTimedOut`, makes zero remote calls,
  leaves the same healthy attachment usable, and does not release an unowned gate.
- [ ] Queued caller cancellation remains cancellation and preserves its token.
- [ ] An executing stalled command terminates only its owned driver, completes
  within the operation plus cleanup budgets, and reaches `CdpOnly` or a
  diagnosable `Faulted` state.
- [ ] Lifecycle contention, including concurrent Stop/Dispose, has bounded
  completion and safe semaphore/resource ownership.
- [ ] A failure from an old epoch cannot terminate or invalidate a new attachment.
- [ ] Operation identifiers, health snapshots, original errors, and cleanup
  failures survive the relevant public error paths.
- [ ] Core on net8.0/net10.0 and Legacy on net481/net8.0/net10.0 satisfy the same
  supported timeout and cancellation contract.

After implementing the regressions and fix, run the targeted tests and
`dotnet build BrowserDock.slnx -m:1`. Record actual runtime results, including
skips, before marking the PR ready. Align the final behavior with the
[shared compatibility contract](seleniumbase-compatibility.md#shared-net-contract)
and [architecture cancellation rules](architecture.md#11-timeouts-and-cancellation).


## Portable implementation and validation

`CommandDeadlineTests` contains 32 cases shared by the Core and Framework test projects. It
runs the real Browser command path through Core and Legacy guarded commands,
using an in-memory W3C executor and a real owned fixture-host process. It does
not start Chrome, perform Windows listener attribution, or establish real
ChromeDriver/Chrome/CDP health. An injected probe isolates the command contract.
Tests use entry/release barriers and an outer 15-second watchdog. They cover
queue expiration and caller-token preservation, stale queued leases, admitted
stalls, lifecycle contention, probe expiration, old-epoch replacement, admission
invalidation ordering, concurrent Stop/Dispose, cancellation precedence, and
ordinary argument failures. Review regressions also cover caller cancellation and
deadline expiration after admission and while a worker is waiting to start,
with zero dispatch and reuse of the same attachment. Cleanup regressions inject
executor-disposal and process-termination failures, verify that independent
cleanup continues, and retry incomplete steps through concurrent cleanup and
Stop/Dispose without repeating successful disposal.

Command failure classification occurs before recovery. If caller and browser
lifetime cancellation are both signaled at that point, caller cancellation wins.
Cancellation stays an `OperationCanceledException`, with the selected original
token and original failure as its inner exception. Its `Data` contains
`OperationId` (`Guid`), `Diagnostic` (Core `BrowserHealthSnapshot`), and
`CleanupFailures` (`string[]`). Legacy passes this cancellation and metadata
through. Typed timeout/driver errors retain their existing Core/Legacy models.

Failure invalidates admission before releasing the command gate. Recovery starts
one ten-second budget before waiting for lifecycle ownership; it disposes the
captured attachment and only updates shared state for the same identity and
closed epoch. Gate-budget exhaustion records incomplete recovery, closes reuse,
and marks the still-current attachment Faulted without overwriting Stop/Dispose
or a replacement. Attachment cancellation and disposal share one executor/process
owner. A wait expiring does not abandon ownership of already-started disposal:
a later Stop/Dispose joins that same task, and pending disposal remains observed.
A completed failed task permits a new cleanup attempt. Successful termination,
executor disposal, exit draining, driver disposal and handle disposal are tracked
separately; only incomplete steps are retried. A process handle remains owned
when termination fails while the process is still alive.
This is not evidence that an unresponsive OS/resource always exits by the deadline.

Current local verification:

- Python comparator tests: 5 passed.
- Python release/tooling tests: 5 passed.
- `git diff --check`: passed.
- Cloud now has .NET SDK 10.0.401 and .NET 8 runtime 8.0.31 installed under
  `/workspace/.dotnet`. Commands need network-enabled execution for restore; the
  earlier proxy error came from execution-sandbox restrictions, not proxy failure.
  Local review-fix build and test outcomes are recorded below.
- GitHub common CI: passed for implementation commit
  `16136005b17a289275f090b737997a81d357d822`; see
  [Common contracts run 36792038812](https://github.com/mete0rfish/BrowserDock/actions/runs/36792038812).
  Every solution build reported zero warnings and errors. The final secret scan
  also passed: [run 36792038784](https://github.com/mete0rfish/BrowserDock/actions/runs/36792038784).
- Windows browser acceptance: deferred to the user's separate run. Local net481
  execution is unavailable on Linux; actual Framework runtime checks use common CI.

Do not mark the original browser acceptance checklist complete from these portable
tests. The actual ChromeDriver stalled-command fixture, exact Windows process
ownership, real Chrome/CDP health, and supported browser/runtime outcomes still
require the separate Windows run. Keep this PR as a draft until that evidence is
recorded; this change does not close #27 by itself.


Previous common-CI outcomes for implementation commit `16136005b17a289275f090b737997a81d357d822` (2026-10-01, Asia/Seoul):

| Host/runtime | Core tests | Framework/Legacy tests |
| --- | --- | --- |
| Ubuntu / net8.0 | 75 passed, 0 skipped | 49 passed, 0 skipped |
| Ubuntu / net10.0 | 75 passed, 0 skipped | 49 passed, 0 skipped |
| Windows hosted CI / net8.0 | 74 passed, 1 skipped | 49 passed, 0 skipped |
| Windows hosted CI / net10.0 | 74 passed, 1 skipped | 49 passed, 0 skipped |
| Windows hosted CI / net481 | Not a Core test-project target | 49 passed, 0 skipped |

The Windows-hosted skip is the pre-existing `RejectsReparsePointOwnedPaths`
symlink-privilege test. All 22 command-regression cases executed in every listed
test-project/runtime run. Hosted common CI excludes `TestCategory=Windows`; it
is separate from the deferred interactive Windows 11 browser acceptance.
The net481 job also passed release packing, the classic C# 7.3 package consumer,
and Framework sample loading. No Windows-browser workflow was dispatched.

The health snapshot is marked serializable because .NET Framework validates
values assigned to `Exception.Data`; that compatibility fix was exercised by the
same cancellation tests on net481. The previous validation commit above precedes
the code-review fixes; its results
do not validate those fixes. New Cloud and common-CI evidence must be recorded
for the updated source.

## Code-review fix validation (2026-10-01, UTC)

- `dotnet build BrowserDock.slnx --no-restore -m:1`: passed, zero warnings/errors,
  using SDK 10.0.401 in the Codex Cloud Linux workspace.
- Core tests: 85 passed on net8.0 and 85 passed on net10.0.
- Framework/Legacy tests: 59 passed on net8.0 and 59 passed on net10.0.
- All 288 local tests passed with zero failures/skips. Each project/runtime ran
  all 32 command regressions. The local TRX results were inspected for actual
  execution and outcomes. Runs excluded `TestCategory=Windows`.
- `git diff --check`: passed.

Execution start and cancellation arbitrate one atomic state. Cancellation that
wins before execution retains the attachment, and the canceled queued worker
cannot submit its action later. Failed cleanup attempts retain incomplete-step
ownership; concurrent retries share one pending attempt and do not repeat steps
that already completed successfully. These portable regressions retain the
Windows-browser acceptance limits above.
