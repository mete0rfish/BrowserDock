<!-- browserdock-ko-wiki:v1 -->
# 06. 브라우저의 시작부터 종료까지

> 핵심 질문: 연결 해제는 왜 종료가 아니며, 재연결은 무엇을 새로 만드나요?

## 먼저 정상 흐름을 읽습니다

성공한 시작과 한 번의 연결 교체에서 주요 상태는 다음 순서로 진행합니다. 오류와 종료 경로는 이 흐름에서 별도로 분기합니다.

```text
Stopped → StartingChrome → ChromeReady
        → AttachingWebDriver → WebDriverAttached
        → Disconnecting → CdpOnly
        → Reattaching → WebDriverAttached
```

`Faulted`와 `Disposing`도 공개 상태에 포함됩니다. 상태 이름만으로 모든 자원이 정리되었다고 판단하지 말고 `Health`와 정리 결과를 함께 확인하세요.

## 시작: 자원을 순서대로 확보합니다

`Browser.StartAsync()`는 설정과 환경을 검사한 뒤 `Browser` 인스턴스를 만듭니다. Chrome 경로와 Chrome/ChromeDriver 버전을 확인하고, patch 모드에 맞는 드라이버 경로를 준비합니다.

다음으로 프로필을 확보하고 Chrome을 시작합니다. `DevToolsActivePort`와 endpoint 발견을 거쳐 CDP를 초기화하고 건강성을 확인합니다. Chrome이 준비되면 WebDriver attachment를 만들고 제어할 페이지를 대응시킵니다.

공개 `StartAsync()`는 정상적으로 WebDriver가 연결된 상태까지 진행한 뒤 반환합니다. Chrome만 시작하고 호출자가 나중에 최초 attachment를 만드는 API로 오해하지 마세요.

시작 도중 실패하면 `StopAsync()`로 이미 생성한 자원의 정리를 시도합니다. 원래 실패와 정리 실패를 구분해 남기는 이유는, 오류가 났다는 사실만으로 프로세스와 프로필이 모두 사라졌다고 볼 수 없기 때문입니다.

## 연결 해제: 먼저 이전 참조를 차단합니다

`DisconnectCoreAsync()`는 정상 attached 상태에서 `admission.Close()`를 호출한 뒤 `Disconnecting`으로 이동합니다. 이때 epoch가 바뀌므로 이전 참조는 드라이버 종료나 재연결 완료를 기다리지 않고 무효화됩니다.

그다음 진행 중인 명령이 끝나기를 제한 시간 동안 기다립니다. drain이 시간 안에 완료되지 않으면 진단 정보를 남기고 attachment 종료를 계속합니다. 스크립트 준비를 시도한 후 ChromeDriver와 Selenium 클라이언트를 정리하고 Chrome/CDP 건강성을 확인합니다.

정상 완료 상태는 `CdpOnly`입니다. 이미 `CdpOnly`인 상태의 disconnect는 건강성 확인 후 반환합니다. 모든 상태에서 무조건 성공하는 종료 대용 API는 아닙니다.

**정리 시작 후에는 호출자의 취소와 독립된 제한 시간으로 정리를 계속합니다.** 취소 요청을 받았다고 드라이버와 프로필을 방치하지 않기 위한 처리입니다.

## 재연결: 새 attachment를 만듭니다

`ReconnectWebDriverAsync()`는 `CdpOnly` 상태를 요구합니다. `AttachCoreAsync(true, ...)`가 Chrome/CDP를 확인하고 새 ChromeDriver, 새 executor, 새 `RemoteWebDriver` 세션을 생성합니다.

이후 CDP target과 WebDriver window를 대응시키고, 성공한 연결의 명령 진입을 열며 generation을 증가시킵니다. 마지막으로 새 lease를 반환합니다.

재연결 실패 시에도 Chrome/CDP가 정상이고 실패한 attachment 정리가 완료되었다면 `CdpOnly`로 남을 수 있습니다. 브라우저나 정리 상태가 나쁘면 `Faulted`가 됩니다. 실패 뒤 무조건 같은 재연결을 반복하기보다 현재 상태를 먼저 읽어야 합니다.

## epoch와 generation을 구분합니다

아래는 다른 실패나 추가 정리 호출이 없는 단순한 성공 경로의 값 변화 예입니다. 숫자를 사용자 코드의 고정 상수로 사용하지 마세요.

| 시점 | SessionGeneration | AttachmentEpoch |
|---|---:|---:|
| 초기 연결 성공 | 1 | 1 |
| 연결 해제 시작 | 1 | 2 |
| 재연결 성공 | 2 | 3 |

`Admission.Open()`과 `Close()`는 모두 epoch를 증가시킵니다. 반면 generation은 성공한 attachment를 반영합니다. 그래서 “재연결이 아직 성공하지 않았으니 이전 참조는 유효하다”는 결론이 성립하지 않습니다.

## 전체 종료와 lease 정리는 다릅니다

`WebDriverLease.DisposeAsync()`는 그 lease를 폐기합니다. Chrome을 닫거나 다른 lease 전체를 폐기하는 소유권 종료 작업이 아닙니다.

`Browser.DisposeAsync()`와 `StopAsync()`는 소유한 브라우저까지 정리하는 경로입니다. 호출자가 지정한 전용 프로필은 보존하고, 임시 프로필은 소유 Chrome 종료가 확인된 후 삭제하는 정책을 따릅니다.

정상 사용에서는 `await using var browser = ...`로 종료 경로를 확보합니다. 실패한 실습을 정리하려고 프로세스 이름 기준의 일괄 강제 종료를 추가하지 않습니다.

## 관찰 예제

다음은 `using BrowserDock;`가 있고 `browser`가 정상 시작된 현대 C# 코드 내부에 넣는 부분 예제입니다.

```csharp
Console.WriteLine($"Before: {browser.State}, G={browser.SessionGeneration}, E={browser.AttachmentEpoch}");
await browser.DisconnectWebDriverAsync();
Console.WriteLine($"Detached: {browser.State}, Chrome={browser.Health.ChromePid}");
await using var next = await browser.ReconnectWebDriverAsync();
Console.WriteLine($"After: {browser.State}, G={next.Generation}, E={next.AttachmentEpoch}");
```

예제의 목적은 정상 경로에서의 관계를 확인하는 것입니다. 출력값만으로 OS 전체 자원 누수가 없다고 판단할 수는 없습니다.

**근거 코드:** [Browser.cs](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Browser.cs), [Admission](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Infrastructure.cs), [상태 모델](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Models.cs)

[← 아키텍처](ko-Architecture.md) · [목차](ko-Home.md) · [다음: CDP →](ko-CDP.md)
