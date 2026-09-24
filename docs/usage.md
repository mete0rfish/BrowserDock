# BrowserDock usage guide

> Keep the browser. Swap the driver.

[Overview](../README.md) · [Contributing](../CONTRIBUTING.md) · [Security](../SECURITY.md) · [Changelog](../CHANGELOG.md)

BrowserDock is experimental. The source release version is `0.2.0`; no public NuGet release has been published. The project uses the MIT license.

This unofficial .NET library manages headed Chrome as a separate process on Windows 11 x64, with an independent CDP connection and replaceable Selenium WebDriver attachments.

**Recorded verification (2026-09-24):** the normal Windows 11 x64 suite passed **258 tests** with zero failures or skips using Chrome for Testing/ChromeDriver `154.0.8037.57`. Core net8.0/net10.0 passed 81 each; Legacy net481/net8.0/net10.0 passed 32 each. The recorded solution build had zero warnings or errors. These are existing records, not new verification of the current checkout or a release candidate.

**Not covered by that run:** Stress, privileged reparse-point tests, Stable-1, SeleniumBase Python browser comparisons, and real binary patch recipes. See the [implementation record](implementation.md) and [support policy](support.md).

## Requirements

Use .NET Framework 4.8.1, .NET 8 or .NET 10 on Windows 11 x64, with Chrome and a compatible ChromeDriver executable. Both binaries must be M115 or later with matching `MAJOR.MINOR.BUILD` versions. The library does not download binaries.

| Application | Package / namespace | Async API |
|---|---|---|
| Existing C# 7.3 / .NET Framework 4.8.1 | `BrowserDock.Legacy` | `Task`, ordinary options classes, `try/finally` cleanup |
| Modern C# / .NET 8 or 10 | `BrowserDock` | `ValueTask`, records, `await using` |

Both packages share the same engine and provide DLLs for `net481;net8.0;net10.0`. Framework applications do not need to migrate to .NET 8/10. Framework users should start with the [installation guide and C# 7.3 example](framework481.md). The example below uses modern C#.

```csharp
using BrowserDock;

await using var browser = await Browser.StartAsync(new BrowserOptions
{
    ChromeBinaryPath = @"C:\Chrome\chrome.exe",
    Driver = new() { ExecutablePath = @"C:\Chrome\chromedriver.exe" }
});

await browser.NavigateAsync(new Uri("https://example.com"));
await using var first = await browser.GetWebDriverAsync();
Console.WriteLine(await first.Commands.GetTitleAsync());

await browser.DisconnectWebDriverAsync(); // Chrome, profile, and CDP remain alive.
await browser.NavigateAsync(new Uri("https://example.com"), new()
{
    Mode = NavigationMode.CdpOnly
});
await using var next = await browser.ReconnectWebDriverAsync(); // New session.
Console.WriteLine(next.Generation);
```

If `ChromeBinaryPath` is omitted, the library searches standard installation locations. A driver path is required. The default user Chrome profile is not allowed. A supplied `Profile.Directory` is exclusively locked and preserved after shutdown. Otherwise, the library creates a temporary profile and deletes it after confirming Chrome has exited.

## API rules

- `Standard` navigation requires attached WebDriver. `Detached` disconnects first, navigates through CDP, and creates a new session when `ReconnectAfterNavigation=true`.
- `CdpOnly` navigation requires explicit WebDriver disconnection. It does not disconnect or reconnect implicitly.
- Starting `DisconnectWebDriverAsync()` invalidates previous leases and elements. Old references fail locally even before reconnection succeeds.
- `ElementRef.ReacquireAsync()` returns a new reference from the original locator and frame path. DOM replacement within one session produces `StaleDomElement`; attachment replacement produces `StaleAttachment`.
- Specify iframe locator paths through `FindOptions.FramePath`. Select page targets explicitly with `GetTargetsAsync()` and `SelectTargetAsync()`.
- Use `SelectTargetAsync()` to reconcile a new window's CDP target and WebDriver handle before using it. `SwitchToWindowAsync()` accepts only reconciled handles.
- `WebDriverLease.DisposeAsync()` invalidates that lease only. `Browser.DisposeAsync()` and `StopAsync()` also terminate owned Chrome.
- Public APIs do not return Selenium objects. JavaScript inputs and results must be JSON-compatible; results containing DOM elements are rejected.
- Do not share a lease across concurrent threads. Lifecycle operations are serialized, and disconnection waits for active commands only for a bounded time.
- `ExecuteCdpAsync()` is an advanced API. Browser/Target commands use the browser connection; other commands use the selected page session. Direct tab/browser closure and script execution can have broader effects than the facade's state and URL policies.
- `NetworkIdle` is a best-effort completion condition requiring 500 ms without tracked requests. Persistent requests may cause a navigation timeout.
- Cancellation does not undo navigation already sent to Chrome. Inspect `NavigationCanceledException.BrowserMayHaveAdvanced` or `BrowserDockException.BrowserMayHaveAdvanced`.

## Patching and logging

Patching defaults to `Disabled`. `ValidateOnly` checks exact pattern counts for a supported recipe and uses the original binary. `BinaryCompatibility` preserves the original and creates a separate cached copy with a hash manifest. Missing recipes and validation failures fail explicitly rather than silently falling back to the original. `IDriverPatchStrategy` supplies verified version ranges and length-preserving patterns.

`BrowserOptions.NewDocumentScripts` are registered once per target session and registered again after CDP recovery. No general-purpose fingerprint spoofing script is included.

`RemoveDiscoveredCdcProperties=true` schedules removal at the next document start only for CDC properties actually found in the document and validated against the name pattern. It defaults to disabled and does not guarantee effectiveness.

Supply a `Microsoft.Extensions.Logging.ILogger` through `BrowserOptions.Logger`. Default logs exclude cookies, storage, page content, script results, URLs, and CDP payloads. There is no external telemetry. Slow logging sinks run outside the bounded queue, and overflow is counted.

## Development and verification

```sh
dotnet restore BrowserDock.slnx
dotnet build BrowserDock.slnx --no-restore -m:1
dotnet test tests/BrowserDock.Tests -f net10.0 --no-restore --filter 'TestCategory!=Windows'
dotnet test tests/BrowserDock.FrameworkTests -f net10.0 --no-restore --filter 'TestCategory!=Windows'
```

The solution requires the .NET 10 SDK. Framework DLLs can be built using reference-assembly packages, but execution verification requires the Windows .NET Framework 4.8.1 runtime. The .NET 10 `BrowserDock.FixtureHost` server is a test dependency, not a product runtime dependency.

For actual Windows execution, follow the [test plan](test-plan.md) and [SeleniumBase comparison runner](../tests/seleniumbase-reference/README.md). They compare four behaviors on a common fixture and distinguish required-test skips, zero executed tests, and cleanup failures. Legacy tests target net481/net8.0/net10.0.

```powershell
./scripts/test-windows.ps1 -Chrome C:\Chrome\chrome.exe -Driver C:\Chrome\chromedriver.exe
# Include 100 lifecycle cycles and 20 concurrent instances.
./scripts/test-windows.ps1 -Chrome C:\Chrome\chrome.exe -Driver C:\Chrome\chromedriver.exe -Stress
```

Chrome and ChromeDriver are not bundled in NuGet packages. External Chrome attachment, ownership transfer with keep-alive, headless mode, GUI input, and automatic downloads remain outside the current scope. Success against detection sites, WAFs, or CAPTCHAs is neither guaranteed nor a release gate.

BrowserDock uses the [MIT License](../LICENSE). Copyright (c) 2026 mete0rfish. See [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) for dependencies and implementation provenance.
