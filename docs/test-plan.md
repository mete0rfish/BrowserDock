# BrowserDock test plan using SeleniumBase as a reference

Written: 2026-09-11 · BrowserDock commit reviewed: `83a2fb4`

This is an **execution plan**, not a claim that the tests ran when it was written. Subsequent implementation and execution are distinguished below and in the [verification record](implementation.md). Priorities are **common regressions → real Windows lifecycle → SeleniumBase comparison → failure recovery/leaks → approval by supported version**.

## Subsequent implementation

Added pinned versions, a shared manifest, Python runner/comparator under `tests/seleniumbase-reference`, and .NET `ReferenceTests`. Rechecking SeleniumBase's version file and Driver/UC sources confirmed **4.53.7** at the reference commit. Follow the [runner guide](../tests/seleniumbase-reference/README.md).

Added admission synchronization/remote-call rejection, driver cleanup deadlines, a real process with stalled W3C session creation, reconnect timeout, in-flight-command/disconnect races, cancellation during cleanup, CDP task/socket/subscription cleanup, pending/failed/wrong-session network events, CDC script validation/deduplication, nested iframes, 100 CDP/WebDriver target-observation cycles, delayed/overflowing/throwing loggers, and Legacy net8 execution. The Windows runner explicitly includes/excludes Stress and rejects skipped required tests, zero tests, and missing TRX files. A manual Windows 11 workflow is provided.

The steps below preserve the original goals and additional verification criteria. Verify Windows browser execution, actual SeleniumBase comparisons, and PowerShell in the supported environment. Cross-process profile/cache contention, every fault point, individual handle attribution, and real patch recipes require further validation. Adding instrumentation is not an acceptance pass. Historical status tables below describe the plan's original baseline; use the implementation record for later results.

## 1. Comparison scope and evaluation

