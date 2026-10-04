# Opt-in UC launch and document scripts

Issue #30 adds a versioned policy, `browserdock-uc-v1`, over existing Chrome
ownership and script primitives. It is not a verified SeleniumBase-equivalent
profile. Windows execution and same-candidate upstream comparison remain pending.
`BrowserOptions.UcProfile` defaults to null; calling `browser.Uc` does not enable it.

## Core and Legacy

```csharp
await using var browser = await Browser.StartAsync(new BrowserOptions
{
    ChromeBinaryPath = chromePath,
    Driver = new DriverArtifactOptions { ExecutablePath = driverPath },
    UcProfile = new UcLaunchProfile
    {
        Language = "en-US",
        NewDocumentScripts = new[] { "globalThis.applicationBootstrap = true" }
    }
});
Console.WriteLine(browser.UcProfile); // redacted effective policy
await browser.Uc.OpenWithDisconnectAsync(url);
```

Legacy uses the same property/type names with mutable setters and `Task` methods.
Its snapshot preserves whether the caller explicitly set CDC removal. C# 7.3:

```csharp
var browser = await BrowserDock.Legacy.Browser.StartAsync(
    new BrowserDock.Legacy.BrowserOptions
    {
        ChromeBinaryPath = chromePath,
        Driver = new BrowserDock.Legacy.DriverArtifactOptions { ExecutablePath = driverPath },
        UcProfile = new BrowserDock.Legacy.UcLaunchProfile { Language = "en-US" }
    });
try { await browser.Uc.OpenWithDisconnectAsync(url); }
finally { await browser.DisposeAsync(); }
```

