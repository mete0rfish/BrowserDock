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

## Recorded validation (2026-09-24)

The existing Windows 11 x64 build 26100 record uses Chrome for Testing/ChromeDriver
`154.0.8037.57`. The normal suite passed 258 tests with zero failures or skips:
Core net8.0/net10.0 passed 81 each; Legacy net481/net8.0/net10.0 passed 32 each.
This is a recorded tested pair, not a claim about every M115+ version or a new
validation of the current checkout. See the [execution record](implementation.md#2026-09-24-windows-11-연결-회귀-검증).

Stress, privileged reparse-point tests, Stable-1, SeleniumBase Python browser comparisons,
and real binary patch recipes were not covered. Release acceptance still requires
validation of the exact release candidate commit.

Release evidence must record commit, library/Selenium/Chrome/driver versions, runtime, OS build, hashes, test selection, and skipped/not-run tests. Older alpha versions are not automatically backported.
