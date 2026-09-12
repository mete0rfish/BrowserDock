# .NET Framework 4.8.1에서 시작하기

기존 C# 7.3 프로젝트는 `BrowserDock.Legacy`를 참조하면 됩니다. 이 패키지는 브라우저 제어 엔진인 `BrowserDock`도 함께 참조합니다. 같은 엔진을 C# 7.3에서 쓰기 쉬운 형태로 감싼 API입니다.

## 준비와 프로젝트 설정

실행 환경은 **Windows 11 x64, .NET Framework 4.8.1, 화면에 창을 띄우는 Chrome**입니다. Chrome과 ChromeDriver의 `MAJOR.MINOR.BUILD`가 같아야 합니다. Framework 4.8, Windows 10, ARM64, 32비트 프로세스는 이번 지원 범위에 포함하지 않습니다.

현재 패키지는 공개 NuGet에 게시하지 않았습니다. 저장소에서 먼저 패키지를 생성합니다.

```sh
dotnet pack src/BrowserDock -c Release -o .artifacts/packages
dotnet pack src/BrowserDock.Legacy -c Release -o .artifacts/packages
```

Visual Studio의 NuGet 패키지 소스에 이 폴더를 추가하고 `BrowserDock.Legacy` 0.2.0-alpha.1을 설치합니다. 의존성 복원을 위해 nuget.org 소스도 유지합니다. 기존 프로젝트는 **PackageReference 방식**을 사용하고 Windows의 Visual Studio/MSBuild로 빌드하세요. `packages.config` 방식의 설치는 이번 검증 범위에 포함하지 않습니다.

실행 프로젝트의 `.csproj`에는 다음 설정을 적용합니다. SDK 형식 프로젝트는 `TargetFramework`를 `net481`로, 기존 형식 프로젝트는 `TargetFrameworkVersion`을 `v4.8.1`로 설정합니다.

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

`binding redirect`는 여러 패키지가 서로 다른 버전의 같은 DLL을 요구할 때, 사용할 버전을 실행 프로그램의 설정 파일에 적는 기능입니다. **라이브러리 프로젝트뿐 아니라 실제 실행 EXE 프로젝트에도** 위 설정을 넣고, 출력된 EXE·DLL·`.exe.config`를 함께 배포하세요. [Microsoft 자동 binding redirect 문서](https://learn.microsoft.com/en-us/dotnet/framework/configure-apps/how-to-enable-and-disable-automatic-binding-redirection)

저장소 빌드에는 .NET 10 SDK와 NuGet 복원이 필요합니다. Framework용 참조 어셈블리는 빌드 때만 사용하는 패키지이므로 사용자 PC의 runtime을 설치해 주지 않습니다. 실행 PC에는 .NET Framework 4.8.1이 필요하며, **제품을 실행하기 위해 .NET 8/10을 설치할 필요는 없습니다.** Framework 설치 여부는 레지스트리 `Release >= 533320`으로 확인할 수 있습니다. [Microsoft 버전 확인 문서](https://learn.microsoft.com/en-us/dotnet/framework/install/how-to-determine-which-versions-are-installed)

## C# 7.3 예제

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

`browser`는 Chrome 프로세스의 소유자입니다. `lease`는 현재 WebDriver 연결을 쓸 수 있는 참조입니다. lease를 정리하면 그 참조만 무효화되고, browser를 정리하면 Chrome과 ChromeDriver가 종료됩니다.

C# 7.3에는 `await using`이 없으므로 **`finally`에서 `DisposeAsync()`를 await**합니다. 오류가 나도 정리 코드가 실행됩니다. WinForms/WPF 이벤트에서도 `await BrowserExample.RunAsync()`처럼 호출하세요. `.Wait()`나 `.Result`로 UI 스레드를 막지 마세요. 이 API는 동기 `Dispose()`를 제공하지 않습니다.

## 연결 해제와 다시 연결

`DisconnectWebDriverAsync()`는 Chrome을 유지하면서 WebDriver만 끊습니다. 쿠키·localStorage·화면 상태는 Chrome에 남습니다. `ReconnectWebDriverAsync()`는 새 연결을 만들기 때문에 기존 lease와 요소 참조는 사용할 수 없습니다. 새 lease를 받거나 요소의 `ReacquireAsync()`를 호출하세요.

실행 가능한 [전체 예제](../samples/Framework481/Program.cs)는 연결 해제, 재연결, 요소 재획득까지 보여 줍니다.

```powershell
dotnet build samples/Framework481 -c Release
& ./samples/Framework481/bin/Release/net481/Framework481.exe C:\Chrome\chrome.exe C:\Chrome\chromedriver.exe https://example.com
```

옵션은 호출 시 복사합니다. 호출 후 원래 옵션이나 컬렉션을 바꿔도 진행 중인 작업에는 반영되지 않습니다. 복사 중 다른 스레드가 동시에 컬렉션을 수정하는 사용법은 지원하지 않습니다. 같은 설정을 바꿔 다른 브라우저를 시작하는 것은 가능합니다.

예외는 `BrowserDock.Legacy.BrowserDockException`과 하위 타입으로 전달됩니다. 재연결 후 오래된 참조는 `StaleAttachmentException`, 취소된 이동은 `NavigationCanceledException`입니다. 취소는 이미 시작한 페이지 이동을 되돌리지 않으므로 `BrowserMayHaveAdvanced`도 확인하세요.

패치 기본값은 비활성화입니다. Legacy의 `DriverPatchRecipe`는 최소·최대 버전(양 끝 포함)과 `PatchPattern` 목록을 받습니다. 검증된 실물 recipe는 포함하지 않습니다.

## 검증 구분

`BrowserDock.FrameworkTests`는 `net481`과 `net10.0`으로 같은 호환성 시험을 빌드합니다. HTTP/WebSocket 응답을 만드는 `BrowserDock.FixtureHost`만 별도 .NET 10 프로세스로 실행합니다. 따라서 실제 Framework 테스트 프로세스에는 ASP.NET Core 참조가 들어가지 않습니다.

Windows CI는 Framework runtime을 확인한 뒤 `net481` 테스트를 실행합니다. 실제 Chrome 수용 시험은 Windows 11의 대화형 데스크톱에서 `scripts/test-windows.ps1`을 실행해야 합니다. 로컬 빌드 성공과 Windows 실제 실행 결과는 [검증 기록](implementation.md)에 구분해서 기록합니다.

기존 형식 프로젝트의 패키지 소비 시험은 `tests/BrowserDock.ClassicConsumer/ClassicConsumer.csproj`입니다. 패키지 생성 후 Windows의 Developer PowerShell에서 다음과 같이 빌드합니다. 패키지를 먼저 만들어야 하므로 기본 솔루션에는 포함하지 않습니다.

```powershell
msbuild tests/BrowserDock.ClassicConsumer/ClassicConsumer.csproj -restore -m:1 -p:Configuration=Release "-p:RestoreAdditionalProjectSources=$pwd/.artifacts/packages"
```