The [modern sample](../samples/UcLifecycle/Program.cs) and
[C# 7.3/net481 sample](../samples/UcFramework481/Program.cs) accept an optional
fourth argument `--profile`. Without it they keep their original behavior.

## Resolution and conflicts

All option collections, including nested profile scripts, are copied before the
first asynchronous startup operation. Resolution and structural conflict checks
precede driver validation, cache/profile allocation and Chrome creation. Mutable
strategy objects and the logger retain their existing caller-owned contract.

| Setting | Version 1 behavior |
|---|---|
| `Version` | Only 1; unknown versions fail with ConfigurationError. |
| `Language` | Explicit, single language tag; default `en-US`. Produces one `--lang` argument. This does not promise an Accept-Language header or navigator-language result without browser verification. |
| Raw `--lang` | Same value is coalesced; differing, missing or whitespace-form values fail. Unrelated explicit arguments retain order. |
| `RemoveDiscoveredCdcProperties` | Profile default true. An explicitly set root option must agree, including explicit false. An omitted root option inherits the profile. Disable through the profile property. |
| `--enable-automation` | Conflicts with this profile. Managed/unsupported port, profile, proxy, headless and sandbox flags retain their existing rejection rules. |
| `NewDocumentScripts` | CDC cleanup first when names are known, then profile scripts, then root scripts. Identical sources are registered once in first-occurrence order. Null/blank scripts fail. Each caller source is sent verbatim in a separate CDP registration. |
| `RequireBinaryPatch` | Default false. When true, requires BinaryCompatibility plus an explicit strategy. Missing/unsupported recipes fail; no fallback. Version/hash/recipe validation remains before Chrome/profile creation. |
| `PatchMode` | Remains caller-selected. Enabling this profile does not select or invent a patch recipe. Real recipes belong to #31. |

`Browser.UcProfile` exposes `Id`, `Language`, CDC-removal policy, patch requirement,
effective patch mode and the number of distinct static document scripts. The
script count excludes the dynamically discovered CDC cleanup script. The Core
and Legacy snapshots contain no raw arguments, directory paths or script bodies.
Raw custom scripts may contradict a profile at runtime; arbitrary JavaScript
semantics are not inspected or classified as supported fingerprint changes.

## Document and session boundaries

- The initial `about:blank` target exists before CDP attachment, but document
  creation can overlap registration. This initial document is outside the timing
  guarantee; the Windows fixture records whether scripts ran there. Registration
  never explicitly replays scripts into an existing document. The first subsequent
  controlled document is the guaranteed document-start boundary.
- ChromeDriver can add properties after initial CDP preparation. The profile
  refreshes discovery after WebDriver target binding, before high-level Standard,
  Detached and CdpOnly navigation, and before raw `Page.navigate` dispatch. The
  existing pre-disconnect preparation remains in use.
- Only names matching BrowserDock's existing strict CDC-name validation are
  retained. Known names survive cleaned documents, tab replacement and transport
  recovery. Scope remains the existing bounded prototype inspection, not a claim
  to remove every upstream property or change every fingerprint.
- A changed cleanup source replaces this session's ordered set: remove its owned
  script IDs, then register cleanup/profile/caller sources in order. Unchanged
  sets make no additional registration calls during ordinary preparation. A new
  WebDriver attachment refreshes the order even when sources are unchanged, since
  that driver can install its own initialization scripts. Independent raw-CDP registrations
  are not removed. Do not concurrently bypass the controller to mutate its scripts.
- Cancellation or failure during registration can have an uncertain remote result.
  The affected transport is invalidated rather than retrying against the same
  session. A subsequent normal health probe must recover it. Caller cancellation
  remains cancellation; this policy does not replay the failed navigation.
- Reconnection does not replay caller scripts in the current document. Library
  tab replacement creates `about:blank`, prepares scripts, then navigates. Session
  detachment/target closure discards local registration state; recovery reinstalls
  scripts on fresh sessions. Profile mode requires the CDP script-removal method.
- An externally navigated popup or a raw `Target.createTarget` with a nonblank URL
  may execute its first document before the controller attaches. That initial
  document is outside this profile's guarantee. Select it explicitly and perform
  a controlled subsequent navigation to use this policy. BrowserDock does not
  install global auto-attach/paused-target behavior in this change.

## Inventory against pinned SeleniumBase

Reference: SeleniumBase 4.53.7, commit
`4ee7dfc4ae83c19385f5ac129f2cda0cfa863d80`,
[`undetected/__init__.py`](https://github.com/seleniumbase/SeleniumBase/blob/4ee7dfc4ae83c19385f5ac129f2cda0cfa863d80/seleniumbase/undetected/__init__.py).
This is an independent composition of BrowserDock's existing primitives, not a
translation of that constructor. The full wrapper adds settings too; this table
does not claim to reproduce every wrapper option.

| Area | Pinned upstream / BrowserDock policy | Status |
|---|---|---|
| Debugging endpoint | Upstream chooses 9222/free/explicit port before launch; BrowserDock keeps OS-assigned port 0, loopback and exact listener PID verification. | Intentional deviation |
| Initial page/profile | BrowserDock keeps owned temporary or explicitly supplied locked profile, initial about:blank and marker-checked cleanup. No profile-directory override or mutation of a caller's Preferences file. | Intentional deviation |
| Welcome flags | Both use no-first-run/no-default-browser-check. BrowserDock does not add upstream's no-service-autorun/password-store/profile-directory/log-level defaults. | Partial static overlap; other defaults intentionally not adopted |
| Language | Upstream derives a locale when absent. BrowserDock version 1 defaults explicitly to en-US; caller may choose another language tag. | Intentional deviation; browser-visible results unverified |
| Debugger capabilities | BrowserDock still attaches with debuggerAddress and PageLoadStrategy.None; no launch-only detach capability or new performance-log capability. | Intentional deviation |
| CDC discovery | Upstream scans/removes before get. BrowserDock retains its stricter validated names, refreshes at explicit boundaries, and orders owned script registrations. | Intentional deviation; real-browser equivalence unverified |
| Patching | BrowserDock requires explicit immutable-source strategies; there is no bundled verified UC recipe. | #31, not implemented by #30 |

No website-specific policy, CAPTCHA helper, navigator spoofing or detection-bypass
claim is introduced. Port/profile ownership and concurrent-instance isolation
retain their existing implementation; profile-specific Windows evidence is pending.

## Verification boundary

Portable tests cover Core/Legacy resolution, explicit false versus absence,
immutability, ordering, capability checks, late discovery, replacement, recovery,
session cleanup and uncertain registration. These are contracts, not browser parity.
`UcLaunchProfileBrowserTests` compiles into both suites and observes initial blank,
document-start order, CDC cleanup, ordinary/detached navigation, replacement,
reattachment and socket recovery on Windows. It has not been executed for this change.
Windows CI was excluded from this work at the maintainer's request. #41 remains
open; #30's Windows acceptance and #36's differential evidence remain outstanding.
