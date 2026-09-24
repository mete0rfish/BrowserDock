# Getting started with .NET Framework 4.8.1

Existing C# 7.3 projects should reference `BrowserDock.Legacy`, which also references the `BrowserDock` engine. It wraps the same engine in an API suitable for C# 7.3.

## Prerequisites and project setup

The supported environment is **Windows 11 x64, .NET Framework 4.8.1, and headed Chrome**. Chrome and ChromeDriver must have matching `MAJOR.MINOR.BUILD` versions. Framework 4.8, Windows 10, ARM64, and 32-bit processes are outside this support scope.

The packages have not been published to public NuGet. Build them from the repository first.

```sh
dotnet pack src/BrowserDock -c Release -o .artifacts/packages
dotnet pack src/BrowserDock.Legacy -c Release -o .artifacts/packages
```

Add this directory as a NuGet source in Visual Studio and install `BrowserDock.Legacy` 0.2.0-alpha.1. Keep nuget.org enabled to restore dependencies. Use **PackageReference** in existing projects and build with Visual Studio/MSBuild on Windows. Installation through `packages.config` is outside the verified scope.

Apply the following settings to the executable project's `.csproj`. Use `TargetFramework=net481` for SDK-style projects or `TargetFrameworkVersion=v4.8.1` for classic projects.

```xml
<PropertyGroup>
  <LangVersion>7.3</LangVersion>
  <PlatformTarget>x64</PlatformTarget>
  <Prefer32Bit>false</Prefer32Bit>
  <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
  <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="BrowserDock.Legacy" Version="0.2.0-alpha.1" />
</ItemGroup>
```

Binding redirects tell the executable which DLL version to use when packages request different versions. Apply these settings **to the executable project as well as the library**, and distribute the generated EXE, DLLs, and `.exe.config` together. See [Microsoft's automatic binding redirect guide](https://learn.microsoft.com/en-us/dotnet/framework/configure-apps/how-to-enable-and-disable-automatic-binding-redirection).

Repository builds require the .NET 10 SDK and NuGet restore. Framework reference assemblies are build-only packages, not runtime installers. The application machine needs .NET Framework 4.8.1; **.NET 8/10 is not required to run the product**. Check the registry for `Release >= 533320`. See [Microsoft's version detection guide](https://learn.microsoft.com/en-us/dotnet/framework/install/how-to-determine-which-versions-are-installed).

## C# 7.3 example

```csharp
using System;
using System.Threading.Tasks;
using BrowserDock.Legacy;

public static class BrowserExample
{
    public static async Task RunAsync()
    {
        var browser = await Browser.StartAsync(new BrowserOptions
        {
            ChromeBinaryPath = @"C:\Chrome\chrome.exe",
            Driver = new DriverArtifactOptions
            {
                ExecutablePath = @"C:\Chrome\chromedriver.exe"
            }
        });
        try
        {
            await browser.NavigateAsync(new Uri("https://example.com"));
            var lease = await browser.GetWebDriverAsync();
            try
            {
                Console.WriteLine(await lease.Commands.GetTitleAsync());
            }
            finally { await lease.DisposeAsync(); }
        }
        finally { await browser.DisposeAsync(); }
    }
}
```

`browser` owns the Chrome process. `lease` references the current WebDriver attachment. Disposing a lease invalidates only that reference; disposing the browser terminates Chrome and ChromeDriver.

C# 7.3 has no `await using`, so **await `DisposeAsync()` in `finally`** to clean up even after errors. In WinForms/WPF event handlers, call `await BrowserExample.RunAsync()` without blocking the UI thread with `.Wait()` or `.Result`. This API does not provide synchronous `Dispose()`.

## Disconnect and reconnect

`DisconnectWebDriverAsync()` disconnects WebDriver while preserving Chrome, cookies, localStorage, and page state. `ReconnectWebDriverAsync()` creates a new attachment, so old leases/elements cannot be reused. Acquire a new lease or call the element's `ReacquireAsync()`.

The runnable [complete sample](../samples/Framework481/Program.cs) demonstrates disconnection, reconnection, and element reacquisition.

```powershell
dotnet build samples/Framework481 -c Release
& ./samples/Framework481/bin/Release/net481/Framework481.exe C:\Chrome\chrome.exe C:\Chrome\chromedriver.exe https://example.com
```

Options are copied at invocation. Later changes to the original options or collections do not affect an active operation. Concurrent mutation of collections during copying is unsupported. You may modify settings and use them to start another browser.

Errors use `BrowserDock.Legacy.BrowserDockException` and its subclasses. Stale references after reconnection produce `StaleAttachmentException`; canceled navigation produces `NavigationCanceledException`. Cancellation cannot undo navigation already sent, so inspect `BrowserMayHaveAdvanced`.

Patching defaults to disabled. Legacy `DriverPatchRecipe` accepts inclusive minimum/maximum versions and a list of `PatchPattern` entries. No verified real binary recipe is included.

## Verification boundaries

`BrowserDock.FrameworkTests` builds the same compatibility tests for `net481`, `net8.0`, and `net10.0`. Only the HTTP/WebSocket fixture server, `BrowserDock.FixtureHost`, runs as a separate .NET 10 process, so the Framework test process has no ASP.NET Core reference.

Windows CI checks the Framework runtime before running net481 tests. Real Chrome acceptance requires `scripts/test-windows.ps1` on an interactive Windows 11 desktop. The [verification record](implementation.md) distinguishes local compilation from actual Windows execution.

The classic consumer is `tests/BrowserDock.ClassicConsumer/ClassicConsumer.csproj`. Build packages first, then run the following in Windows Developer PowerShell. It is excluded from the default solution because it requires prebuilt packages.

```powershell
msbuild tests/BrowserDock.ClassicConsumer/ClassicConsumer.csproj -restore -m:1 -p:Configuration=Release "-p:RestoreAdditionalProjectSources=$pwd/.artifacts/packages"
```
