<!-- browserdock-ko-wiki:v1 -->
# 09. 상황별 API 사용법과 Legacy 지원

> 목표: 하려는 작업의 사전조건과 실행 후 상태를 알고 API를 선택합니다.

이 장의 현대 C# 예제는 `using BrowserDock;`를 사용하며, 별도 표시가 없으면 정상 시작한 `browser` 안에서 실행하는 **서로 독립적인 부분 예제**입니다. 모든 예제를 위에서 아래로 이어 붙이는 프로그램은 아닙니다.

## 이동 모드 선택하기

| 모드 | 일반적인 시작 상태 | 이동 경로 | 정상 완료 후 |
|---|---|---|---|
| `Standard` | `WebDriverAttached` | WebDriver로 시작, CDP로 완료 관찰 | 연결 유지 |
| `Detached` | attached 또는 `CdpOnly` | 필요 시 연결 해제 후 CDP | 기본 `CdpOnly`, 선택적으로 재연결 |
| `CdpOnly` | 명시적으로 연결을 해제한 상태 | CDP | `CdpOnly` 유지 |

첫 navigation은 기본 모드로 시작할 수 있습니다.

```csharp
await browser.NavigateAsync(new Uri("http://127.0.0.1:8765/"));
```

연결 해제와 이동 후 재연결을 한 작업으로 표현하려면 `Detached`를 선택합니다. 이 작업 이전에 얻었던 lease는 완료 후에도 다시 유효해지지 않습니다.

```csharp
await browser.NavigateAsync(new Uri("http://127.0.0.1:8765/"),
    new NavigationOptions
    {
        Mode = NavigationMode.Detached,
        ReconnectAfterNavigation = true
    });
await using var lease = await browser.GetWebDriverAsync();
```

상태 전환을 직접 드러내려면 분리해서 호출합니다. `CdpOnly`는 암묵적으로 WebDriver를 해제하지 않습니다.

```csharp
await browser.DisconnectWebDriverAsync();
await browser.NavigateAsync(new Uri("http://127.0.0.1:8765/"),
    new NavigationOptions { Mode = NavigationMode.CdpOnly });
await using var lease = await browser.ReconnectWebDriverAsync();
```

## 요소 찾기와 조작

[03장의 로컬 페이지](ko-Getting-Started.md)가 열려 있고 WebDriver가 attached인 상태에서 실행합니다.

```csharp
await using var lease = await browser.GetWebDriverAsync();
var input = await lease.Commands.FindAsync(Locator.Id("name"));
await input.ClearAsync();
await input.SendKeysAsync("BrowserDock");
var save = await lease.Commands.FindAsync(Locator.Id("save"));
await save.ClickAsync();
var status = await lease.Commands.FindAsync(Locator.Id("status"));
Console.WriteLine(await status.GetTextAsync());
```

이 fixture의 버튼 처리는 동기 JavaScript입니다. 실제 비동기 웹 애플리케이션에서는 클릭 반환 직후 업무 결과까지 완료되었다고 가정하지 말고, 목적에 맞는 완료 상태를 관찰하세요.

## 스크립트와 저장소

같은 로컬 페이지에서 저장 버튼을 누른 뒤, 현재 lease로 저장된 문자열을 읽을 수 있습니다.

```csharp
var stored = await lease.Commands.ExecuteScriptAsync(
    "return localStorage.getItem('browserdock-study-name')");
Console.WriteLine(stored.ValueKind == System.Text.Json.JsonValueKind.Null
    ? "저장된 값이 없습니다."
    : stored.GetString());
```

스크립트 입력과 결과는 JSON 호환 값으로 제한됩니다. DOM 요소나 raw Selenium 객체를 반환해 공개 경계를 우회하지 않습니다. localStorage 실습은 `data:` URL 대신 같은 loopback HTTP origin을 사용하세요.

## 탭 선택과 iframe 탐색

`GetTargetsAsync()`가 반환한 후보를 제목이나 URL 등 애플리케이션의 기준으로 확인하고, 정확히 하나의 대상이 결정되면 `SelectTargetAsync(target.Key)`로 선택합니다. `First()`나 마지막 배열 항목을 무조건 선택하지 마세요.

iframe 내부 요소는 `FindOptions.FramePath`로 탐색 경로를 명시합니다. 다음 코드는 해당 iframe과 input이 실제 존재하는 별도 페이지를 전제로 하며, 03장의 단순 fixture에는 그 구조가 없습니다.

```csharp
var field = await lease.Commands.FindAsync(Locator.Id("email"),
    new FindOptions
    {
        FramePath = new[] { Locator.Css("iframe#account") }
    });
```

`SwitchToWindowAsync()`는 대응이 확인된 handle만 허용합니다. CDP 대상 선택과 WebDriver 창 선택을 별개의 무관한 상태처럼 관리하지 않습니다.

## 취소와 제한 시간

`BrowserOptions.Timeouts`로 시작·연결·명령·이동·정리 단계의 제한 시간을 구성합니다. timeout을 늘리는 것은 원인 확인을 대신하지 않습니다.

취소 또는 timeout 뒤에는 `BrowserMayHaveAdvanced`를 확인합니다. navigation이 이미 전달되었으면 예외가 발생했어도 페이지가 이동했을 수 있습니다. 사용자의 업무 동작을 재시도하기 전에 현재 페이지와 결과를 확인하세요.

## .NET Framework와 C# 7.3

`BrowserDock.Legacy`는 일반 설정 클래스와 `Task` API를 제공합니다. 같은 엔진을 사용하므로 lifecycle과 참조 안전성 규칙은 동일하게 적용됩니다. 구문만 현대 예제에서 가져오면 C# 7.3에서 컴파일되지 않을 수 있습니다.

다음은 C# 7.3에서 사용할 수 있는 보조 클래스 예제입니다. 호출자는 `RunAsync()`를 자신의 비동기 진입점에서 기다립니다.

```csharp
using System;
using System.Threading.Tasks;
using BrowserDock.Legacy;

internal static class LegacyExample
{
    public static async Task RunAsync(string chrome, string driver, string url)
    {
        Browser browser = null;
        try
        {
            browser = await Browser.StartAsync(new BrowserOptions
            {
                ChromeBinaryPath = chrome,
                Driver = new DriverArtifactOptions { ExecutablePath = driver }
            });
            await browser.NavigateAsync(new Uri(url));
            var lease = await browser.GetWebDriverAsync();
            try { Console.WriteLine(await lease.Commands.GetTitleAsync()); }
            finally { await lease.DisposeAsync(); }
        }
        finally { if (browser != null) await browser.DisposeAsync(); }
    }
}
```

설치와 binding 관련 상세 내용은 기존 Framework 문서를 사용합니다. 공개 NuGet 게시 전 상태를 확인하지 않고 `dotnet add package`만으로 설치된다고 안내하지 않습니다.

**근거:** [Models.cs](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Models.cs), [navigation 구현](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Browser.cs), [Framework 안내](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/docs/framework481.md), [Legacy 샘플](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/samples/Framework481/Program.cs)

[← WebDriver와 참조](ko-WebDriver-References.md) · [목차](ko-Home.md) · [다음: 자원 안전성 →](ko-Resource-Safety.md)
