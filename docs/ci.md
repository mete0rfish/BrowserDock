# Label-requested CI

Every workflow starts only on a PR **label-added** event. There is no
`workflow_dispatch`, push, scheduled or PR-open/synchronize trigger. Leaving a
label attached does not run new commits; remove and re-add it to request another
run. Other labels may produce skipped workflow records, but do not execute tests
or satisfy required checks under their required names.

| Label | Work requested |
|---|---|
| `ci:required` | Six required checks: Ubuntu/Windows × net8/net10 common contracts, net481 contracts/package consumer, Secret scan |
| `ci:run` | Required checks plus normal GitHub-hosted browser tests |
| `ci:browser` | Only normal GitHub-hosted browser tests |
| `ci:browser:stress` | Hosted browser tests including stress cases |
| `ci:quarantined` | Only quarantined browser diagnostics (#41), separate from normal coverage |
| `ci:windows11` | Interactive Windows 11 acceptance on the configured self-hosted runner, after merge |
| `ci:windows11:stress` | The same Windows 11 acceptance including stress cases, after merge |
| `ci:package` | Package candidate validation, after merge; no publishing |
| `release:publish` | Build/validate a candidate and request approved publishing, after merge |

Create the named labels in **Issues → Labels** if needed. The label being added
selects the work; other labels already attached do not expand it. For example,
adding `ci:quarantined` to a PR that retains `ci:run` runs only quarantine work.
Use `ci:required` when actual browser execution is excluded.

## PR and post-merge work

Common, secret, hosted-browser and quarantine workflows use `pull_request` and
check out GitHub's test merge commit. Resolve merge conflicts first. These checks
can validate an open PR, including a PR stacked on another feature branch.

Windows 11 and package/release workflows use **trusted default-branch workflow
code** via `pull_request_target: labeled`. Their jobs require a same-repository
PR already merged into the default branch, and explicitly check out that PR's
immutable `merge_commit_sha`. Open, closed-but-unmerged, fork and non-default-base
PRs cannot dispatch those jobs. This preserves the existing default-branch
`windows-browser`/`release` environment restrictions without running unmerged PR
code on a privileged runner or with publishing credentials. These workflows
become usable after this CI change reaches the default branch.

Configure environment/repository variables `BROWSERDOCK_CHROME` and
`BROWSERDOCK_DRIVER` as absolute paths on the interactive Windows 11 runner.
The runner still needs the `windows-11-interactive` label. GitHub-hosted browser
jobs install their pinned Chrome/driver automatically and need no dedicated PC.

For publishing, set `WINDOWS_ACCEPTANCE_RUN_ID` to the successful
`ci:windows11:stress` run for the same merged PR/commit. The trusted run name binds
the requested commit and stress label; downloaded fixture metadata independently
confirms the actual checked-out commit, clean tree, stress mode and passing TRX
files. `pull_request_target`'s `head_sha` identifies the base workflow revision,
so it is not substituted for the explicitly checked-out release commit.
`PUBLIC_RELEASE_ENABLED`, NuGet Trusted Publishing and the `release` environment
remain required. Adding a normal CI label never publishes. See [releasing](releasing.md).

## Result gates

Common and package jobs audit their **actual** named Core/Legacy TRX files.
Missing files, zero results, inconsistent counters, unexpected skipped/failed
tests, missing definitions and duplicate result IDs fail the job. On Windows,
`RejectsReparsePointOwnedPaths` is explicitly filtered out because it requires
separate symlink privileges; it continues to run in Ubuntu common CI.

`scripts/assert-suite-results.ps1` shares the required-fixture manifest between
execution and release evidence. Common results must contain executed UC workflow,
launch-profile and script-registration fixtures. Normal browser results must
also contain executed `UcBrowserTests` and `UcLaunchProfileBrowserTests` in both
Core and Legacy, plus each project's existing browser fixtures. A definition
without a corresponding passing result does not satisfy a required fixture.
Quarantine has a separate Core-only manifest. `test-result-audit.ps1` tests these
failure paths; it does not replace auditing actual test output.

This change retains existing first-failure behavior and runner/version matrices.
It does not add automatic retries, schedules, or broaden browser acceptance.
