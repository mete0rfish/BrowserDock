# Issue #30 validation — 2026-10-06

Candidate: PR #43, `feat/30-uc-launch-profile`, now integrated with main
`6cda000b7175376f334bc6118d274b8fb08ed641` (PR #45).
Environment: Linux cloud, .NET SDK 10.0.401, runtimes 8.0.31/10.0.12;
Chromium and ChromeDriver 154.0.8037.57, headless, owned temporary profiles,
loopback HTTP fixture only. Windows browser execution remains excluded.

## Reproduced defect and fix

The observer CDP session is initialized before ChromeDriver attaches. On three
consecutive Standard navigations, the profile script saw zero CDC properties,
but the **first inline page script** saw all seven ChromeDriver CDC properties.
Removing/re-registering the observer's scripts after driver attachment did not
fix this: ChromeDriver's initializer runs in another CDP session.

The fix retains observer cleanup/profile/caller ordering and additionally
registers **only cleanup** through ChromeDriver's CDP command endpoint, after
its initializer in that same session. No cross-session ordering assumption is
needed. The attachment owns one cleanup identifier per bound window; unchanged
sources do not re-register and changed sources remove only the owned identifier.
Caller scripts are not copied to the driver session. An uncertain driver-side
mutation terminates the affected attachment; cleanup errors preserve the original
exception/cancellation.

| Measurement | Before | After |
|---|---:|---:|
| CDC properties at the first inline page script, three Standard navigations | 7 each | 0 each |
| CDC properties seen by the profile script | 0 each | 0 each |
| Caller/profile executions per document | 1 | 1 |
| Profile script ready state | loading | loading |

Additional Linux checks used both captured Core and Legacy settings: repeated
Standard navigation, CDP recovery while attached, detached navigation, controlled
tab replacement, disconnected CDP recovery, fresh driver attachment followed by
Standard navigation, selection of another window, and return to the original
window. All 18 navigated documents had zero CDC properties at their first page
script, profile-before-caller order, and one profile execution. Reattachment did
not replay user scripts in the existing document. ChromeDriver did reintroduce
CDC properties in that existing document: the documented cleanup guarantee is
for the **next controlled document**, not retroactive cleanup.

This harness exercises the product's CdpController, Attachment and option capture
against real Linux Chromium/ChromeDriver. It does not call Windows-only
Browser.StartAsync, verify Windows hosting/PID checks, or substitute for Windows
acceptance. Local reproduction sources/results are retained outside Git under
`/workspace/issue43-review` (baseline) and `/workspace/issue43-fix` (fixed/lifecycle).
The baseline recheck is `result-current-baseline.log`; fixed observations are
`result-fixed.log` and `result-lifecycle-{core,legacy}.log`.

## Regression coverage

- Common contracts exercise driver-session command routing, window-specific ID
  ownership, duplicate suppression, changed cleanup, disabled cleanup, rejected
  registration, missing IDs, cancellation and failed cleanup.
- The Standard protocol fixture emits the matching Document request required by
  PR #45, before frame/lifecycle completion; no production correlation was relaxed.
- The Windows browser fixture now measures the first inline HTML script as well
  as the profile script, including attached CDP recovery and Standard navigation
  after reconnect. It is compiled but has not been executed in this work.
- The obsolete test asserting that reordering observer scripts fixes driver order
  was removed. The same-session driver path is checked instead.

## Current validation

Full solution build passed with zero warnings/errors, including net481 libraries,
tests and C# 7.3 samples. Linux common contracts (`TestCategory!=Windows`) passed
with zero failures/skips: Core net8/net10 **334 each**, Legacy net8/net10 **270
each**, **1,208 total**. TRX and logs are under `/workspace/issue43-fix/results`
and `/workspace/issue43-fix`. Required hosted CI results are recorded in the PR.
Python reference/tooling tests passed (5 + 5), and the PowerShell TRX audit passed.

Required CI is requested using `ci:required`: Linux/Windows common contracts,
net481 runtime/package-consumer validation and Secret scan. Actual Windows
browser CI remains excluded; `ci:run` is not used.

## Remaining acceptance

Windows profile browser execution, profile-specific concurrent-instance/PID
isolation, measured locale/header behavior and pinned upstream comparison remain
pending. #30 stays open and PR #43 stays draft. Binary recipes (#31), differential
evidence (#36) and quarantined Windows navigation investigation (#41) are separate.

## Earlier portable validation (2026-10-04)

The original candidate's 1,130 common cases passed on final recheck. An initial
profile-disabled cancellation test failed on its subsequent navigation. PR #45
later made that test's cancellation occur after a protocol round trip, avoiding
cancellation racing the WebSocket send. The current branch includes that change;
see the original validation in commit `030c527` for the retained initial failure.
