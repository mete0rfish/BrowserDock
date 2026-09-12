# Security policy

## Supported versions

No public stable release is currently supported. The alpha development line receives fixes on a best-effort basis; older experimental snapshots are not maintained. See [support.md](docs/support.md).

## Report privately

The private security reporting address awaits the owner's confirmation. Before publication, enable GitHub private vulnerability reporting and/or list a monitored contact here. Do not post unpatched vulnerabilities, credentials, cookies, or profiles in public issues. There is no response-time SLA.

Include the affected commit/package, OS/runtime, browser/driver versions, reproduction steps with a disposable local fixture, expected boundary, and actual impact. Share the minimum information needed to reproduce the issue.

## Boundaries

Useful reports include unintended process termination, deletion of caller-owned data, profile locking failures, executable/cache integrity, non-loopback control endpoints, stale-reference guards, sensitive logging, and release credentials.

Advanced CDP commands and caller-supplied JavaScript control the browser. Deliberately supplied commands are not a sandbox escape: the library is not a sandbox for untrusted scripts. CAPTCHA or third-party detection results alone are not a security guarantee.

Maintainers should reproduce reports in isolation, coordinate disclosure where practical, document affected versions and fixes, and avoid public sensitive artifacts before remediation.
