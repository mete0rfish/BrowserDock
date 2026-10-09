# Issue #41 reproduction and coverage-restoration plan

Status: planned, 2026-10-09. No new browser execution is claimed here.
See [navigation diagnostics](issue-41-diagnostics.md), [CI routes](ci.md),
and the [test plan](test-plan.md). #51's browser-free net481 failure is separate.

## Baseline and independent questions

Start from main `11fcefca74cfa0ab8d5c0c6fe59524fc61e96810` with pinned Chrome for
Testing/ChromeDriver 154.0.8037.57. Compare an eventual correction against this
baseline using the same OS/session/binaries. Keep historical artifacts from
36943306004 (both attempts), 36991863246 and 37093628704 (both attempts).

| Track | Observation to distinguish |
|---|---|
| Initial/redirect navigation | Which requested Document/session/frame/loader completed or failed; unrelated/provisional events must not decide success |
| Profile cleanup | Owned process-tree exit, retained filesystem error and actual lock-owner evidence; a deletion error alone does not identify the owner |
| Nested frames | Whether failure occurs before frame assertions, or during frame reacquisition after reconnect |
| Immediate client redirect | Requested page versus immediate location.replace; keep separate from server redirect/request-correlation corrections |

## Phase A: observe current coverage

Run normal and quarantined suites separately. On an investigation PR,
ci:browser selects normal hosted coverage and ci:quarantined selects the existing
Core-only quarantined case. Preserve first-failure behavior. Record unexecuted
later runtimes as not run. ci:required does not execute a real browser.

For a prepared Windows 11 x64 interactive fixture, use PowerShell 7 and the
existing runner. Supply paths to the pinned executables and fresh output locations:

```powershell
./scripts/test-windows.ps1 -Chrome $env:BROWSERDOCK_CHROME -Driver $env:BROWSERDOCK_DRIVER -Results ('.artifacts/investigations/41/normal-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
./scripts/test-windows.ps1 -Chrome $env:BROWSERDOCK_CHROME -Driver $env:BROWSERDOCK_DRIVER -QuarantinedOnly -Results ('.artifacts/investigations/41/quarantine-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
```

Execute these as separate runs; stop and inspect a failure before starting another.
The quarantine manifest intentionally has no Legacy run. Do not report it as
restoration of six removed cases or a full runtime pass.

## Phase B: restore diagnostic access to removed cases

On a dedicated #41 branch, recover exactly these cases from PR #39's removal diff,
preserving original expectations and integrating current diagnostics:

- Core SharedScenarioMatchesManifest: SB-03 and SB-04.
- Core AC06_StartupCancellationRollsBack: driver-start and session-created.
- Core CallerProfileAndCanceledStopPreserveSentinelAndReleaseLock.
- Legacy FacadeDetachedNavigationSupportsRedirectsAndSameDocument: DOMContentLoaded.

Keep the currently quarantined nested-frame case separate. Verify discovery counts
before execution; missing restored tests are failures of this diagnostic setup.
The current main normal runner cannot execute removed cases until this restoration
change exists. Keep these trials distinct from acceptance results.

For a first failure, retain TRX, fixture.json, executable/archive hashes, HTTP
start/end records, Navigation.Trace and cleanup diagnostics. Align UTC/relative
timings and session/target/frame/loader/request IDs. Target.targetDestroyed alone
does not identify the affected target. Navigation completion alone does not prove
that the expected page was reached.

Investigate file locks only within disposable, library-owned fixture profiles.
Use a reviewed local lock-owner probe if current evidence cannot identify the
owner; record process identity and the relevant file relation. Do not change
termination/deletion ownership rules or force-delete a caller profile to collect
evidence. Do not publish raw profile contents or unrelated process details.

## Phase C: distinguish and correct

Reproduce one symptom with a controlled barrier or event sequence. Vary one cause
at a time: provisional/server redirect, observer detach, delayed target/page
appearance, cancellation stage, or confirmed lingering profile owner. If the
historical trigger cannot be reproduced, record that limitation and keep #41 open.

Use a separate runtime PR for each demonstrated correction. Add the regression
that fails on baseline and passes on the correction, then compare normal and
affected diagnostic scenarios on the same pinned fixture. No automatic navigation
replay, test retries, deadline increases or conversion of errors to success.

Restore cases to normal coverage only with the issue's evidence requirements.
For nested-frame restoration, run at least three consecutive full normal suites
including that case on both Core runtimes at the same candidate/fixture. Preserve
all failures, even when a later run passes. Complete the remaining Core/Legacy
matrix for any claimed supported scope.

Hosted Windows Server results and interactive Windows 11 acceptance remain
distinct. Post-merge ci:windows11/ci:windows11:stress routes require the configured
runner and environment; this planning batch neither prepares that runner nor
requests those runs. Stress, Stable-1 and SeleniumBase parity retain separate gates.
