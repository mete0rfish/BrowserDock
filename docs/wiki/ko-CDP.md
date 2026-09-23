<!-- browserdock-ko-wiki:v1 -->
# 07. CDP로 브라우저와 페이지 제어하기

> 핵심 질문: 명령을 전송한 뒤 어떤 응답과 이벤트를 기다려야 하나요?

## 연결 발견과 초기화

Chrome은 별도 프로필과 원격 디버깅 설정으로 시작됩니다. BrowserDock은 발견한 endpoint를 확인한 뒤 loopback WebSocket으로 연결합니다. 임의의 외부 주소를 신뢰해 연결하는 구조가 아닙니다.

`CdpController.InitializeAsync()`는 브라우저 버전과 필요한 CDP 메서드를 확인하고 target 발견을 활성화합니다. 단일 page target은 초기 대상으로 선택할 수 있지만, 여러 페이지가 있을 때 배열 순서만 보고 대상을 추측하지 않습니다.

## 요청 번호로 응답을 대응시킵니다

`CdpConnection.SendAsync()`는 숫자 ID를 증가시켜 요청을 만들고, 그 ID에 대응하는 완료 객체를 `pending`에 보관합니다. 응답이 도착하면 ID로 원래 요청을 찾습니다.

응답은 보낸 순서대로 돌아온다고 가정하지 않습니다. 두 요청 중 두 번째 응답이 먼저 와도 올바른 호출을 완료해야 합니다. 이 동작은 `CdpMultiplexesOutOfOrderResponsesAndIncludesPageSession` 테스트에서 확인할 수 있습니다.

요청에는 응답 ID가 있지만 이벤트에는 메서드 이름과 매개변수가 있습니다. 수신 루프는 이벤트를 별도 큐에 전달하고 dispatcher가 처리합니다. 이벤트 큐가 넘치면 조용히 중요한 이벤트를 버리는 대신 연결 실패로 다룹니다. 상태 추적이 불완전한 연결을 정상으로 보지 않기 위한 처리입니다.

## browser 명령과 page 명령

| 명령 예 | 전송 문맥 |
|---|---|
| `Browser.getVersion` | browser 연결 |
| `Target.getTargets` | browser 연결 |
| `Page.navigate` | 선택한 page session |
| `Runtime.evaluate` | 선택한 page session |

`ExecuteCdpAsync()`는 `Browser.*`와 `Target.*`를 browser 연결로 보냅니다. 그 밖의 메서드는 선택된 page session으로 보냅니다. browser/target 명령에 page target 매개변수를 함께 지정하면 설정 오류로 처리합니다.

다음은 시작된 `browser`와 제어 가능한 페이지가 있는 현대 C# 문맥에서 사용하는 부분 예제입니다. 반환값은 `Runtime.evaluate` 응답의 구조를 따라 읽습니다.

```csharp
var result = await browser.ExecuteCdpAsync(
    "Runtime.evaluate",
    new { expression = "document.title", returnByValue = true });

if (result.TryGetProperty("exceptionDetails", out _))
    throw new InvalidOperationException("페이지 스크립트 실행이 실패했습니다.");

Console.WriteLine(result.GetProperty("result").GetProperty("value").GetString());
```

다른 CDP 명령의 응답에도 항상 `result.value`가 있다고 가정하지 마세요. 이 예제는 문자열을 반환하는 표현식에 한정합니다.

## 탭은 명시적으로 선택합니다

`GetTargetsAsync()`로 후보를 확인하고 원하는 `TargetKey`를 `SelectTargetAsync()`에 전달합니다. key는 제어 대상의 식별값이지 탭의 순번이 아닙니다.

CDP target ID와 WebDriver window handle의 문자열이 우연히 비슷해 보여도 그 형식에 의존하면 안 됩니다. WebDriver가 연결되어 있다면 BrowserDock이 임시 marker를 양쪽 경로에서 확인해 대응시킵니다. 상세 과정은 [08장](ko-WebDriver-References.md)에 있습니다.

## 페이지 이동은 완료 조건까지 포함합니다

`Standard` 모드에서는 WebDriver가 navigation을 시작하고 CDP가 완료 관찰에 참여합니다. `Detached`와 `CdpOnly`는 CDP로 이동합니다. 따라서 Standard를 “CDP를 전혀 사용하지 않는 모드”라고 설명하면 정확하지 않습니다.

| 완료 조건 | 사용 시 생각할 점 |
|---|---|
| `Commit` | 새 문서 이동이 커밋되는 시점을 기준으로 합니다. |
| `DOMContentLoaded` | DOM 구성 이벤트와 원하는 UI의 준비는 다를 수 있습니다. |
| `Load` | 문서 load 이벤트 이후에도 애플리케이션 작업은 계속될 수 있습니다. |
| `NetworkIdle` | 추적 요청이 500ms 비는 best-effort 조건입니다. |

어떤 조건도 모든 웹 애플리케이션의 “사용 준비 완료”를 자동으로 의미하지 않습니다. 장기 요청이 있는 페이지에서 NetworkIdle이 navigation timeout에 도달할 수 있습니다. 버튼 사용 가능 여부 등 애플리케이션 조건은 별도로 관찰해야 합니다.

## 연결 복구와 취소

CDP 연결이 끊기면 대기 요청을 실패시키고 복구 경로에서 target과 session을 다시 구성합니다. 새 문서 스크립트도 필요한 session에 다시 등록합니다. Chrome이 종료된 경우까지 소켓 재접속만으로 원래 브라우저를 되살리는 기능은 아닙니다.

navigation 취소는 이미 전송한 이동을 취소 이전 상태로 되돌리지 않습니다. 예외의 `BrowserMayHaveAdvanced`와 현재 상태를 확인하고, 결제나 제출 같은 부작용이 있는 작업을 무조건 반복하지 마세요.

**근거:** [Cdp.cs](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Cdp.cs), [navigation 조정](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Browser.cs), [프로토콜 계약 테스트](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/tests/BrowserDock.Tests/ContractTests.cs)

[← 생명주기](ko-Browser-Lifecycle.md) · [목차](ko-Home.md) · [다음: WebDriver와 참조 →](ko-WebDriver-References.md)
