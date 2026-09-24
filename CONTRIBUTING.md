# Contributing

Read the [release procedure](docs/releasing.md) when changing package metadata or automation.
Validate tooling changes with `python3 -m unittest discover -s tests/tooling -v`.

BrowserDock is experimental. Focused improvements to lifecycle correctness, diagnostics, compatibility, and documentation are welcome. Discuss large API changes in an issue first. Follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Development

Install the .NET 10 SDK selected by `global.json`. Running net8.0 tests needs a .NET 8 runtime; running net481 needs Windows with Framework 4.8.1. Building net481 elsewhere uses NuGet reference assemblies.

```sh
dotnet restore BrowserDock.slnx
dotnet build BrowserDock.slnx --no-restore -m:1
dotnet test tests/BrowserDock.Tests -f net10.0 --no-build --no-restore --filter 'TestCategory!=Windows'
dotnet test tests/BrowserDock.FrameworkTests -f net10.0 --no-build --no-restore --filter 'TestCategory!=Windows'
python3 -m unittest discover -s tests/seleniumbase-reference -p test_compare.py -v
```

Common tests need no Chrome. Real browser tests require Windows 11 x64, an interactive desktop, PowerShell 7, and compatible binaries. Follow the [test plan](docs/test-plan.md). Build-only or skipped results are not browser verification.

## Pull requests

1. Start a focused branch from the default branch.
2. Explain the concrete problem, resulting behavior, and minimal reproduction.
3. Add regression tests for lifecycle, protocol, ownership, or compatibility changes. Prefer local HTTP/WebSocket fixtures.
4. Record exact verification commands and identify Windows/runtime tests not run.
5. Update documentation and the Unreleased changelog for public behavior changes.

Keep unrelated refactoring separate. Follow existing formatting. Do not add browser binaries, profiles, cookies, secrets, generated results, or machine-specific configuration. Changes to process termination, profile deletion, patching, public APIs, and workflows need explicit evidence. Never weaken assertions or turn unexpected skips into passes to make CI green. Maintainers review code before running it on dedicated Windows machines.

## Rights and participation

Submit only material you have permission to contribute. Identify copied/adapted code and preserve applicable notices. Reference links are not permission to relicense source. Do not import or translate GPL code into this independent project without resolving the licensing implications first.

Contributions are accepted under the project's [MIT License](LICENSE). No separate CLA is currently required. Maintainers do not promise a response or release schedule.

Use the bug template with OS build, architecture, runtime, Chrome/driver versions, commit, patch mode, and a local reproduction. Strip private data from logs. Report vulnerabilities through [SECURITY.md](SECURITY.md).
