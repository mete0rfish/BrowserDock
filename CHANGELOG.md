# Changelog

Public changes are recorded here. Breaking changes during 0.x are described explicitly.

## Unreleased

### Added

- Core/Legacy high-level `TypeAsync` (clear then input) and `SendKeysAsync`
  (append at the end), with editable-state waiting, one overall deadline and
  per-stage cancellation checks. A trailing LF/CRLF requests form submission;
  partial side effects are never replayed. Raw `ElementRef.SendKeysAsync` stays
  unchanged. Custom `IBrowserCommands` implementers must add both members.
  See [input contracts](docs/element-input.md) (#32, partial).

- Core/Legacy high-level `ClickAsync(locator, ElementWaitOptions, token)` on
  `Browser` and `lease.Commands`: wait for displayed/enabled state, then issue
  one guarded WebDriver click within the same overall deadline (#32, partial).
  No replay after dispatch, JavaScript/native fallback or automatic load wait.
  Custom `IBrowserCommands` implementers must add the new click member.
  See [guarded clicks](docs/element-clicks.md).

- Core/Legacy element existence, visibility and absence waits with one overall
  deadline, explicit target/frame context, cancellation and bounded observation
  retries (#32, partial). Add DOM attribute/property and live-value reads; keep
  `GetAttributeAsync` as a DOM-attribute alias. `IBrowserCommands` implementers
  must add the three new wait members. See [element waits](docs/element-waits.md).

- Optional, versioned UC launch/script profile in Core and Legacy (#30): immutable
  resolved settings, language/CDC conflicts, explicit patch requirements, ordered
  document scripts and redacted effective-policy information. Profile mode refreshes
  CDC discovery at attachment/navigation boundaries and replaces changed script
  registrations without accumulating duplicates. Driver-session cleanup also runs
  after ChromeDriver's initializer so CDC properties are absent at the first page
  script, without replaying caller scripts. Windows browser execution is unverified;
  real binary recipes and upstream parity remain separate work.
### Changed

- Require an explicit PR label for every CI workflow, including dedicated browser,
  stress, quarantine and post-merge acceptance/package/release labels. Audit actual
  common-test TRX files and require executed UC browser fixtures in both browser
  runs and release evidence; discovered-only fixtures cannot satisfy the gate.

- Temporarily quarantine the nested-frame reacquisition browser test tracked in
  #41. Preserve its assertions in a separate manually dispatched Windows suite;
  normal/stress runs exclude it and report the coverage gap. This is not a fix
  for the intermittent navigation failure.

### Fixed

- Read Windows Chrome version metadata without launching Chrome during reference
  runner preflight; retain CLI version queries for the Linux investigation.

- Keep command-fixture readiness polling within its original budget when an
  atomically published PID file has a transient Windows sharing/lock violation.
  Other I/O failures and owner/PID checks remain errors; no test is rerun.

- Retain bounded owned-process/attachment cleanup diagnostics and preserve
  primary fixture stop errors alongside subsequent cleanup failures (#51).
  The intermittent net481 failure's root cause is still unconfirmed.

- Add Core/Legacy visible-text substring/exact and hidden-or-absent waits (#32),
  reusing the overall wait deadline and explicit target/frame context. Inputs
  use their live value. Both `IBrowserCommands` interfaces gain three members;
  third-party implementations need source updates during 0.x.

- Add the separately selectable SB-05 high-level interaction comparison (#36),
  with same-commit/version/binary identity checks and an explicit Linux
  investigation mode. Windows browser acceptance remains separate.

- Report WebDriver JavaScript errors as `ScriptExecutionFailure` in Core and
  Legacy without invalidating a healthy attachment. Preserve the original error
  and command diagnostics; partially completed scripts/input are never replayed.
  The new category is appended, preserving existing enum values.

- Revalidate Chrome/CDP and the controlled target after UC disconnected holds;
  preserve caller cancellation tokens for every UC method, including queued and
  already-canceled calls (#29).

- Preserve Chrome navigation rejection text, navigation-stage diagnostics and
  original cancellation causes through Core/Legacy error and timeout mapping (#41).

- Reject ineffective navigation reconnect options and delays that cannot fit the
  navigation budget before probing or mutating browser state (#28). Core and
  Legacy share the same validation; valid navigation modes retain their behavior.

- Include queued WebDriver commands in the operation deadline, preserve caller
  cancellation tokens, and bound failed-command recovery with one cleanup budget
  (#27). Failed-attachment invalidation and shared disposal protect replacement
  attachments and concurrent Stop/Dispose. Cancellation before execution retains
  the attachment; failed resource cleanup can retry incomplete steps without
  repeating completed disposal. Windows browser acceptance is pending.

- Capture and validate one startup options snapshot and use it for Chrome launch,
  driver preparation, CDP scripts and subsequent operations (#26). Caller list
  mutations after capture no longer alter the running configuration. Custom patch
  strategies and loggers remain caller-owned objects with documented ownership.

### Added

- Opt-in Core/Legacy `Browser.Uc` lifecycle and navigation helpers (#29): distinct
  connect/reconnect, explicit current/replacement-tab policy and minimum disconnected
  duration measured from confirmed driver exit. Each composite operation shares
  one deadline and lifecycle gate. Existing Core navigation behavior is unchanged.

- SeleniumBase Driver/UC and CDP compatibility contract and per-API matrix for
  issue #25: phased scope, explicit timing/context/value differences, Core/Legacy
  requirements, implementation gaps, provenance and candidate-commit evidence rules.
  This documentation adds no runtime APIs or new browser/parity validation claims.

## 0.2.0 — 2026-09-25

First non-prerelease GitHub source release in the private repository. No NuGet
packages are published. This version does not establish new Windows acceptance
evidence or broaden the verified support matrix.

### Added

- Owner-confirmed MIT license, reporting contacts, English documentation, and required secret scanning.

- Independent Chrome ownership, CDP control, and replaceable WebDriver sessions.
- Attachment epochs, guarded leases, and explicit element reacquisition.
- Modern and C# 7.3-friendly Task APIs targeting net481/net8.0/net10.0.
- Local protocol contracts, Windows lifecycle/fault/stress tests, and shared SeleniumBase scenarios.
- Contribution/security documents, issue templates, and package/release preparation.

### Changed

- Promoted the shared source/package version from `0.2.0-alpha.1` to `0.2.0`.

- Updated Logging.Abstractions and Microsoft.Bcl.AsyncInterfaces together to 10.0.12
  to avoid the Framework package downgrade error.
- Renamed the project, packages, namespaces, public `Browser` type, exception base
  type, command interface, environment variables, project paths, tests, and docs to
  BrowserDock. No compatibility aliases are retained because no public release exists.

### Fixed

- Wait for Chrome's initial page within the CDP initialization deadline instead
  of racing it with a fallback tab and failing startup with `AmbiguousTarget`.
- Recognize owned IPv6 wildcard ChromeDriver listeners while retaining IPv4
  loopback readiness probes and strict Chrome CDP listener validation.
- Omit the launch-only `detach` capability when attaching to externally owned
  Chrome; cover listener ownership and attachment capabilities in regression tests.
- Allow up to five seconds for Windows port-close probes to report connection
  refusal, and require that specific socket error instead of accepting any error.
- Corrected the Windows process-wait fixture's shell arguments and ensured it exits
  before testing subscription to an already-exited process.
- Isolated the diagnostic overflow test writer from thread-pool scheduling delays
  while retaining the blocked-sink and bounded-write assertions.

### Limitations

- Normal Windows tests pass on Chrome/ChromeDriver 154.0.8037.57 across the five
  Core/Legacy runtime combinations. Stress, Stable-1, and SeleniumBase Python
  browser comparisons remain unverified; see the implementation validation record.
- No verified binary patch recipes, headless mode, GUI input, browser downloads, or CAPTCHA guarantee.
- No public NuGet release. Earlier 0.1.0/0.2.0 packages were local artifacts.
