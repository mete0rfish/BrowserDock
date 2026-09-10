# UcDotNet

Windows 11 x64의 headed Chrome을 별도 프로세스로 관리하고, 독립 CDP 연결과 교체 가능한 Selenium WebDriver attachment를 제공하는 비공식 .NET 라이브러리입니다.

**현재 상태:** MVP 구현 및 자동화 시험 코드가 있습니다. Windows 실제 브라우저 수용 시험은 이 개발 환경에서 실행하지 않았습니다. 검증 상태와 제한은 [구현 및 검증 기록](docs/implementation.md)을 확인하세요. 검증된 ChromeDriver 패치 recipe는 아직 포함하지 않습니다.

## 사용

.NET 8 또는 .NET 10과 Windows 11 x64, Chrome 및 호환 ChromeDriver 실행 파일이 필요합니다. Chrome/ChromeDriver는 M115 이상이며 `MAJOR.MINOR.BUILD`가 일치해야 합니다. 라이브러리는 바이너리를 자동 다운로드하지 않습니다.

```csharp
using UcDotNet;

await using var browser = await UcBrowser.StartAsync(new BrowserOptions
{
    ChromeBinaryPath = @"C:\Chrome\chrome.exe",
    Driver = new() { ExecutablePath = @"C:\Chrome\chromedriver.exe" }
});

await browser.NavigateAsync(new Uri("https://example.com"));
await using var first = await browser.GetWebDriverAsync();
Console.WriteLine(await first.Commands.GetTitleAsync());

await browser.DisconnectWebDriverAsync(); // Chrome, profile, CDP 유지
await browser.NavigateAsync(new Uri("https://example.com"), new()
{
    Mode = NavigationMode.CdpOnly
});
await using var next = await browser.ReconnectWebDriverAsync(); // 새 session
Console.WriteLine(next.Generation);
```

`ChromeBinaryPath`를 생략하면 표준 설치 위치를 탐색합니다. 드라이버 경로는 필수입니다. 사용자 기본 Chrome 프로필은 사용할 수 없습니다. `Profile.Directory`를 지정하면 프로필을 독점 잠그고 종료 후에도 보존합니다. 생략하면 임시 프로필을 만들고 Chrome 종료 확인 후 삭제합니다.

## API 사용 규칙

- `Standard` navigation은 연결된 WebDriver가 필요합니다. `Detached`는 먼저 연결을 해제한 뒤 CDP로 이동하며 `ReconnectAfterNavigation=true`일 때 새 session을 만듭니다.
- `CdpOnly` navigation은 명시적으로 WebDriver 연결을 해제한 상태에서 사용합니다. 암묵적으로 연결을 끊거나 복구하지 않습니다.
- `DisconnectWebDriverAsync()` 시작 시 이전 lease와 요소가 무효화됩니다. reconnect 성공 전에도 이전 참조는 로컬에서 실패합니다.
- `ElementRef.ReacquireAsync()`는 원 locator와 frame 경로로 새 참조를 반환합니다. 같은 session의 DOM 교체 오류는 `StaleDomElement`, attachment 변경은 `StaleAttachment`입니다.
- `FindOptions.FramePath`로 iframe locator 경로를 지정합니다. 여러 page target은 `GetTargetsAsync()`와 `SelectTargetAsync()`로 명시적으로 선택합니다.
- 새 창은 `SelectTargetAsync()`로 CDP target과 WebDriver handle을 대응시킨 뒤 사용합니다. `SwitchToWindowAsync()`는 대응이 확인된 handle만 허용합니다.
- `WebDriverLease.DisposeAsync()`는 해당 lease만 무효화합니다. `UcBrowser.DisposeAsync()`와 `StopAsync()`는 소유 Chrome까지 종료합니다.
- Selenium 객체는 공개 API로 반환하지 않습니다. JavaScript 입력·결과는 JSON 호환 값만 허용하며 DOM 요소가 포함된 결과는 거부합니다.
- lease를 여러 스레드에서 공유하지 마세요. lifecycle 작업은 직렬화하며, 실행 중 명령은 disconnect 시 제한 시간만 기다립니다.
- `ExecuteCdpAsync()`는 고급 API입니다. Browser/Target 명령은 browser 연결, 그 외 명령은 선택된 page session으로 보냅니다. 직접 tab·browser 종료나 script 실행을 요청하면 일반 facade의 상태·URL 정책보다 넓은 영향을 줄 수 있습니다.
- `NetworkIdle`은 500ms 동안 추적 중 요청이 없는 best-effort 완료 조건입니다. 장기 요청이 있는 페이지는 navigation timeout에 도달할 수 있습니다.
- 취소가 이미 전달된 navigation을 되돌리지는 않습니다. `NavigationCanceledException.BrowserMayHaveAdvanced` 또는 `UcException.BrowserMayHaveAdvanced`를 확인하세요.

## 패치와 로그

패치 기본값은 `Disabled`입니다. `ValidateOnly`는 지원 recipe의 정확한 패턴 수를 검사하고 원본을 사용합니다. `BinaryCompatibility`는 원본을 보존하며 별도 캐시 사본과 hash manifest를 만듭니다. 지원 recipe가 없거나 검증에 실패하면 원본으로 조용히 대체하지 않고 실패합니다. `IDriverPatchStrategy`는 검증된 버전 범위와 길이를 보존하는 패턴을 제공하는 확장점입니다.

`BrowserOptions.NewDocumentScripts`는 대상 session마다 중복 없이 등록되며 CDP 복구 후 재등록됩니다. 범용 fingerprint 위장 스크립트는 포함하지 않습니다.

`RemoveDiscoveredCdcProperties=true`는 실제 문서에서 발견하고 이름 패턴을 검증한 CDC 속성만 다음 문서 시작 시 제거하도록 등록합니다. 기본값은 비활성화이며 효과를 보장하지 않습니다.

`BrowserOptions.Logger`에 `Microsoft.Extensions.Logging.ILogger`를 전달할 수 있습니다. 기본 로그에는 cookie, storage, page 내용, script 결과, URL, CDP payload를 쓰지 않습니다. 외부 telemetry는 없습니다. 느린 logging sink는 제한된 큐 밖에서 실행하며 overflow를 집계합니다.

## 개발과 검증

```sh
dotnet restore UcDotNet.slnx
dotnet build UcDotNet.slnx --no-restore -m:1
dotnet test tests/UcDotNet.Tests -f net10.0 --no-restore --filter 'TestCategory!=Windows'
```

Windows 실제 시험:

```powershell
./scripts/test-windows.ps1 -Chrome C:\Chrome\chrome.exe -Driver C:\Chrome\chromedriver.exe
# 100회 수명주기, 20개 병렬 인스턴스까지 포함
./scripts/test-windows.ps1 -Chrome C:\Chrome\chrome.exe -Driver C:\Chrome\chromedriver.exe -Stress
```

실제 Chrome 및 ChromeDriver는 NuGet에 번들하지 않습니다. 외부 Chrome attach, keep-alive 소유권 이전, headless, GUI 입력, 자동 다운로드는 후속 범위입니다. 특정 탐지 사이트, WAF 또는 CAPTCHA 통과를 보장하거나 release gate로 사용하지 않습니다.

공개 배포용 프로젝트 라이선스는 아직 결정하지 않았습니다. 의존성과 구현 출처는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)에 기록합니다.
