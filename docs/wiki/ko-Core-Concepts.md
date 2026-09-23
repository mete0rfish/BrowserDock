<!-- browserdock-ko-wiki:v1 -->
# 02. 코드를 읽기 전에 알아둘 개념

> 핵심 질문: Chrome, ChromeDriver, Selenium, CDP는 각각 어떤 역할을 하나요?

## 구성요소 네 가지

**Chrome**은 웹 페이지를 실행하는 실제 브라우저입니다. 화면, DOM, 쿠키, 브라우저 저장소 등의 상태를 가집니다.

**ChromeDriver**는 WebDriver 명령을 Chrome에 적용하는 별도 실행 파일입니다. BrowserDock에서는 이 프로세스를 Chrome과 따로 시작하고 종료합니다.

**Selenium .NET**은 WebDriver 명령을 사용하는 클라이언트 라이브러리입니다. BrowserDock 내부에서는 공개 `RemoteWebDriver` API를 사용하지만, 사용자에게 그 객체를 직접 반환하지 않습니다.

**CDP**는 Chrome DevTools Protocol입니다. BrowserDock은 WebSocket을 통해 Chrome에 직접 연결해 버전 확인, target 관리, 페이지 이동, 스크립트 실행 등을 수행합니다.

사용자에게 보이는 진입점은 `Browser`입니다. 내부적으로 WebDriver 경로는 ChromeDriver를 거치고, CDP 경로는 별도 연결로 Chrome과 통신합니다. 두 경로가 있다고 해서 호출자가 같은 페이지에 무제한으로 명령을 동시에 보내도 안전하다는 뜻은 아닙니다.

## 헷갈리기 쉬운 수명 단위

| 용어 | 의미 | 혼동하지 말아야 할 것 |
|---|---|---|
| 프로세스 | 운영체제가 실행하는 프로그램 단위 | WebDriver 세션과 다릅니다. |
| 프로필 | Chrome이 사용하는 사용자 데이터 디렉터리 | 현재 열린 DOM 그 자체가 아닙니다. |
| WebDriver 세션 | WebDriver 명령의 실행 문맥 | Chrome을 재시작하지 않아도 교체할 수 있습니다. |
| CDP page session | 특정 target에 명령을 보내는 CDP 문맥 | WebDriver 세션 ID와 다릅니다. |
| target | CDP가 제어하는 대상 | 여기서는 주로 page target을 다룹니다. |
| window handle | WebDriver가 창을 식별하는 값 | CDP target ID와 같다고 가정하지 않습니다. |

예를 들어 같은 Chrome이 살아 있어도 ChromeDriver가 종료되면 기존 WebDriver 세션으로 명령을 보낼 수 없습니다. 반대로 WebDriver가 없어도 독립 CDP 연결이 정상이라면 CDP 명령은 가능할 수 있습니다.

## lease, epoch, generation

`WebDriverLease`는 현재 연결의 명령 인터페이스를 사용하는 참조입니다. 브라우저의 소유권 자체를 넘겨받는 객체가 아닙니다.

`AttachmentEpoch`는 **이 명령이 여전히 현재 연결에 속하는가**를 판정하는 유효성 값입니다. `Admission.Open()`과 `Close()`에서 증가하므로 단순한 재연결 횟수로 해석하지 않습니다.

`SessionGeneration`은 성공적으로 연결한 세션의 세대를 나타냅니다. 초기 연결 성공도 세대 증가에 포함됩니다. 연결 성공 전에 이전 참조를 무효화할 필요가 있기 때문에 epoch와 generation이 따로 있습니다.

자세한 변화 시점은 [06장](ko-Browser-Lifecycle.md), 참조 사용 예제는 [08장](ko-WebDriver-References.md)에서 확인합니다.

## C# 코드를 읽을 때 필요한 것

`await`는 비동기 작업의 완료를 기다리며 그 결과나 예외를 받습니다. `Task`와 `ValueTask`가 보이면 우선 호출자가 언제 기다리고 실패를 어디서 처리하는지 확인하세요. 이 프로젝트의 `ValueTask` 결과는 예제처럼 한 번 받아 한 번 `await`하는 방식으로 사용합니다.

`CancellationToken`은 취소 요청을 전달합니다. 이미 브라우저에 전달된 클릭이나 navigation을 과거 상태로 되돌리는 장치는 아닙니다.

`await using`은 범위를 벗어날 때 비동기 정리를 수행합니다. 단, 어떤 자원이 정리되는지는 대상 객체에 따라 다릅니다. lease의 정리와 `Browser`의 정리를 같은 작업으로 생각하면 안 됩니다.

`SemaphoreSlim`은 동시에 진입할 수 있는 작업 수를 제한합니다. `Browser.cs`에서는 수명주기 조정과 명령 실행에 서로 다른 gate가 등장합니다. 각 gate를 어느 경로에서 획득하고 `finally`에서 해제하는지 읽으면 동시성 구조를 이해하기 쉽습니다.

## 학습 확인

같은 페이지에서 WebDriver만 재연결했다고 가정해 보세요. Chrome PID가 유지되어도 기존 lease는 무효일 수 있습니다. “운영체제 프로세스의 동일성”과 “라이브러리 참조의 유효성”이 다른 기준이기 때문입니다.

**근거 코드:** [Browser.cs](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Browser.cs), [Infrastructure.cs](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Infrastructure.cs), [Elements.cs](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Elements.cs)

[← 프로젝트 개요](ko-Overview.md) · [목차](ko-Home.md) · [다음: 첫 실행 →](ko-Getting-Started.md)
