# BrowserDock

> Keep the browser. Swap the driver.

[Usage guide](docs/usage.md) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md) · [Changelog](CHANGELOG.md)

**Experimental.** BrowserDock manages a separately launched Chrome process, an independent CDP connection, and replaceable WebDriver sessions on Windows. Its lifecycle design is inspired by SeleniumBase UC Mode. This independent project is not an official SeleniumBase .NET binding.

Keep Chrome alive while disconnecting WebDriver, then create a new session against the same browser. Old leases and elements fail locally after disconnection; callers explicitly acquire new references.

## Status and requirements

No public NuGet release has been published by this project yet. Build from source for evaluation. The source release version is `0.2.0`. APIs and behavior may change during 0.x; breaking changes are documented in the changelog.

| Requirement | Current scope |
|---|---|
| Browser hosting | Windows 11 x64, interactive desktop, headed Chrome |
| Binaries | Caller-supplied Chrome/ChromeDriver, M115+, matching `MAJOR.MINOR.BUILD` |
| Modern API | `BrowserDock`: .NET 8/10, ValueTask, records, await using |
| Existing C# applications | `BrowserDock.Legacy`: Task facade, including C# 7.3/.NET Framework 4.8.1 |
| Build | .NET 10 SDK; Framework reference assemblies restore through NuGet |

Both packages target `net481;net8.0;net10.0` and share one engine. The .NET 10 fixture server is a test dependency, not a runtime dependency for Framework applications.

Recorded verification (2026-09-24): the normal Windows 11 x64 suite passed **258 tests**, with zero failures or skips, using Chrome for Testing/ChromeDriver `154.0.8037.57`: Core net8.0/net10.0 passed 81 each; Legacy net481/net8.0/net10.0 passed 32 each. The recorded solution build had zero warnings or errors. These are existing execution records, not a new validation of the current checkout or a release candidate.

**Not covered by that run:** Stress, privileged reparse-point tests, Stable-1, SeleniumBase Python browser comparisons, and real binary patch recipes. See the [verification record](docs/implementation.md) and [support policy](docs/support.md).

## Build and try

```sh
dotnet restore BrowserDock.slnx
dotnet build BrowserDock.slnx --no-restore -m:1
dotnet run --project samples/Lifecycle -- C:\Chrome\chrome.exe C:\Chrome\chromedriver.exe https://example.com
```

Run the browser sample on Windows 11 x64. For another application, use a project reference to `src/BrowserDock/BrowserDock.csproj`, or build local packages using the [release guide](docs/releasing.md). Framework users should follow the [C# 7.3 installation and sample](docs/framework481.md).

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

await browser.DisconnectWebDriverAsync(); // Chrome, profile, CDP remain alive.
await browser.NavigateAsync(new Uri("https://example.com"), new()
{
    Mode = NavigationMode.CdpOnly
});
await using var next = await browser.ReconnectWebDriverAsync(); // New session.
Console.WriteLine(next.Generation);
```

## Lifecycle rules

- `Standard` navigation needs an attached WebDriver. `Detached` disconnects first and navigates through CDP; set `ReconnectAfterNavigation=true` to attach afterwards.
- `CdpOnly` requires explicit disconnection and does not reconnect implicitly.
- Disconnect invalidates existing leases and elements. `ReacquireAsync()` creates a new element from its original locator and frame path. Attachment changes and DOM staleness have different errors.
- Select targets explicitly with `GetTargetsAsync()` and `SelectTargetAsync()`. Ambiguous targets are errors.
- Disposing a lease invalidates that lease. Disposing/stopping the browser shuts down its owned Chrome and driver.
- A supplied profile is exclusively locked and preserved. An automatic temporary profile is deleted after Chrome exits. Default user profiles are rejected.
- Do not share a lease between concurrent threads. Lifecycle operations are serialized per browser.
- `NetworkIdle` is best-effort: 500 ms without tracked outstanding requests. Persistent requests can time out.
- Cancellation does not undo navigation already sent to Chrome. Inspect `BrowserMayHaveAdvanced` on the exception.

## Boundaries

Headless mode, GUI input, CAPTCHA solving, automatic binary downloads, external Chrome ownership transfer, and verified binary patch recipes are outside the current scope. No detection-site, WAF, or CAPTCHA success is promised.

Patching is disabled by default. Opt-in strategies validate exact patterns and preserve the vendor binary; synthetic tests do not prove real binary compatibility. New-document scripts and discovered CDC-property removal are optional.

The facade does not expose raw Selenium objects. `ExecuteCdpAsync()` is advanced: Browser/Target commands and script evaluation can have broader effects than facade operations. Use dedicated profiles and trusted commands.

Default logging does not collect cookies, storage, page content, script results, URLs, or CDP payloads. There is no external telemetry. Test artifacts may contain local paths and fixture content; review them before sharing.

## Test and contribute

```sh
dotnet test tests/BrowserDock.Tests -f net10.0 --no-restore --filter 'TestCategory!=Windows'
dotnet test tests/BrowserDock.FrameworkTests -f net10.0 --no-restore --filter 'TestCategory!=Windows'
python3 -m unittest discover -s tests/seleniumbase-reference -p test_compare.py -v
```

On a prepared Windows 11 desktop:

```powershell
./scripts/test-windows.ps1 -Chrome C:\Chrome\chrome.exe -Driver C:\Chrome\chromedriver.exe
# Add -Stress for 100 lifecycle cycles and 20 concurrent browsers.
```

Read the [test plan](docs/test-plan.md), [SeleniumBase reference runner](tests/seleniumbase-reference/README.md), [architecture](docs/architecture.md), and [contribution guide](CONTRIBUTING.md). Include a minimal local reproduction and exact versions in bug reports. Use [SECURITY.md](SECURITY.md) for vulnerabilities.

## License and provenance

BrowserDock is licensed under the [MIT License](LICENSE). Copyright (c) 2026 mete0rfish. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for dependency and source provenance.
