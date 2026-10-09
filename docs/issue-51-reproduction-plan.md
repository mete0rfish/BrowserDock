# Issue #51 reproduction plan

Status: planned, 2026-10-09. No new test execution is claimed here.
See [existing diagnostics](net481-cleanup-diagnostics.md) and
[the first-batch decision](work-batch-2026-10-09.md).

## Question and candidates

Historical failures occur in net481 common fixtures without Chrome. PR #52
observed attachment/process cleanup complete while Browser.StopAsync still timed
out. A blocked CDP cleanup boundary is a hypothesis, not an established cause.

| Candidate | Use |
|---|---|
| `881fdaa15377cb396078920a0ed2606800f82d42` | PR #50 historical failure/rerun reference; keep old artifacts |
| `a71a9767ab36cfc688fdff3b3100512225e0b252` | Nine instrumented net481 failures; process/attachment already complete |
| `11fcefca74cfa0ab8d5c0c6fe59524fc61e96810` | Current main; expanded Browser/CDP snapshots |
| `f3dd6f932b44a66ff5d6faa4ad02d0d6b349ce15` | PR #53; thread collector and cleanup schedules |

Do not mix candidate results or interpret servicing/image differences as causes
without a controlled comparison. Historical failing and #53 passing runs report
the same Windows image and Framework Release 533509.

## Preparation

Use a clean checkout of the selected SHA, .NET SDK from global.json, .NET 10
runtime for the collector/fixture host, and actual Framework 4.8.1 on Windows x64.
Build the whole solution before using --no-build. Record SHA/tree status,
dotnet --info, OS/image, Framework Release, configuration, exact filter and command.
Use a fresh result directory for every execution; never overwrite an attempt.

On #53, set BROWSERDOCK_CLEANUP_EVIDENCE to that result directory. Keep the
collector enabled for net481 and preserve both successful output and collection
failure metadata. Do not launch Chrome or download browser binaries for #51.

## Bounded runs

1. Validate the collector with CollectorFindsBlockedFrameworkThreadWithoutReadingLocals.
2. Run the four CdpCleanupRaceTests cases, then CommandDeadlineTests' process-only
   and bound-CDP repetitions. Each case retains its own existing watchdog.
3. Run the full net481 common suite to retain fixture interaction/order effects.
4. Start with three independently numbered executions of each group. Stop on the
   first failure and inspect the evidence before requesting another execution.
   Three passes mean "not reproduced", not "fixed"; extend the experiment only
   when a specific hypothesis justifies it.

Example for a clean #53 checkout after restore/build, in PowerShell 7:

```powershell
$resultDir = Join-Path $PWD ('.artifacts/investigations/51/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $resultDir | Out-Null
$env:BROWSERDOCK_CLEANUP_EVIDENCE = $resultDir
dotnet test tests/BrowserDock.FrameworkTests -f net481 --no-build --no-restore --filter 'FullyQualifiedName~CdpCleanupRaceTests' --logger 'trx;LogFileName=cdp-races.trx' --results-directory $resultDir
if ($LASTEXITCODE -ne 0) { throw 'Retain TRX and stack evidence; inspect before continuing.' }
./scripts/assert-test-results.ps1 -Path (Join-Path $resultDir 'cdp-races.trx') -RequiredClasses 'BrowserDock.Tests.CdpCleanupRaceTests'
```

For the full run use --filter 'TestCategory!=Windows', another fresh directory,
and assert-suite-results.ps1 -Suite Common -Project Legacy. Do not use the full
Common manifest on a focused subset. Re-request remote common CI with ci:required
only when needed; label retention does not run a new commit.

## Evidence and decision

Retain the primary stop error, later cleanup errors, Browser/CDP/attachment/process
traces, task/gate states, timing, stack JSON, TRX and exit code. Compare the last
entered boundary with managed frames and completed owners. Thread-pool counts
alone do not prove starvation. Collector failure must not hide the original error.

- If disposal stalls before returning a task, correlate the observed worker's
  frames with native cancellation/abort boundaries.
- If a reader/dispatcher or gate remains pending, design a deterministic schedule
  isolating that dependency; do not infer deadlock from an event name alone.
- If all runs pass, keep #51 open and record "not reproduced" with exact scope.

Closure requires a demonstrated cause, a focused regression failing before the
runtime correction, passing after it, and final supported common-matrix results.
Do not remove tests, relax assertions, raise deadlines or add automatic retries.
