# BrowserDock — experimental alpha

**Keep the browser. Swap the driver.**

Independent Windows Chrome lifecycle management inspired by SeleniumBase UC Mode.
This is not an official SeleniumBase .NET binding. No CAPTCHA or detection-site success is guaranteed.

- **BrowserDock:** modern async API for an owned Chrome process, independent CDP connection,
  and replaceable WebDriver sessions.
- **BrowserDock.Legacy:** Task facade for existing C# 7.3/.NET Framework 4.8.1 applications,
  backed by the same engine.

Both packages target .NET Framework 4.8.1, .NET 8 and .NET 10. Browser hosting requires
Windows 11 x64 and an interactive desktop. Supply Chrome and ChromeDriver M115+
with matching MAJOR.MINOR.BUILD versions. Browser binaries are not bundled or downloaded.

```csharp
using BrowserDock;

await using var browser = await Browser.StartAsync(new BrowserOptions
{
    ChromeBinaryPath = @"C:\Chrome\chrome.exe",
    Driver = new() { ExecutablePath = @"C:\Chrome\chromedriver.exe" }
});
await browser.NavigateAsync(new Uri("https://example.com"));
await browser.DisconnectWebDriverAsync();
await using var lease = await browser.ReconnectWebDriverAsync();
Console.WriteLine(await lease.Commands.GetTitleAsync());
```

Disconnect invalidates old leases and elements. Acquire new references after reconnecting.
Disposing the browser stops its owned processes; use dedicated profiles.
Legacy consumers use Task methods and explicit disposal; see the package project page
for the C# 7.3 sample, support matrix and verification record.

APIs may change during alpha. Headless mode, GUI input, automatic binary downloads
and verified binary patch recipes are outside the current scope. Patching is disabled
by default. Common tests alone do not establish real Windows browser compatibility.

The package project page contains current documentation and release notes.
Dependency and provenance information is included in THIRD-PARTY-NOTICES.md.
