# Issue 26: startup options snapshot verification

Verified locally on 2026-09-29, on branch `fix/issue-26-options-snapshot`,
against the working-tree changes based on `6c2c2feb8a1b69a1276b09d6fdb9c62d06ece54d`.
This is not a release-candidate or full Windows acceptance pass.

## Change and ownership boundary

`Browser.StartAsync` captures the four option collections into privately owned,
read-only copies, validates those copies, and uses the same effective options for
all startup stages and the retained browser configuration. Core profile and
timeout records already have immutable scalar properties. Existing argument order,
defaults, platform checks and rollback behavior are preserved.

Custom `IDriverPatchStrategy` implementations, their pattern collections and byte
arrays remain caller-owned and must stay stable until startup finishes, including
failure/cancellation cleanup. Logger instances remain shared for the browser
lifetime. Legacy recipe snapshots already copy both pattern byte arrays; the
existing test now also checks replacement bytes, script/scheme collections and
recipe-list replacement. See [the ownership rules](usage.md#api-rules).

## Regression evidence

Before changing the implementation, two Windows tests failed deterministically
using the existing `validation` stage hook and `TaskCompletionSource` barriers:

- Chrome reported `--lang=ko` after the caller changed the original `--lang=en-US`
  argument. The installed script produced `mutated`, and CDP recovery subsequently
  produced `after-start`, instead of the captured value.
- Patch preparation called the replacement strategy once and the captured strategy
  zero times after the caller replaced the original list contents.

After the fix, both tests pass on all five test targets below. They inspect Chrome's
actual command line through CDP, execute the registered script before and after
CDP recovery, compare retained options, check URL policy and record which strategy
is consulted. No timing-sensitive sleeps are used in these regressions.

Five new common tests per target exercise capture/validation in the shared engine,
including a single-read caller collection, rejection of managed arguments and a
synthetic patch prepared from the captured strategy list after the caller clears
its collections. The tests run without Chrome or the Windows hosting guard.

## Environment and results

Windows 11 x64 build 26100; .NET SDK 10.0.401; .NET runtimes 8.0.31 and 10.0.12;
.NET Framework 4.8.1; Chrome for Testing and ChromeDriver 154.0.8037.57.
The final solution build completed with zero warnings and zero errors.

| Test project | Runtime | Common passed / failed | Windows passed / failed | New Windows regressions passed |
|---|---|---:|---:|---:|
| BrowserDock.Tests | net8.0 | 52 / 0 | 32 / 4 | 2 |
| BrowserDock.Tests | net10.0 | 52 / 0 | 32 / 4 | 2 |
| BrowserDock.FrameworkTests | net481 | 27 / 0 | 11 / 1 | 2 |
| BrowserDock.FrameworkTests | net8.0 | 27 / 0 | 12 / 0 | 2 |
| BrowserDock.FrameworkTests | net10.0 | 27 / 0 | 12 / 0 | 2 |

No tests in these selected runs were skipped. All 185 common tests passed. Of 108
Windows tests, 99 passed and nine failed. The issue-26 regressions passed 10/10;
existing startup cancellation, rollback, profile release and process cleanup tests
also passed. After adjusting the new common fixture to use the engine's canonical
temporary root for macOS portability, the solution was rebuilt and those five
common tests passed again on every target (25/25). No product code changed after
the Windows runs.

## Remaining Windows failures

Both Core runtimes failed these existing UI assertions:

- `SharedScenarioMatchesManifest("SB-01")`: click/input/frame values stayed unchanged.
- `AC01_02_03_DisconnectPreservesChromeAndReconnectReplacesSession`: the button
  remained `ready` after the final click.
- `AC07_AttachedResizeReportsCapabilityResult`: outer width was 945 instead of 1024.
- `NestedFrameReacquisitionPreservesContextAcrossSessionChange`: input remained empty.

Legacy net481 failed `FacadeReconnectPreservesBrowserStateAndInvalidatesOldReferences`
at its post-reconnect button assertion. The same Legacy test passed on net8/net10.

A diagnostic engine was built separately from the unmodified Core sources at the
base revision, without replacing working-tree sources or normal build outputs.
In the initial net8 comparison, SB-01, attached resize and nested-frame input failed
with the same symptoms; the disconnect test passed. These results demonstrate
pre-existing failures for those three cases, but do not establish the cause of
every remaining UI failure. Their assertions were not weakened or excluded to
claim a full pass.

Follow-up isolation: the Core net8 disconnect test passed again on the original
engine and also passed on the fixed engine when run alone. The Legacy net481
facade test initially passed on the original engine, but three alternating
original/fixed comparisons then failed on both engines (3/3 each) with the same
button assertion. Thus this Legacy symptom also predates the change. The Core
suite-versus-isolated difference remains unresolved; these checks do not erase
the nine failures in the matrix or establish a root cause for the UI behavior.

## Commands and local artifacts

Set `DOTNET_ROOT`/`PATH` to the installed SDK and `BROWSERDOCK_CHROME` and
`BROWSERDOCK_DRIVER` to the tested binaries. Build with:

```powershell
dotnet build BrowserDock.slnx --no-restore -m:1 -p:UseSharedCompilation=false
```

Run `dotnet test` for each project/runtime row above with `--no-build --no-restore`,
`--logger trx` and the following filters, separately:

```text
TestCategory!=Windows&TestCategory!=Stress&FullyQualifiedName!~RejectsReparsePointOwnedPaths
TestCategory=Windows&TestCategory!=Stress&FullyQualifiedName!~RejectsReparsePointOwnedPaths
```

For the focused regressions, use
`FullyQualifiedName~StartupSnapshotTests|FullyQualifiedName~OptionsCaptureTests`.
The local runner and its transcripts preserve the exact invocations and environment.

Artifacts under `.artifacts/issue-26/` (ignored local diagnostic data):

- `red-host/before-core-net10.trx`: the two pre-fix assertion failures.
- `red/before-core-net10.trx`: initial sandbox run, where Chrome exited before
  readiness; this is not the launch/script reproduction evidence.
- `common-20260929-123908-204/`: common matrix TRX, transcript and fixture metadata.
- `windows-20260929-123954-815/`: Core net8 Windows results.
- `windows-20260929-124430-939/`: Core net10 and all Legacy Windows results.
- `common-final/`: final portable-fixture checks on all targets.
- `baseline-results/`: comparisons using the unmodified Core engine.
- `ui-comparison/`: isolated Core results and the alternating Legacy comparisons.
- `build-final.log`: final solution build output.
- `source-sha256.json`: hashes of the changed source/test files used for verification.

Stress, privileged reparse-point tests, Stable-1, SeleniumBase Python browser
comparisons and real patch recipes were not run. Linux/macOS execution was not
performed locally; the common tests are included in both existing test projects
for the supported CI hosts.
