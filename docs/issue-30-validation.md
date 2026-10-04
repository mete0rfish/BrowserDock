# Issue #30 portable validation — 2026-10-04

Candidate: `feat/30-uc-launch-profile`, based on main merge commit
`14c59fea281f9520ee6d49ec81cca0eda04daab4` (PR #42).
Environment: Linux cloud workspace, .NET SDK 10.0.401, runtimes 8.0.31/10.0.12.
Windows CI and Windows browser execution were explicitly excluded from this work.

## Build and portable contracts

`dotnet build BrowserDock.slnx --no-restore` passed with zero warnings/errors.
This includes Core/Legacy net481, net8.0 and net10.0, both test projects and the
C# 7.3/net481 samples. Building net481 is not Framework runtime execution.

Each suite used:

```sh
dotnet test <project> -f <framework> --no-build --no-restore --filter 'TestCategory!=Windows' --logger trx
```

The final four-suite batch produced:

| Suite | Runtime | Passed | Failed | Skipped |
|---|---|---:|---:|---:|
| Core | net8.0 | 302 | 1 | 0 |
| Core | net10.0 | 303 | 0 | 0 |
| Legacy | net8.0 | 262 | 0 | 0 |
| Legacy | net10.0 | 262 | 0 | 0 |

The failure was the existing, profile-disabled
`CanceledProtocolRequestIdentifiesNavigationStage("Page.navigate")` test. Its
cancellation assertions completed; the subsequent navigation failed with
`DevToolsEndpointFailure: CDP connection is unavailable` at `Network.enable`.
All four cases passed in a targeted net8.0 recheck. A subsequent full Core net8.0
recheck passed 303/303, with zero skips. The latest result for each suite therefore
passes all 1,130 cases, with the earlier intermittent failure retained.

The unchanged CDP send path uses the caller token for WebSocket sending, so
cancellation while send completion is pending is a possible transport-abort cause.
This is a hypothesis, not a demonstrated root cause or a fix for #41. A passing
rerun does not establish stability.

New contracts cover profile defaults/conflicts, nested snapshot immutability,
Core/Legacy equivalence, explicit patch requirements, redacted metadata, ordered
registration/replacement, driver attachment refresh, uncertain mutation,
late CDC discovery, target cleanup, tab replacement and CDP recovery. Protocol
fixtures observe command ordering, not actual JavaScript execution in Chrome.

Local artifacts (outside Git): `/workspace/issue30-results/final/*.trx`,
`/workspace/issue30-results/recheck-network/cancel-stage-recheck.trx`,
`/workspace/issue30-results/core8-recheck/BrowserDock.Tests-net8.0.trx`, and
`/tmp/issue30-build-complete.log`. An earlier recheck without loopback permission
aborted in the VSTest socket setup; it did not execute tests.

## Remaining acceptance

- `UcLaunchProfileBrowserTests` is compiled into both suites but was not run.
  It records initial blank-page behavior and checks document-start ordering,
  CDC cleanup, replacement, reconnect and transport recovery.
- Profile-specific concurrent-instance/PID/profile-isolation observations,
  Windows browser execution, and net481 execution remain pending.
- Language is validated as an effective launch option; browser-visible locale
  and header behavior are not measured here.
- Real binary recipes remain #31; same-candidate upstream comparison remains #36.
  The existing Windows navigation/cleanup investigation in #41 remains open.

Keep the implementation PR in draft and issue #30 open until its remaining
acceptance evidence is reviewed. See [profile behavior and upstream inventory](uc-launch-profile.md).
