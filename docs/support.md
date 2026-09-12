# Support and compatibility

BrowserDock is experimental. Alpha releases may change public APIs, exceptions, and behavior; see the changelog. No commercial support or response-time SLA is promised.

| Area | Policy |
|---|---|
| Platform | Windows 11 x64, interactive desktop, headed Chrome |
| Modern development | .NET 10 is the primary long-term development target |
| .NET 8 | Retained during its support period; reassess after 2026-11-10 before the next release |
| Framework | 4.8.1 via Legacy, C# 7.3 examples, x64 consumer tests |
| Chrome/driver | M115+, matching MAJOR.MINOR.BUILD; support limited to tested pairs |
| Stable/Stable-1 | Intended verification matrix, not a completed test claim |
| Patching | Disabled by default; real recipes are not supplied or verified |

The .NET 8 date follows [Microsoft's lifecycle](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core). Removing a TFM is a compatibility change requiring notice; this policy does not remove one automatically.

Build support differs from execution support. Common tests run on macOS/Linux, but product browser hosting there is not supported. See [implementation.md](implementation.md) for actual results.

Release evidence must record commit, library/Selenium/Chrome/driver versions, runtime, OS build, hashes, test selection, and skipped/not-run tests. Older alpha versions are not automatically backported.
