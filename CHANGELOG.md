# Changelog

Public preparation adds English/Korean entry points, contribution/security policies,
issue forms, dependency update configuration, portable symbols, and a gated NuGet
candidate/release workflow. Public identity and licensing remain pending owner confirmation.

Public changes are recorded here. Breaking changes during 0.x are described explicitly.

## Unreleased — 0.2.0-alpha.1

### Added

- Independent Chrome ownership, CDP control, and replaceable WebDriver sessions.
- Attachment epochs, guarded leases, and explicit element reacquisition.
- Modern and C# 7.3-friendly Task APIs targeting net481/net8.0/net10.0.
- Local protocol contracts, Windows lifecycle/fault/stress tests, and shared SeleniumBase scenarios.
- Contribution/security documents, issue templates, and package/release preparation.

### Changed

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