BrowserDock is an independent .NET implementation inspired by SeleniumBase UC Mode, not an identical framework/API. Compare Chrome-preserving detach/reattach, navigation, and state preservation. Final product acceptance follows [AC-01–10](spec.md#20-verifiable-acceptance-criteria).

The [official UC guide](https://seleniumbase.io/help_docs/uc_mode/) describes starting Chrome before attaching ChromeDriver, disconnecting as needed, and `disconnect()`, `connect()`, and `uc_open_with_reconnect()`. Use those behaviors as scenario starting points.

SeleniumBase examples provide pytest execution, parameterization, failure logs, and reports. Here NUnit/TRX serve those operational purposes; Python is only the comparison tool. Some upstream examples intentionally fail, so the entire examples directory's pass rate is not a product criterion. See the [official examples guide](https://seleniumbase.io/examples/ReadMe/).

| Scenario | SeleniumBase path | BrowserDock equivalent and observations |
|---|---|---|
| Normal navigation/DOM | UC `default_get()`, find/click/type | `NavigateAsync(Standard)` and lease commands; final URL/text/input |
| Disconnect, keep browser | `driver.disconnect()` | `DisconnectWebDriverAsync()`; same Chrome alive, driver stopped, CDP responds |
| Reconnect | `driver.connect()` / `reconnect()` | New session; cookies/storage/same-document DOM preserved |
| Detached navigation and recovery | `uc_open_with_reconnect()` | `Detached`, `ReconnectAfterNavigation=true`; final document/attachment |
| Remain disconnected | `uc_open_with_disconnect()` | `Detached`, `ReconnectAfterNavigation=false`; remain `CdpOnly` |
| Switch to CDP | `activate_cdp_mode()` | `EnterCdpOnlyAsync()`, then `NavigateAsync(CdpOnly)`/`ExecuteCdpAsync()` |
| Stop browser | Manager exit / `quit()` | `StopAsync()`/`DisposeAsync()` cleans owned resources |

This maps scenarios, not identical internal call order. SeleniumBase CDP Mode has separate CDP behavior and WebDriver-call switching; do not assume all BrowserDock CDP APIs correspond. See the [CDP Mode guide](https://seleniumbase.io/examples/cdp_mode/ReadMe/).

Test the following intentional differences against BrowserDock's own contract:

- Explicitly disconnect before `CdpOnly` navigation; do not expect implicit reconnect.
- Old leases/elements fail locally with `StaleAttachmentException` after disconnect starts. Python object reuse need not match.
- Ambiguous tabs produce `AmbiguousTargetException` and require selection, rather than choosing the last tab.
- Use `TargetPolicy.ReplaceControlled` separately when comparing new-tab UC navigation. Distinguish same-document preservation from document-replacing navigation.
- GUI input, CAPTCHA handling, headless mode, automatic driver downloads, and site-specific HTTP prechecks are outside the MVP. External-site observations are reference-only under AC-10.

The original reference SHA is `4ee7dfc4ae83c19385f5ac129f2cda0cfa863d80`. During the initial research, refetching that fixed source on the web failed, so existing specification evidence and current official documentation were kept distinct. Before building the harness, verify checkout availability and actual implementation. If unavailable, select an executable release/SHA and document why. See [reference repositories](spec.md#21-repositories-analyzed). Subsequent source verification is recorded above.

## 2. Assets and original verification baseline

| Area | Asset | Original assessment |
|---|---|---|
| Unit tests | `tests/BrowserDock.Tests/UnitTests.cs` | Admission races, versions, navigation combinations, patches, profiles |
| Protocol contracts | `ContractTests.cs`, `NavigationContractTests.cs` | Real HTTP/WebSocket W3C/CDP fixtures, stale rejection, loaders/recovery |
| Real Chrome | `tests/BrowserDock.Tests/WindowsTests.cs` | Lifecycle/navigation/tabs/cancel/crash/attach limits/stress implemented; execution pending at baseline |
| Legacy/Framework | `tests/BrowserDock.FrameworkTests` | Compatibility/protocol/Windows tests; net481 execution needed at baseline |
| Servers | Core `LocalServer`, `tests/BrowserDock.FixtureHost` | `/page`, `/frame`, `/redirect`, `/slow`, `/download` |
| Runner | `scripts/test-windows.ps1` | Version/hash/runtime metadata, per-TFM TRX, `-Stress` |
| CI | `.github/workflows/ci.yml` | Common tests and Windows net481 consumers; excludes actual Windows 11 Chrome acceptance |

The [historical implementation record](implementation.md) reports 34 existing common and 21 compatibility tests passing in .NET 10 Release on 2026-09-10, with 22 and 12 Windows tests skipped. These were not reruns for the original plan; remote CI was not checked in that task.

That development host had only .NET 10 installed. macOS can run common tests; actual Windows 11 x64 Chrome/net481 acceptance requires a separate environment.

## 3. Environment and version pins

The current runner includes the subsequently added Legacy net8 target:

| Layer | Environment / TFM | When |
|---|---|---|
| Common regressions | Ubuntu/Windows CI, Core and Legacy net8.0/net10.0 | Every PR |
| Framework common/consumer | Actual Framework 4.8.1, C# 7.3/x64, Windows MSBuild | Every PR |
| First browser smoke | Interactive Windows 11 x64, CfT Stable, Core net10.0 | First |
| Full browser matrix | Same environment; Core net8.0/net10.0, Legacy net481/net8.0/net10.0 | Regular regression/release |
| Browser-version matrix | Those five combinations × CfT Stable/Stable-1 | Release |
| Installed Chrome | Windows 11 x64 Chrome Stable with matching driver | Profile/endpoint/lifecycle before release |

Prepare PowerShell 7, .NET 10 SDK, .NET 8/10 runtimes, Framework 4.8.1, and Visual Studio Build Tools/MSBuild for consumers. The fixture server requires .NET 10 on the test host, not in the net481 product runtime.

Chrome/driver must be M115+ with matching `MAJOR.MINOR.BUILD`. Prefer identical full-version CfT pairs. Pin an available previous-milestone pair for Stable-1. Record official release information when preparing the fixture rather than guessing versions. See [ChromeDriver version selection](https://developer.chrome.com/docs/chromedriver/downloads/version-selection).

Record BrowserDock SHA, SeleniumBase SHA/package/Python dependency list, OS build, TFM, full browser/driver versions and SHA256, launch options, patch mode, and fixture version. SeleniumBase may modify a UC driver, so record vendor and executed artifact versions/hashes separately. Do not force both implementations to share a patched binary.

Each test uses its own profile/browser. Run ordinary browser tests serially; reserve 20-instance concurrency for Stress. Separate hosted Windows common-test passes from interactive Windows 11 browser acceptance.

## 4. Execution sequence

### Step 1 — Common regressions

Run from the repository root:

```sh
dotnet restore BrowserDock.slnx
dotnet build BrowserDock.slnx --no-restore -m:1
dotnet test tests/BrowserDock.Tests/BrowserDock.Tests.csproj -f net10.0 --no-build --no-restore --filter 'TestCategory!=Windows' --logger 'trx;LogFileName=core-net10.trx' --results-directory .artifacts/test-plan/common
dotnet test tests/BrowserDock.FrameworkTests/BrowserDock.FrameworkTests.csproj -f net10.0 --no-build --no-restore --filter 'TestCategory!=Windows' --logger 'trx;LogFileName=legacy-net10.trx' --results-directory .artifacts/test-plan/common
```

Check every exit code and stop on failure. Add net8.0 runs where installed and FrameworkTests net481 on actual Framework. All discovered required tests must pass, with no unexpected skips or zero-test runs.

### Step 2 — Core Windows smoke

After preparing Windows and building, start with the most important test:

```powershell
$env:BROWSERDOCK_CHROME = 'C:\Chrome\chrome.exe'
$env:BROWSERDOCK_DRIVER = 'C:\Chrome\chromedriver.exe'
$env:BROWSERDOCK_STRESS = '0'
dotnet test tests/BrowserDock.Tests/BrowserDock.Tests.csproj -f net10.0 --no-build --no-restore --filter 'FullyQualifiedName~AC01_02_03_DisconnectPreservesChromeAndReconnectReplacesSession' --logger 'trx;LogFileName=lifecycle-smoke.trx' --results-directory .artifacts/test-plan/smoke
```

It intentionally stays disconnected for 30 seconds, checking Chrome identity/profile/endpoint preservation, driver exit, new SessionId/generation, cookies/localStorage/DOM, and stale/reacquire. A skip is fixture failure. Resolve version/profile/attach/cleanup issues before expanding the suite.

### Step 3 — Full normal Windows suite

```powershell
./scripts/test-windows.ps1 -Chrome C:\Chrome\chrome.exe -Driver C:\Chrome\chromedriver.exe -Results .artifacts/test-plan/stable-normal
./scripts/test-windows.ps1 -Chrome C:\ChromePrevious\chrome.exe -Driver C:\ChromePrevious\chromedriver.exe -Results .artifacts/test-plan/stable-minus-one-normal
```

Each call runs Core net8.0/net10.0 and Legacy net481/net8.0/net10.0. The script stops at failure; record later TFMs without results as not run. Preserve TRX and `fixture.json` using separate directories for repeats.

### Step 4 — Compare the same local pages with SeleniumBase

**The Python harness and shared manifest are implemented.** `run.py` passes FixtureHost's startup `BROWSERDOCK_FIXTURE_URL` to both runners. Shared `/reference/*` HTML avoids differences between older Core/Framework pages.

1. Pin a verified SeleniumBase SHA/release and dependencies in a venv; verify Chrome and the executed driver artifact.
2. Use identical origins, HTML, initial storage, and window conditions, with separate profiles and sequential execution.
3. Start with SB-01 navigation/button/input/iframe, SB-02 same-document disconnect/connect, SB-03 detached navigation/reconnect, and SB-04 CDP-only navigation.
4. SB-02 checks preserved Chrome and cookies/storage/DOM with session replacement. SB-03 checks final URL/title/elements/storage after document replacement without requiring the previous DOM marker.
5. Add ReplaceControlled and multiple-tab cases separately. Record explicit target/stale errors as BrowserDock-specific expectations.
6. Wait for element/URL/lifecycle conditions rather than fixed sleeps. Measure deliberate disconnection intervals with a monotonic timer. Python reconnect waits and .NET `ReconnectDelay` need not have identical internal ordering.
7. Run each scenario at least three times with fresh profiles. Store JSON observations and preserve the first failure even if a repeat passes.

The result schema includes `scenarioId`, implementation/version, mode, final URL/title/fixture values, before/after Chrome identity, session replacement, target, error category, elapsed time, and cleanup. Compare **relationships within each run**, not identical raw PIDs/SessionIds between implementations. BrowserDock generation/epoch are separate fields.

Store pytest JUnit XML/failure logs and NUnit TRX. Check actual DOM commands, CDP responses, and process liveness, not just process exit or connected flags. If only SeleniumBase fails, investigate upstream/environment; if only BrowserDock fails, first distinguish shared contracts from intentional differences.

### Step 5 — Failure and boundary coverage

| Priority | Check | Pass condition |
|---|---|---|
| P0 | AC-01 driver exit, AC-03 admission synchronization | Driver exits within 5 s; wait for admission closure instead of `Task.Delay(1)`, then prove stale rejection with zero remote calls |
| P0 | Stalled new-session, reconnect timeout, cleanup cancellation | Typed stage errors, unchanged generation, cleanup within 10 s, no owned PID/transport remnants |
| P0 | 100 CDP/WD target create/close cycles with cross-observation | No wrong-session dispatch, lost events, or wrong-tab selection; distinct from 100 start/stop cycles |
| P0 | Background task/subscription/socket/process-tree leaks | Zero library tasks and owned resources after stop; aggregate handles/threads alone are insufficient |
| P1 | ReplaceControlled, selected-target close/crash, same-URL tabs | Replace only the controlled target; preserve unrelated tabs; precise ambiguity/close/crash errors |
| P1 | Delayed DOM, nested iframe, reacquisition, invalid locator | Reproduce frame paths, distinguish DOM/attachment stale, honor timeout |
| P1 | Slow/failed/persistent requests | Four WaitUntil contracts; ignore old loaders; timeout if NetworkIdle is unmet |
| P1 | Scripts/CDC off/on and CDP recovery | No duplicate document execution, re-register after recovery, preserve ordinary properties; detection sites are not substitutes |
| P1 | Cross-process profile/cache contention | Explicit second-profile-use failure, one valid cache, no partial artifacts |
| P1 | Slow/overflowing/throwing logger and payloads | No lifecycle deadlock or only bounded failure; overflow diagnostics; no sensitive default payload |
| P1 | Legacy net8 consumers, WinForms/WPF await/cleanup | Execute the provided net8 Legacy asset; net8 was subsequently added to FrameworkTests |

AC-07 resize records supported/unsupported outcomes. Establish per-version baselines, then check actual dimensions on success and `UnsupportedAttachedCommand` when unsupported. Do not classify arbitrary exceptions as unsupported.

### Step 6 — Stress and cleanup

```powershell
./scripts/test-windows.ps1 -Chrome C:\Chrome\chrome.exe -Driver C:\Chrome\chromedriver.exe -Stress -Results .artifacts/test-plan/stable-stress
./scripts/test-windows.ps1 -Chrome C:\ChromePrevious\chrome.exe -Driver C:\ChromePrevious\chromedriver.exe -Stress -Results .artifacts/test-plan/stable-minus-one-stress
```

After five warm-ups, measure 100 cycles and 20 concurrent instances. AC-04 limits handle growth to 5, thread growth to 2, and the last 20 private-byte samples' median to 120% of baseline. Step 5 instrumentation must also show zero library tasks and full process-tree/socket/handle cleanup. Measure driver/port closure within 5 s, profile deletion within 10 s, and lock release within 5 s.

Core `AC05_TwentyInstancesHaveDistinctEndpointsAndPorts` has `[Category("Stress")]`. `-Stress` selects the category and environment flag; normal runs explicitly exclude it.

All selected required Windows/Stress tests must execute. Intentionally skipped privileged symlink unit tests are explicitly filtered, with separate evidence required for that path. Selected tests may not skip.

### Step 7 — Framework and package consumers

Release-pack Core/Legacy and build `tests/BrowserDock.ClassicConsumer` without ProjectReference using Windows MSBuild. Verify binding redirects, x64, C# 7.3, and net481 dependency selection. See the [Framework procedure](framework481.md).

The current no-argument usage-exit-code-2 check is only a loading smoke test. Release acceptance must provide actual Chrome arguments and exercise navigation/disconnect/reconnect/stop. net10 results do not substitute for net481.

### Step 8 — Optional patching

Default release verification uses `PatchMode = DriverPatchMode.Disabled`. No real recipe is currently supplied, so synthetic fixture success only validates the patch engine.

To claim real `BinaryCompatibility`, pin recipe ranges, source hashes, exact match counts, and result hashes for Stable/Stable-1, then rerun launch/attach/disconnect/reconnect. Include ValidateOnly, unknown recipes, zero/excess matches, tampering, concurrent creation, and byte-preservation of vendor originals. Without recipes, that compatibility remains unverified.

## 5. Evidence and release approval

Planned additions to `fixture.json` and per-TFM TRX:

- Command, Git SHA, fixture/SeleniumBase versions, and pass/fail/skip/not-run for each matrix cell.
- Pre-failure state, generation/epoch, owned PIDs, endpoint probes, cleanup failures, monotonic timing.
- Local-fixture screenshots/HTML and normalized JSON. If Chrome dies, record collection failure and the last available diagnostics.
- Per-cycle handles/threads/private bytes and owned resources; preserve raw samples, not only averages.

Bound diagnostics separately without overwriting the original failure. Always clean up in `finally`, even if collection fails. Test artifact collection must not change the product's default payload-exclusion policy.

Approval requires:

1. All required runtime/browser common and Windows tests executed and passed; no unexpected skips, zero tests, or missing TRX.
2. Real-browser AC-01–03 evidence, plus measured AC-04–09 leak/concurrency/fault/target coverage for declared support. State AC-08 real-patch support separately.
3. Matching shared SeleniumBase scenarios with documented intentional differences. Comparisons alone do not approve .NET-specific stale/cancellation/cleanup contracts.
4. Completed P0 additions; required P1 behavior verified before release, unsupported features explicitly identified.
5. Updated `docs/implementation.md` with actual environments/results/unverified scope, retaining first failures and reruns. Detection-site success is not an acceptance criterion.

## 6. Work packages

| Order | Work | Deliverable / completion evidence |
|---|---|---|
| 1 | Environment, common regressions, Windows smoke | Versions/hashes/TRX and first AC-01–03 measurements |
| 2 | Categories, missing-test rejection, timing | Reject skipped required tests; explicit durations |
| 3 | Shared HTML and comparison runner | `tests/seleniumbase-reference/`, scenario JSON, SB-01–04 report |
| 4 | P0 faults, simultaneous CDP/WD, leaks | Regression tests and resource artifacts |
| 5 | P1 features, Legacy consumers, version matrix | Stable/Stable-1 and runtime reports |
| 6 | Dedicated Windows 11 automation/approval | Common CI plus browser job, preserved TRX/metadata/approval records |

First establish the environment and real core-test results. Validate the highest-risk Chrome-preservation/reconnect assumptions before adding the comparison harness and broader coverage.
