# Owned-process cleanup investigation

[Issue #51](https://github.com/mete0rfish/BrowserDock/issues/51) tracks the
intermittent net481 common-contract failures observed on PR #50. The first
execution failed 16 cases during fixture disposal; the same source passed all
441 cases when only the failed job was rerun. These fixtures do not launch Chrome.
The original failure's cause remains unconfirmed.

Failed attachment cleanup waits now retain these string-valued exception entries:

- `Cleanup.BrowserTrace` / `Cleanup.BrowserSnapshot`: final-cleanup stages,
  shared owner state, gate availability and diagnostic-worker state when a
  fixture's caller wait expires. The stage is recorded before its state snapshot.
- `Cleanup.CdpTrace` / `Cleanup.CdpSnapshot`: separate reader, dispatcher and
  disposal cancellation/abort boundaries and pump-task states.
- `Cleanup.AttachmentTrace`: bounded timestamps and start/end/failure stages for
  process termination, executor disposal, driver disposal and process-handle disposal.
- `Cleanup.AttachmentSnapshot`: shared cleanup-task state and completed cleanup steps.
- `Cleanup.ProcessTrace`: process-exit, output-drain, owned-tree and completion stages.
- `Cleanup.ProcessSnapshot`: owned PID/liveness, last wait stage, stdout/stderr task state
  and available thread-pool worker/I/O thread counts at capture time.

The trace excludes process paths, output content, script bodies and exception
messages. Snapshots are observations, not an atomic history or proof of a cause.
Thread-pool availability alone does not establish starvation. Cancellation still
bounds the caller's wait; the shared cleanup owner continues and can be joined by
a later Stop/Dispose operation. Failed command recovery copies the same evidence
into Core/Legacy exceptions. Existing cleanup budgets are unchanged.

The portable command fixture reports the original stop exception and any later
attachment-cleanup exceptions instead of letting its finally block replace the
primary error. TRX output includes the selected metadata from every owned attachment.

Deterministic barrier tests hold termination or executor disposal, cancel a waiter,
check the reported stage and caller token, release the owner and verify cleanup
completes once. A separate test repeats owned-process cleanup 20 times and checks
process exit and both output drains. These run in common CI, including net481.
A passing repetition does not resolve the historical failure.

The failure reproduced in PR #52 with all attachment/process steps already
complete; nine net481 cases timed out waiting for Browser.StopAsync in fixtures
with CDP connections. A documentation-only candidate then passed net481 without
a runtime fix. Browser/CDP boundary evidence and a separate 40-iteration bound-CDP
cleanup test narrow the investigation; the root cause is not established yet.

A separate Windows net10 Legacy run failed before attachment creation with a
sharing violation when reading an already published fixture PID file. Publication
already uses a closed temporary file followed by rename. The fixture now polls
only sharing/lock violations (Win32 errors 32/33) within the existing startup
token and budget; other I/O errors, owner exit and PID mismatch still fail.
This retries a readiness observation, not a test or a side-effecting command,
and is not presented as a fix for the final-cleanup timeout.

Use `ci:required` on the PR to run common contracts and Secret scan. If a run fails,
retain that attempt and inspect its TRX metadata before choosing a fix. Do not
remove the case, increase timeouts or treat a passing rerun as root-cause evidence.
Actual Windows browser verification and #41's browser failures are separate.

## Concurrent CDP termination investigation

`CdpCleanupRaceTests` races transport interruption with disposal while a receive
is pending, repeating 128 fresh connections against one local CDP fixture server.
It preserves the 15-second caller watchdog and checks that requests, pumps,
subscriptions and the socket are gone. A late injected failure after completed
disposal is allowed; incomplete disposal or other failures are retained.
Another 128-connection case holds an unanswered command and an event callback,
then releases two interruption callers, disposal and a dispatcher exception
together. This covers active dispatch and real message traffic as well as an
idle receive; a passing bounded stress run does not prove absence of a race.

The net481 common job sets `BROWSERDOCK_CLEANUP_EVIDENCE` to its result directory.
When a fixture stop or the CDP race exceeds the existing watchdog, a separate
.NET 10 process snapshots the owned test host with pinned ClrMD 3.1.512801 and
writes managed thread IDs and method signatures to `cleanup-stacks-*.json`.
No addresses, arguments, locals, environment values, memory dumps or heap data
are emitted or uploaded. Collection has its own bounded diagnostic wait and
cannot replace the primary timeout. The collector disposes its temporary snapshot.
A net481 smoke test proves that a deliberately blocked managed thread appears.

Read `Cleanup.CdpTrace` together with the method stacks. A `*-cancel-start` or
`*-abort-start` without its corresponding end can distinguish synchronous
termination from the later pump join. `WaitAsync` only bounds the task after
`DisposeAsync` returns it; a synchronous cancellation/abort stall before its first
await needs its own demonstrated cause. Neither the stress test nor a passing
collector check establishes the historical failure's cause without captured
failure evidence. Existing test selections and cleanup deadlines are unchanged.
