# Implementation and verification record

## Current verification summary (documentation update, 2026-09-24)

This table summarizes existing execution records. Tests were not rerun for this documentation update; these are not results for the current checkout or release candidate. Dated failures and unexecuted checks below are retained as diagnostic history.

| Area | Latest record and remaining verification |
|---|---|
| Normal Windows suite | Windows 11 x64 build 26100, Chrome/CfT and ChromeDriver 154.0.8037.57: 258 passed, zero failures/skips |
| Runtime results | Core net8.0/net10.0: 81 each; Legacy net481/net8.0/net10.0: 32 each |
| Solution build | Zero warnings/errors in that Windows record |
| Stress/privileged tests | Stress and privileged reparse-point tests unverified |
| Additional browsers | Stable-1 and SeleniumBase Python browser comparisons unverified |
| Real binary patches | No supplied/verified recipes; separate from synthetic fixture tests |
| Release approval | CI, Windows Stress, packages, consumers, and Source Link must be verified on the same candidate commit |

See [Windows connection regression verification](#2026-09-24-windows-11-connection-regression-verification) for environments and artifact paths.

## 2026-09-24 License and operational identity confirmed

The owner confirmed MIT and copyright holder/package author `mete0rfish`.
Private security and conduct reports go to `sungwonyoon326@gmail.com`.
The root LICENSE, package metadata, README, and operational documents were updated.
The GitHub private vulnerability reporting API returned 404, so activation was not confirmed.
This resolves the pending license/contact status in historical entries below.
Repository visibility, NuGet publication, and a complete provenance/dependency license review remain separate work.

## 2026-09-12 Rename to BrowserDock

The pre-publication working name changed to `BrowserDock`. The solution, source/test paths,
assembly and NuGet IDs (`BrowserDock`, `BrowserDock.Legacy`), namespaces, documentation,
CI, and release validators changed together. The public entry type is `Browser`,
the exception base is `BrowserDockException`, and the command facade is `IBrowserCommands`.
Profile/patch-cache markers and Windows test environment variables use `browserdock` and
`BROWSERDOCK_*`. No compatibility aliases were added because no public release existed.

Verification environment: macOS arm64, .NET SDK 10.0.103/runtime 10.0.3.

- Full `BrowserDock.slnx` Release build passed with zero warnings/errors.
- .NET 10 common tests: Core 44 and Legacy 22 passed, zero skips.
- Five SeleniumBase comparison-validator tests and five release-validator tests passed.
- Generated and inspected `BrowserDock.0.2.0-alpha.1` and `BrowserDock.Legacy.0.2.0-alpha.1` nupkg/snupkg pairs; Legacy depends on the matching Core version.
- Consumers without ProjectReference built for net8/net10 and loaded both assemblies on net10. No .NET 8 runtime was installed, so net8 execution was not performed.
- MSBuild evaluation confirmed the C# 7.3 classic consumer references the Legacy alpha version. Actual Windows/Framework 4.8.1 execution remained pending.
- Repository files were checked for the old working name, public types, and environment variables. The parent checkout's local path is outside repository contents and was not renamed.

Results and packages are in `.artifacts/browserdock-rename/`. Actual Windows Chrome tests and GitHub workflows were not run during the rename.

## 2026-09-12 Open-source preparation

Added the English README with a link to the then-Korean guide, CONTRIBUTING, SECURITY,
CODE_OF_CONDUCT, CHANGELOG, issue/PR templates, and Dependabot. CI Actions were pinned to
SHAs verified against official repositories, with read-only default permissions. Windows
tests reference an approval environment; its protection rules require separate GitHub setup.

Both packages and consumer tests use `0.2.0-alpha.1` from `build/Version.props`.
Added a NuGet README, shared metadata, portable symbols, package/release validators,
and candidate/approved-release workflows. Publishing is disabled by default and requires
account setup, same-commit Windows Stress results, required execution tests, and public
metadata checks. See the [release procedure](releasing.md) for manual prerequisites.

Verification environment: macOS arm64, .NET SDK 10.0.103/runtime 10.0.3.

- Full Release build: zero warnings/errors.
- .NET 10 common tests: Core 44 and Legacy 22 passed, zero skips.
- Five Python comparison-validator and five release-validator tests passed, including rejection of wrong commits, failed runs, evidence from another repository, missing TFMs/symbols, wrong dependencies, extra executables, and relative README links.
- Both alpha nupkg/snupkg pairs were generated and inspected. The public-release check failed as expected because author metadata was undecided.
- Separate package-only consumers built on net8/net10 and executed/loaded both libraries on net10. MSBuild evaluation confirmed the classic consumer uses the same alpha version.
- GitHub YAML syntax and `git diff --check` passed. This does not mean the workflows ran on GitHub.
- A scan of 85 working files and 99 Git blobs reachable from all local refs found no private-key, GitHub/Slack-token, or AWS-access-ID patterns. Results: `.artifacts/open-source/secret-pattern-scan.json`. This limited scan does not replace privacy, provenance, unstructured-secret, or remote-only-history audits.

Results and local packages are in `.artifacts/open-source/`. This environment did not run
actual Windows Chrome, net8/net481 runtime or classic consumer execution, PowerShell/GitHub
workflows, OIDC publication, or public-commit Source Link resolution. At that time the license,
rights holder, and private contact awaited owner confirmation. The origin remote was set to
`https://github.com/mete0rfish/BrowserDock.git`, with the project URL in package metadata.
LICENSE creation, visibility changes, GitHub protection configuration, and NuGet publication
had not yet been performed.

## 2026-09-11 Follow-up test implementation verification

The version file and Driver/UC sources at SeleniumBase commit `4ee7dfc4ae83c19385f5ac129f2cda0cfa863d80` were inspected directly. The comparison harness uses version 4.53.7. Added the [test plan](test-plan.md) and [comparison runner guide](../tests/seleniumbase-reference/README.md).

- Implemented shared HTML fixtures, an SB-01–04 manifest, NUnit reference tests, Python SeleniumBase tests, a sequential runner, and a JSON comparator. Both observations are checked against expectations; missing data, wrong implementation/scenario IDs, unchanged sessions, and cleanup failures are rejected.
- Replaced an arbitrary 1 ms Windows-test delay with an admission barrier and verified remote call counts. Strengthened stress categorization, shutdown timing, and CDP pending/worker/subscription/socket and Chrome Job Object accounting.
- Added a real child-process fixture with a stalled W3C new-session response, reconnect timeout, active-command/disconnect races, caller cancellation during cleanup, nested iframe reacquisition, and 100 CDP/WD target create/select/close cycles.
- Common tests cover pending/failed/completed network requests, other-page-session events, duplicate scripts, CDC name validation/recovery, and delayed/overflowing/throwing loggers. Corrected a Legacy input assertion from the `value` HTML attribute to the live DOM property.
- Added net8.0 to FrameworkTests. The Windows script runs Core net8/net10 and Legacy net481/net8/net10, rejecting missing fixtures, zero tests, skips, and nonpassing TRX results. It preserves older results. Added a manual interactive Windows 11 self-hosted workflow; runner registration and actual execution remained separate.

Verification environment: macOS arm64, .NET SDK 10.0.103/runtime 10.0.3.

- `dotnet build BrowserDock.slnx --no-restore --disable-build-servers -m:1`: all targets built, zero warnings/errors.
- .NET 10 common tests: **44 Core passed**, **22 Legacy/Framework passed**, zero skips. Results: `.artifacts/test-implementation/verified-core.trx`, `verified-legacy.trx`.
- Python comparator: **five tests passed**; runner/reference Python syntax compilation passed.
- Windows tests were discovered and compiled only. Actual Chrome/SeleniumBase comparison, Stress, net481/net8 runtime execution, PowerShell, and the self-hosted workflow were not run. Building the added net8 target after restore is not a net8 execution pass.

Remaining work included actual Windows results and the full source/runtime matrix, individual file-handle attribution, cross-process profile/cache contention, all fault points, and real binary recipes. Internal accounting covers tracked tasks/subscriptions/sockets and the Chrome Job Object; it is not an OS-wide leak analysis.

The following entries preserve earlier implementation and execution history.

Baseline: `spec.md` sections 18.1/18.3 and `architecture.md`. The shared engine and modern API live in `src/BrowserDock`, with Hosting/CDP/WebDriver/Patching/Diagnostics boundaries. `src/BrowserDock.Legacy` provides the C# 7.3 Task API. Both target `net481;net8.0;net10.0`. Tests in `BrowserDock.Tests` and `BrowserDock.FrameworkTests` distinguish common tests from `Windows`/`Stress` categories.

## Implemented contracts

- Assign Chrome to a Windows Job Object **while suspended, before execution**, preventing child processes from escaping ownership tracking during startup. ChromeDriver has separate PID ownership.
- Temporary-profile nonce markers, exclusive caller-profile locks, rejection of default profiles/reparse points, and loopback DevTools discovery.
- Chrome/driver build matching, opt-in copy-on-write patches, and atomic cache validation. No built-in real binary recipes.
- Separate CDP browser/page sessions, an event registry, numeric request multiplexing, method checks, one recovery attempt on the same endpoint, and script re-registration.
- Create sessions with public `RemoteWebDriver(ICommandExecutor, ICapabilities)` and intercept Quit in the executor. Element IDs come from public W3C responses, without private Selenium property access.
- Reconcile W3C sessions and CDP targets with a random temporary JavaScript marker observed on both sides, then remove it. Do not rely on window ordering or internal handle formats.
- Serialized lifecycle, command admission, epoch invalidation, session generations, cancellation, and bounded cleanup. Reject raw Selenium objects in JSON script results.

## Acceptance-criteria tracking

This table records the initial implementation state. See the current summary above for subsequent results.

| Criterion | Tests provided | Initial execution status |
|---|---|---|
| AC-01 Preserve Chrome | 30-second disconnection; original PID/create time/CDP; driver-port closure | Windows execution pending |
| AC-02 Session recovery | New session ID; preserved profile/endpoint/storage/DOM; click | Windows execution pending |
| AC-03 Stale references | Admission races; Windows lease/element remote counts; DOM stale | Report common/Windows results separately |
| AC-04 Leaks | Five warm-ups, 100 cycles, PID/port/profile/handle/thread/private-byte measurements | `-Stress` pending |
| AC-05 Concurrency | 20 instances, profile locks, synthetic patch-cache concurrency | Report common/Windows results separately |
| AC-06 Fault/cancel | Startup cancellation, driver/Chrome crash, CDP socket loss | Report common/Windows results separately |
| AC-07 Attached limitations | Record resize support/unsupported result and browser version | Windows execution pending |
| AC-08 Patching | Synthetic match/hash/idempotency/tampering/original-preservation tests | Real Stable/Stable-1 recipes pending |
| AC-09 Concurrent CDP/WD | Repeated lifecycle, explicit target reconciliation, CDP recovery, scripts | Windows execution pending |
| AC-10 Product claims | Detection sites excluded from tests/release gates | Documented |

## Verification environment and remaining release checks

The historical development environment was macOS arm64 with .NET SDK 10.0.103. It could build Framework 4.8.1 and .NET 8/10, but only .NET 10 was installed for execution. Framework and .NET 8 execution required another environment. Skipped Windows tests are not browser compatibility passes.

Local verification on 2026-09-09:

- .NET 8/10 builds passed without warnings/errors.
- 34 .NET 10 unit and HTTP/WebSocket contract tests passed.
- 22 Windows tests skipped due to OS/fixture conditions; no actual browser execution claim.
- Release package generated: `.artifacts/packages/BrowserDock.0.1.0.nupkg`; not published.
- Detailed results: generated `tests/BrowserDock.Tests/TestResults/common.trx`.

Local Framework 4.8.1 extension verification on 2026-09-10:

- Recorded the original MVP at `main` commit `91a3685`; implemented on `feature/net481-support`.
- Debug/Release solution builds passed with zero warnings/errors, covering Core/Legacy `net481;net8.0;net10.0`, Framework tests `net481;net10.0`, and the C# 7.3/x64 sample.
- .NET 10 Release: 34 existing and 21 new compatibility tests passed. The 22 existing and 12 new Windows-only tests skipped. These were not net481 execution results.
- New common tests cover timeout/cancellation/error propagation/UI context, process-exit waits, Windows argument escaping, option/script input copying, Legacy boundaries, real HTTP Selenium construction/find/detach, and CDP response ordering/socket loss/navigation/recovery.
- New Windows tests cover Legacy lifecycle, stale/reacquire, cookies/storage/DOM/iframes, navigation, startup cancellation, profile/PID/port cleanup, 100 cycles, and 20 parallel instances. Execution remained pending.
- Generated `BrowserDock.0.2.0.nupkg` and `BrowserDock.Legacy.0.2.0.nupkg` in `.artifacts/packages`. Verified DLLs/dependency groups for three TFMs and exclusion of build reference assemblies from runtime dependencies. Not published.
- Verified generated binding redirects for Framework samples/tests. Installed both packages in a separate SDK-style C# 7.3/net481 consumer without ProjectReference and built without warnings/errors. Confirmed net481 library DLLs and Selenium's net462 DLL selection. Also prepared a classic Windows consumer.
- Ordinary macOS MSBuild could not resolve NuGet compile references for the classic project. A Windows Visual Studio/MSBuild CI step was configured, without claiming a pass at that point.
- Generated results: `tests/BrowserDock.Tests/TestResults/regression-release.trx`, `tests/BrowserDock.FrameworkTests/TestResults/framework-release.trx`.

Framework CI checks installed runtime Release `>= 533320` and runs net481 common tests plus the C# 7.3 consumer. Adding CI configuration did not verify remote execution in that local task. Only the test server runs as a separate .NET 10 process; it is not a Framework product dependency.

Run `scripts/test-windows.ps1` for Windows Stable and Stable-1 Chrome/CfT pairs and preserve hashes, versions, and TRX files. Hosted Windows common tests do not replace interactive Windows 11 acceptance.

Tests also compare ChromeDriver detach/DELETE-session combinations and navigation/reconnect cancellation. Before release, verify real recipes, all browser fault points, background tasks, and individual file-handle leaks with Windows evidence. Synthetic patch-engine passes do not establish executable compatibility.

If initial `StartAsync()` fails, no browser instance can be returned: clean up owned resources even if an intermediate CDP-only state is healthy, then throw. Reconnect failure on a returned instance leaves `CdpOnly` or `Faulted` according to Chrome/CDP health.

Default logs exclude sensitive payloads. A user logging sink that never returns cannot be forcibly stopped reliably; report cleanup failure after the bounded wait.

Before public release, confirm licensing and dependency notices. Follow the specification's date-based policy to update TFMs and CI after .NET 8 support ends.

## 2026-09-24 Windows 11 connection regression verification

Environment: Windows 11 x64 build 26100, PowerShell 7.6.6, .NET SDK 10.0.401, runtimes 8.0.31/10.0.12, Framework Release 533320, and Chrome for Testing/ChromeDriver 154.0.8037.57.

- Fixed an IPv4-only port check missing ChromeDriver's `::` listener. Driver readiness now requires an IPv4 loopback HTTP response plus PID/port ownership. Chrome CDP retains strict loopback checks. IPv6 layout follows [Windows MIB_TCP6ROW_OWNER_PID](https://learn.microsoft.com/en-us/windows/win32/api/tcpmib/ns-tcpmib-mib_tcp6row_owner_pid).
- Omitted the `detach` capability rejected for external Chrome attachment. Common protocol tests use the product options factory to verify debugger address, omitted detach, and blocked DELETE-session.
- Increased the port-closure wait from one to five seconds. Require `ConnectionRefused`, not an arbitrary socket error; timeout still fails.
- Debug solution build: zero warnings/errors. Listener regressions: four passed each on net8/net10. Core attach/30-second disconnect/reconnect passed in the standalone net10 and full net8 runs.
- Common tests: Core net8/net10 passed 43 each; Legacy net481/net8/net10 passed 22 each; 152 total.
- First normal Windows target, Core net8: 72 passed, five failed, zero skips. All failures were `StartAsync` `AmbiguousTarget` errors in SB-04, cleanup cancellation, and three detach-comparison cases. The port/options fix did not by itself pass the full acceptance suite.
- The runner stopped at the first failed TFM, so the full Core net10 and Legacy browser matrix did not run. Stress, Python SeleniumBase comparisons, and real patch recipes also did not run.

Artifacts: `.artifacts/windows-validation/20260924-020520-282/` contains `smoke.trx`, `listeners-*.trx`, `normal/net8.0.trx`, and `normal/fixture.json`; common results are in `.artifacts/common-validation/20260924-020800-228/`. Initial and intermediate failures were retained in timestamped directories.

### Initial-page creation race fix and revalidation

Follow-up diagnostics reproduced the remaining `AmbiguousTarget` failures. Startup observed an empty list before Chrome's first page appeared and called `Target.createTarget`; Chrome's original `about:blank` then appeared too. This sequence occurred in three of 15 consecutive two-browser startup diagnostics.

Startup now waits for the first page target within the existing `CdpConnect` timeout and cancellation token instead of creating a replacement tab. Actual multiple pages still require explicit selection. CDP recovery retains the controlled target. Single-page checks and selection use the same registry lock.

Regression tests cover no page on the first one/three queries, cancellation while waiting, and actual multiple pages. Two delayed-creation cases failed before the fix; all four passed afterward. Nine scenarios related to previous failures passed in two consecutive runs.

The normal `scripts/test-windows.ps1` run completed on the same Windows 11 and Chrome/ChromeDriver 154.0.8037.57 environment.

| Suite | Runtime | Passed | Failed | Skipped |
|---|---|---:|---:|---:|
| Core | net8.0 | 81 | 0 | 0 |
| Core | net10.0 | 81 | 0 | 0 |
| Legacy | net481 | 32 | 0 | 0 |
| Legacy | net8.0 | 32 | 0 | 0 |
| Legacy | net10.0 | 32 | 0 | 0 |

All 258 passed; every TRX also passed the runner's required-class, full-execution, and passing-result checks. The solution build had zero warnings/errors. In `.artifacts/startup-validation/20260924-022344-076/`, `round-1.trx` and `round-2.trx` record repeat verification; `normal/*.trx` and `normal/fixture.json` record the full normal suite and environment. Before/after protocol regressions are in `.artifacts/startup-race/before.trx` and `after.trx`.

This run excluded Stress, privileged reparse-point tests, Stable-1, Python SeleniumBase comparisons, and real binary patch recipes. It does not grant release approval for those areas.
