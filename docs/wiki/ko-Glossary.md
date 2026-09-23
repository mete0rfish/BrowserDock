<!-- browserdock-ko-wiki:v1 -->
# 부록 A. 용어집과 API 색인

이 페이지는 본문을 읽다가 용어가 헷갈릴 때 찾아보는 참조입니다. 메서드의 전체 계약은 실제 선언과 해당 장을 함께 확인하세요.

## 핵심 용어

| 용어 | 이 프로젝트에서의 의미 |
|---|---|
| Chrome host | 라이브러리가 시작하고 소유하는 실제 Chrome 프로세스와 관리 경계 |
| ChromeDriver | WebDriver 명령을 Chrome에 적용하는 별도 프로세스 |
| attachment | ChromeDriver, executor, WebDriver client와 세션을 묶는 교체 가능한 연결 |
| lease | 현재 attachment의 명령 API를 사용하는 참조. 브라우저 소유권은 아님 |
| epoch | 이전 attachment 참조의 사용을 거부하는 유효성 경계 값 |
| generation | 성공한 WebDriver 연결의 세대. 초기 연결도 포함 |
| admission | 명령 진입 허용 여부와 실행 중 명령 수를 관리하는 장치 |
| drain | 이미 진입한 명령의 완료를 기다리는 과정 |
| deadline | 작업에 허용된 시간이 끝나면 취소를 요청하는 제한 |
| CDP | Chrome DevTools Protocol. 독립 WebSocket 제어 경로 |
| target | CDP의 제어 대상. 본문에서는 주로 페이지를 의미 |
| TargetKey | 호출자에게 제공하는 불투명한 target 식별값 |
| page session | 특정 target에 명령을 보내기 위한 CDP session |
| window handle | WebDriver의 창 식별값. TargetKey와 다른 체계 |
| locator | 현재 DOM에서 요소를 찾는 탐색 조건 |
| frame path | 바깥쪽부터 안쪽으로 지정한 iframe locator 경로 |
| stale | 객체의 저장된 문맥이 더 이상 현재 상태에 유효하지 않음 |
| fixture | 재현 가능한 입력과 기대값을 제공하는 테스트 환경 |
| recipe | 특정 버전 범위와 패턴 조건을 가진 드라이버 패치 규칙 |

`session`이라는 단어만으로는 CDP session인지 WebDriver session인지 분명하지 않습니다. 오류 보고와 문서에서는 접두사를 함께 쓰는 편이 좋습니다.

## 생명주기 API

| API | 역할 | 관련 장 |
|---|---|---|
| `Browser.StartAsync()` | Chrome·CDP·초기 WebDriver 연결 생성 | [06](ko-Browser-Lifecycle.md) |
| `GetWebDriverAsync()` | 현재 attachment를 사용하는 lease 얻기 | [08](ko-WebDriver-References.md) |
| `DisconnectWebDriverAsync()` | WebDriver를 해제하고 Chrome·CDP 유지 | [06](ko-Browser-Lifecycle.md) |
| `EnterCdpOnlyAsync()` | 연결 해제 경로의 별도 진입 이름 | [06](ko-Browser-Lifecycle.md) |
| `ReconnectWebDriverAsync()` | CdpOnly에서 새 연결과 lease 생성 | [06](ko-Browser-Lifecycle.md) |
| `StopAsync()` / `Browser.DisposeAsync()` | 소유한 브라우저까지 정리 | [10](ko-Resource-Safety.md) |
| `WebDriverLease.DisposeAsync()` | 해당 lease만 폐기 | [08](ko-WebDriver-References.md) |

## 페이지·요소·진단 API

| API 또는 모델 | 역할 |
|---|---|
| `NavigateAsync()` / `NavigationOptions` | 이동 방식과 완료 조건 지정 |
| `GetTargetsAsync()` / `SelectTargetAsync()` | page target 조회와 명시 선택 |
| `ExecuteCdpAsync()` | 고급 CDP 명령 실행 |
| `IBrowserCommands` | guarded WebDriver 명령 인터페이스 |
| `FindAsync()` / `FindOptions` | locator와 target/frame 문맥으로 요소 탐색 |
| `ElementRef.ReacquireAsync()` | 저장된 탐색 문맥을 사용해 새 참조 반환 |
| `Browser.Health` | 상태·PID·연결·정리 실패 snapshot |
| `BrowserDockException.Category` | 오류의 의미상 분류 |
| `BrowserMayHaveAdvanced` | 실패나 취소 이전에 브라우저가 진행했을 가능성 |

현대 API와 Legacy API는 같은 동작 개념을 공유하지만 반환 타입과 설정 표현이 다를 수 있습니다. 코드를 복사할 때 namespace와 대상 언어 버전을 함께 확인하세요.

**근거:** [공개 모델](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Models.cs), [Browser API](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Browser.cs), [참조 API](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Elements.cs)

[학습 가이드](ko-Home.md) · [설계 선택 해설 →](ko-Design-Decisions.md)
