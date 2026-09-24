# Security policy

## Supported versions

No public stable release is currently supported. The alpha development line receives fixes on a best-effort basis; older experimental snapshots are not maintained. See [support.md](docs/support.md).

## Report privately

Report security vulnerabilities privately to [sungwonyoon326@gmail.com](mailto:sungwonyoon326@gmail.com), monitored by mete0rfish. Do not post unpatched vulnerabilities, credentials, cookies, or profiles in public issues. There is no response-time SLA.

Include the affected commit/package, OS/runtime, browser/driver versions, reproduction steps with a disposable local fixture, expected boundary, and actual impact. Share the minimum information needed to reproduce the issue.

## Boundaries

Useful reports include unintended process termination, deletion of caller-owned data, profile locking failures, executable/cache integrity, non-loopback control endpoints, stale-reference guards, sensitive logging, and release credentials.

Advanced CDP commands and caller-supplied JavaScript control the browser. Deliberately supplied commands are not a sandbox escape: the library is not a sandbox for untrusted scripts. CAPTCHA or third-party detection results alone are not a security guarantee.

Maintainers should reproduce reports in isolation, coordinate disclosure where practical, document affected versions and fixes, and avoid public sensitive artifacts before remediation.

## Repository checks

The private repository uses a SHA-pinned Gitleaks workflow on pushes and pull requests.
It is a post-push check, not GitHub push protection, and does not prevent a secret from
reaching GitHub. Treat any detected credential as exposed and rotate it. Reports and
PR comments are disabled to avoid copying findings into artifacts or discussions.
