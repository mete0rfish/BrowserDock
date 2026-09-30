# Issue 27 command timeout and cancellation plan

This draft describes the proposed fix for [issue 27](https://github.com/mete0rfish/BrowserDock/issues/27).
Queued commands must share the operation deadline, report timeout separately from
caller cancellation, and leave a healthy attachment intact if execution never
started. Failed commands need bounded recovery that cannot remove a newer
attachment. Implementation and acceptance tests are pending.

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

## Proposed implementation

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
3. Only initiate failed-command recovery when execution was admitted and the
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
