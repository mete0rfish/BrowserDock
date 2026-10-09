# First work batch - 2026-10-09

## Baseline and purpose

Remote main: `11fcefca74cfa0ab8d5c0c6fe59524fc61e96810` (merged PR #52).
PR #53 reviewed head: `f3dd6f932b44a66ff5d6faa4ad02d0d6b349ce15`.
This batch reconciles trackers and prepares bounded investigations. It does not
run browser acceptance, fix the historical cleanup timeout, or authorize release.

## Tracker reconciliation

| Tracker | Implemented evidence | Still open |
|---|---|---|
| #19 | PR #20: MIT identity, reporting routes, notices and English documentation; PR #21: 0.2.0 source version | Final publication review, current operational settings, release-candidate Windows stress, public Source Link and NuGet publication |
| #24 | #25-#29 completed; PR #43 launch profile; PR #47-#49/#52 interaction increments | #30-#36 acceptance and remaining implementation; #41/#51 stability |
| #30 | PR #43: opt-in profile, conflicts/snapshot, script order and CDC cleanup; common and bounded Linux browser evidence | Full Windows controlled-fixture/ownership acceptance; no complete profile-equivalence claim |
| #32 | PR #47 waits/live properties, #48 click, #49 input, #52 text/hidden waits | Multiple-element search, compatibility context/helper decisions and broader execution evidence |
| #36 | PR #52: SB-05 Core/Legacy Linux comparisons and identity/expected-output rejection | Windows and remaining lifecycle/profile/patch/native/CDP scenarios; no whole-row promotion to Equivalent |
| #41 | PR #45 request correlation and diagnostics; PR #44 quarantine | Historical trigger, profile lock owner, six removed cases and one quarantined case |
| #51 | PR #52 diagnostic stages and repeated CDP/process cleanup | Demonstrated cause/fix; PR #53 remains a draft investigation |

Issue checkboxes describe their full wording. Passing a bounded scenario or merging
an implementation PR does not complete a broader acceptance condition. Preserve
historical failures and dates alongside newer evidence.

## PR #53 scope decision

**Keep Draft. Scope any eventual merge as diagnostic tooling and regression
coverage, not a fix for #51.** This batch does not merge or change draft state.

Included work at the reviewed head:

- Owned-testhost managed-thread capture using a pinned, non-packable ClrMD tool;
  bounded collection, method signatures/thread IDs only, original failure retained.
- Four portable CDP cleanup schedules and the Framework collector smoke test.
- CI attachment of stack JSON beside TRX, preserving label-only triggers.
- Two separate corrections: native Windows version-resource lookup and input
  test expectations distinguishing polling timeout from active-command recovery.

There is no production `src/` change in this diff. Common run
[37742981668](https://github.com/mete0rfish/BrowserDock/actions/runs/37742981668)
and Secret scan
[37742981715](https://github.com/mete0rfish/BrowserDock/actions/runs/37742981715)
are successful. The five common jobs were checked through the Actions API;
browser and quarantine runs were skipped. The PR reports 4,989 passing .NET
executions, including 527 net481 cases. No inline review threads were returned.
These observations are not a complete maintainer approval or a historical fix.

Before an eventual diagnostic-only merge, review the collector boundary and cost,
preserved assertions/budgets, package isolation, and the two separate corrections.
Any source/base change needs newly requested checks. Keep #51 open after such a
merge. Put a demonstrated runtime fix and its failing-before/passing-after
regression in a separate PR. Windows acceptance is a separate workstream.

## Next executable work

1. Follow the [net481 reproduction plan](issue-51-reproduction-plan.md) on the
   exact #53 candidate; preserve each failed and passing attempt separately.
2. Follow the [Windows reproduction plan](issue-41-reproduction-plan.md), first
   collecting current normal/quarantine evidence, then restoring historical
   cases on a dedicated investigation branch.
3. Reconcile #30/#32/#36 against the resulting evidence before advancing claims.
   A small #32 API increment can proceed independently with its own contracts.

Use the existing [CI label routes](ci.md). This planning batch adds no labels,
starts no CI/browser runs, changes no repository settings and publishes no package.
