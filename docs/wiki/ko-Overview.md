<!-- browserdock-ko-wiki:v1 -->
# 01. BrowserDock은 어떤 문제를 해결하나요?

> 핵심 질문: 브라우저를 계속 열어 둔 채 자동화 연결만 바꾸려면 무엇을 분리해야 할까요?

## 브라우저와 자동화 연결은 같은 것이 아닙니다

자동화 코드를 작성하면 브라우저, 드라이버 프로세스, 세션을 한 덩어리처럼 다루기 쉽습니다. 그러나 브라우저가 살아 있는 것과 WebDriver 명령을 보낼 수 있는 것은 서로 다른 조건입니다.

BrowserDock은 Chrome을 먼저 소유하고 실행합니다. 그 Chrome에 독립적인 CDP 연결을 만들고, 필요할 때 교체할 수 있는 WebDriver 연결을 붙입니다. 따라서 WebDriver 연결을 해제하는 작업을 Chrome 종료와 분리할 수 있습니다.

이 차이를 한 문장으로 정리하면 **“브라우저를 다시 만드는 대신, 제어에 사용하는 연결을 새로 만든다”**입니다.

## 무엇이 유지되고 무엇이 교체되나요?

다음 표는 정상적인 WebDriver 연결 해제와 재연결을 설명합니다. Chrome 충돌이나 전체 종료까지 같은 결과를 보장한다는 뜻은 아닙니다.

| 대상 | 연결 해제 후 | 재연결 후 |
|---|---|---|
| 소유한 Chrome 프로세스 | 계속 실행 | 같은 브라우저 사용 |
| 프로필 디렉터리 | 유지 | 같은 프로필 사용 |
| 독립 CDP 연결 | 유지 | 계속 사용하며 필요 시 복구 |
| ChromeDriver 프로세스 | 종료 | 새 프로세스 생성 |
| WebDriver 세션 | 사용 중단 | 새 세션 생성 |
| 기존 lease와 요소 참조 | 무효화 | 자동으로 되살아나지 않음 |

연결 해제만으로 페이지를 다시 로드하지는 않습니다. 다만 페이지 자체의 JavaScript, 사용자의 조작, 탭 종료, 이후 navigation은 브라우저 상태를 바꿀 수 있습니다. **브라우저 유지가 모든 애플리케이션 상태의 고정을 뜻하지는 않습니다.**

## 어떤 흐름으로 사용하나요?

`Browser.StartAsync()`로 시작하고 `NavigateAsync()`로 이동합니다. WebDriver 명령은 `GetWebDriverAsync()`가 반환한 lease의 `Commands`를 통해 실행합니다.

WebDriver가 필요 없는 구간에서는 `DisconnectWebDriverAsync()`로 연결을 해제합니다. 그 상태에서 CDP를 사용하다가 `ReconnectWebDriverAsync()`로 새 lease를 얻습니다. 전체 작업이 끝나면 `Browser.DisposeAsync()` 또는 `StopAsync()`로 소유한 Chrome까지 정리합니다.

이 흐름의 실행 예제는 [03장](ko-Getting-Started.md), 내부 구현은 [06장](ko-Browser-Lifecycle.md)에 있습니다.

## 프로젝트의 현재 경계

문서 기준 구현의 브라우저 실행 대상은 **Windows 11 x64의 화면이 있는 Chrome**입니다. .NET Framework 4.8.1, .NET 8, .NET 10 대상 라이브러리를 제공하며, 저장소 전체 빌드에는 .NET 10 SDK가 필요합니다.

Chrome과 호환 ChromeDriver 실행 파일은 호출자가 준비합니다. 현재 범위에는 바이너리 자동 다운로드, 이미 실행된 외부 Chrome에 대한 소유권 인수, headless, GUI 입력이 포함되지 않습니다.

또한 특정 사이트의 탐지 회피, WAF 또는 CAPTCHA 통과를 보장하지 않습니다. 학습과 검증의 기준은 제어 계약과 자원 안전성이지 외부 사이트의 판정 결과가 아닙니다.

## 읽고 나서 확인할 질문

“연결을 끊었는데 Chrome이 남아 있다”는 현상은 오류일까요? WebDriver 연결 해제라면 의도한 결과입니다. 전체 종료가 끝났는데 소유 Chrome이 남아 있다면 다른 문제이므로 정리 결과를 확인해야 합니다.

**근거:** [README와 사용 규칙](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/README.ko.md), [생명주기 구현](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Browser.cs)

[← 학습 가이드](ko-Home.md) · [다음: 배경지식 →](ko-Core-Concepts.md)
