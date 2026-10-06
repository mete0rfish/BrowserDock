# Issue #41: navigation failure evidence

This change fixes two demonstrated event-wait failure paths and adds evidence for
the remaining Windows investigation. It does not close #41 or restore the six
removed cases or the quarantined nested-frame case.

## Navigation behavior

- Track the main Document request by observer session, main frame, loader and
  request ID. Fail with `ProtocolError` and the original Chrome error when that
  request fails. Other sessions, subframes, subresources and the previous loader
  cannot trigger this failure.
- Chrome also sends `net::ERR_ABORTED` for successful downloads. When the matching
  request has received a response, retain the existing navigation budget for
  `Browser.downloadWillBegin`. Do not add a short grace timer or infer a download
  solely from the MIME type. A later error-page load must not report success.
- If that response-followed-by-abort never produces a download event, cancellation
  or timeout retains `Navigation.CdpErrorText` and `Navigation.AwaitingDownload`.
  This ambiguous case can still consume the full existing budget.
- Fail with `DevToolsEndpointFailure` when the active observer session detaches,
  even if the browser connection and target remain alive. Events are handled after
  the navigation command returns; this does not change a stalled WebDriver call's
  own deadline or introduce automatic navigation retries.

## Evidence retained

`Navigation.Trace` is JSON containing the last 64 selected event/phase records,
with UTC and relative timestamps and session/target/frame/loader/request IDs.
Individual values are limited to 128 characters. Protocol URLs, headers, response
bodies and JavaScript sources are excluded. The trace is diagnostic, not a complete
CDP recording; heavy Network events are omitted and older records can be evicted.
Existing Core/Legacy cancellation and timeout translations preserve it.

`Cleanup.Trace` records stage timing, Chrome/driver PID and liveness, owned Chrome
tree liveness and CDP health. Cleanup exceptions now retain an aggregate of their
original causes. If temporary profile deletion exhausts its existing budget after
an I/O/access failure, the last filesystem exception, HResult and attempt count
survive. Budgets, ownership checks, process termination and deletion retry policy
are unchanged. This is **not** a file-lock owner detector.

Local browser fixture HTTP start/end records include UTC, a request ID, route,
status, response-started flag, cancellation and exception type. They exclude query
strings, headers and bodies. Core and the Legacy fixture process retain at most
64 records for TRX output. UTC and route correlate them with navigation evidence;
fixture request IDs are not CDP request IDs.

The known failing initial navigation sites and retained redirect cases print
exception metadata into TRX. Startup-cancellation tests inspect `CleanupFailure`;
their final directory deletion preserves a primary failure while recording a
secondary cleanup failure. Cleanup failure without a primary failure still fails
the test.

## Local validation

Portable protocol regressions cover document failure/abort, redirect failure,
observer detach, unrelated events, delayed download and an ambiguous abort followed
by an error-page load. Shared Core/Legacy tests cover cancellation/timeout mapping,
cleanup causes, trace bounds/redaction and preservation of primary test failures.

A separate diagnostic harness used Linux headless Chromium and ChromeDriver
154.0.8037.57, the real `RemoteWebDriver` with `PageLoadStrategy.None`, and a local
HTTP server. It explicitly injected `Page.stopLoading` or observer
`Target.detachFromTarget` after a slow request reached the server:

| Scenario | Before | After |
| --- | --- | --- |
| Main request stopped before HTTP response | Event wait used the 5-second budget | Original `net::ERR_ABORTED`, about 27 ms |
| Observer session detached | Event wait used the 5-second budget despite a live target | Session failure, about 38 ms |
| Standard attachment download | `Download`, about 83 ms | `Download`, about 58 ms |

Durations are individual diagnostic observations, not performance guarantees.
The baseline failure experiments used PR #42 head `161654a`; the download baseline
used main `0f79de8`, before these changes. The relevant event-wait behavior was the
same. Linux uses different binaries from Windows Chrome for Testing. These injected
failures do not establish the cause of the historical Windows failures.

## Remaining acceptance

Keep actual Windows browser execution separate from `ci:required` common contracts.
Capture fresh normal/quarantined browser TRX with the new evidence, identify the
actual trigger and profile lock owner, and compare the required baseline/candidate
fixtures. Restore removed coverage only with that evidence. For the nested-frame
case, #41 requires three consecutive full normal candidate runs including the case
on both Core runtimes. Windows 11 desktop acceptance remains separate.
