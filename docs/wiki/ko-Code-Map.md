<!-- browserdock-ko-wiki:v1 -->
# 04. 저장소 구조와 코드 읽기 지도

> 목표: 기능 이름에서 실제 구현과 검증 코드를 찾아갑니다.

## 저장소의 큰 구역

`src/BrowserDock`은 공통 엔진과 현대 C# API입니다. `src/BrowserDock.Legacy`는 같은 엔진을 사용하는 C# 7.3 친화적 API 경계입니다. Legacy를 별도 브라우저 엔진으로 이해하지 않습니다.

`tests`에는 공통 계약, 실제 브라우저, Framework 호환성, 테스트 도구 관련 코드가 있습니다. `samples`는 소비 예제이고, `scripts`는 검증과 배포 보조 명령입니다. `docs`에는 상세 명세와 아키텍처 기준선, 구현 및 검증 기록이 있습니다.

## 질문에서 파일로 찾아가기

| 알고 싶은 것 | 읽기 시작점 |
|---|---|
| 어떤 설정과 상태가 공개되나요? | `Models.cs` |
| 시작·분리·재연결·종료는 어디서 조정하나요? | `Browser.cs` |
| 프로필과 프로세스를 어떻게 소유하나요? | `Hosting.cs` |
| CDP 메시지를 어떻게 요청과 연결하나요? | `Cdp.cs`의 `CdpConnection` |
| 탭과 page session은 어떻게 관리하나요? | `Cdp.cs`의 `CdpController` |
| ChromeDriver와 Selenium 세션은 어떻게 만드나요? | `WebDriver.cs`의 `Attachment` |
| lease와 요소는 언제 무효인가요? | `Elements.cs` |
| 명령 진입과 제한 시간은 어떻게 관리하나요? | `Infrastructure.cs`의 `Admission`, `Deadline` |
| 로그 큐와 overflow는 어떻게 처리하나요? | `Infrastructure.cs`의 `Diagnostics` |
| 드라이버 패치와 캐시는 어디에 있나요? | `Patching.cs` |
| 런타임·컴파일러 차이를 어떻게 보완하나요? | `Compatibility.cs`, `CompilerCompatibility.cs` |
| 오류 분류는 무엇인가요? | `Errors.cs` |

현재 소스는 여러 논리 역할을 하나의 파일에 담습니다. 설계 문서에 나온 컴포넌트 이름과 동일한 파일을 찾지 못했다고 해서 구현이 없다고 단정하지 마세요.

## 첫 탐색: 제목 한 번 읽기

`Lifecycle` 샘플의 `original.Commands.GetTitleAsync()`에서 시작합니다. `Elements.cs`에서 `GuardedCommands.GetTitleAsync()`가 공통 `Run()`으로 전달하는 것을 확인합니다.

이어서 `Browser.CommandAsync()`를 읽습니다. lease의 유효성과 epoch, 명령 gate, admission 검사가 어디에 있는지 표시합니다. 마지막으로 `Attachment.InvokeAsync()`에서 Selenium 호출을 실행하고 취소를 처리하는 부분을 확인합니다.

이렇게 읽으면 “제목을 얻는다”는 API 뒤에 **현재 연결인지 확인 → 명령을 직렬화 → 실제 호출 → 실패 처리**가 있다는 것을 볼 수 있습니다.

## 두 번째 탐색: 재연결

`ReconnectWebDriverAsync()`에서 `CdpOnly` 상태 검사를 찾습니다. `AttachCoreAsync()`로 이동해 Chrome/CDP 상태 확인, attachment 생성, target 연결, epoch 개방, generation 증가를 따라갑니다.

`Attachment.CreateAsync()`에서는 새 ChromeDriver 프로세스와 새 `RemoteWebDriver`를 만드는지 확인합니다. 이전 객체를 그대로 재사용하는 복구 경로가 아니라는 점이 중요합니다.

## 구현에서 테스트로 이동하기

프로토콜 수준 동작은 `ContractTests.cs`에서 시작하기 좋습니다. 예를 들어 `CdpMultiplexesOutOfOrderResponsesAndIncludesPageSession`은 응답이 역순으로 도착해도 요청에 맞게 대응하는지 검사합니다.

`PublicSeleniumConstructorAndDisposeNeverSendDeleteSession`은 폐기 과정에서 원격 DELETE 요청이 나가지 않는지 검사합니다. 두 테스트 모두 “실제 Chrome 전체 동작을 검증했다”는 뜻은 아니므로 테스트의 경계도 함께 읽습니다.

## 작은 연습

IDE에서 한 메서드를 선택한 뒤 “호출자, 생성하는 자원, 변경하는 상태, 실패 시 정리, 대응 테스트”를 다섯 칸으로 적어 보세요. 파일 전체를 순서대로 읽는 것보다 의도를 파악하기 쉽습니다.

**근거:** [핵심 소스 디렉터리](https://github.com/mete0rfish/BrowserDock/tree/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock), [계약 테스트](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/tests/BrowserDock.Tests/ContractTests.cs)

[← 첫 실행](ko-Getting-Started.md) · [목차](ko-Home.md) · [다음: 아키텍처 →](ko-Architecture.md)
