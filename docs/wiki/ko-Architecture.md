<!-- browserdock-ko-wiki:v1 -->
# 05. 전체 구조와 설계 원칙

> 핵심 질문: 브라우저와 연결의 수명을 누가 조정하고, 실패했을 때 누가 책임지나요?

## 세 가지 수명을 분리합니다

BrowserDock은 Chrome 프로세스, 독립 CDP 연결, WebDriver attachment를 서로 다른 수명 단위로 다룹니다. `Browser`는 이들을 조합하는 공개 진입점입니다.

CDP는 브라우저와 페이지를 관찰하고 선택하는 경로입니다. WebDriver attachment는 core W3C 명령을 실행하는 교체 가능한 경로입니다. ChromeDriver를 종료할 때 독립 CDP 연결까지 같은 객체의 정리 과정에 묶지 않는 것이 핵심입니다.

전체 호출 관계를 간략히 읽으면 다음과 같습니다. 이는 파일 구조가 아니라 역할을 설명하는 지도입니다.

```text
사용자 코드
    Browser
        프로세스·프로필 관리 ── Chrome
        CDP 제어 ──────────── Chrome
        WebDriver 연결 ── ChromeDriver ── Chrome
```

## 실제 코드에 대응시키기

| 책임 | 현재 구현의 읽기 위치 |
|---|---|
| 상태 전이, 시작·종료, 연결 교체 | `Browser.cs` |
| 프로세스 시작과 소유권, 프로필 | `Hosting.cs` |
| WebSocket 요청·응답, target과 page session | `Cdp.cs` |
| ChromeDriver readiness, 세션 생성, 명령 executor | `WebDriver.cs` |
| 공개 lease와 요소 참조 | `Elements.cs` |
| admission, deadline, 진단 큐 | `Infrastructure.cs` |
| 드라이버 검증용 패치 캐시 | `Patching.cs` |

`docs/architecture.md`는 구현 전 아키텍처 기준선입니다. 그 문서의 `Lifecycle Coordinator`, `TargetRegistry` 같은 논리 역할이 모두 독립 클래스로 구현되어 있다고 가정하지 않습니다. 예를 들어 현재는 여러 lifecycle 역할이 `Browser.cs`에 모여 있습니다.

## 자원 소유권이 중요한 이유

자원을 사용한다는 사실만으로 삭제할 권한이 생기지는 않습니다. 특히 프로필과 프로세스는 다른 프로그램이나 사용자가 소유할 수 있습니다.

| 자원 | 정상 종료 시 책임 |
|---|---|
| 라이브러리가 시작한 Chrome | `Browser`가 종료·정리 |
| attachment의 ChromeDriver | 연결 해제 또는 전체 종료 때 정리 |
| 임시 프로필 | 소유 Chrome 종료를 확인한 뒤 삭제 |
| 호출자가 지정한 전용 프로필 | 삭제하지 않고 잠금 해제 |
| CDP 연결과 작업 | 전체 종료 시 정리 |
| 검증된 패치 캐시 | 실행마다 지우는 임시 프로필과 다르게 취급 |

따라서 “chrome.exe라는 이름의 모든 프로세스를 종료한다”는 식의 정리는 이 구조와 맞지 않습니다. 실패 복구도 소유한 자원에 한정해야 합니다.

## 공개 객체가 안전장치가 되는 방식

사용자에게 raw `IWebDriver`를 반환하면 오래된 세션으로 직접 명령을 보내거나 라이브러리를 거치지 않고 종료할 수 있습니다. BrowserDock은 `WebDriverLease.Commands`와 `ElementRef`를 경계로 사용해 명령 전에 유효성을 확인합니다.

이 선택은 Selenium의 모든 편의 기능을 그대로 노출하지 않는다는 비용이 있습니다. 대신 연결 교체 이후 잘못된 참조가 사용되는 것을 라이브러리 경계에서 다룰 수 있습니다. 세부 계약은 [08장](ko-WebDriver-References.md)에 있습니다.

## 동시성의 조정 책임

`Browser`는 lifecycle gate로 상태를 바꾸는 작업을 직렬화합니다. 명령 경로에는 별도의 command gate가 있고, `Admission`은 새 명령을 받아도 되는지와 진행 중인 명령 수를 추적합니다.

이 셋은 같은 잠금의 다른 이름이 아닙니다. lifecycle gate는 조정 순서, command gate는 명령 실행 문맥, admission은 연결 교체 시 진입 차단과 drain을 다룹니다. 잠금 사용을 변경할 때는 정상 호출뿐 아니라 disconnect와 실행 중 명령의 경합을 함께 검토해야 합니다.

## 설계상의 한계도 함께 읽기

고급 `ExecuteCdpAsync()`는 일반 facade보다 넓은 영향을 줄 수 있습니다. 직접 탭을 닫거나 스크립트로 페이지 상태를 바꾸면 라이브러리의 고수준 사용 규칙을 넘어서는 결과가 생길 수 있습니다.

또한 코드상의 직렬화와 실제 Chrome 환경에서의 검증은 별개의 근거입니다. 아키텍처 설명만으로 누수 없음이나 모든 race 해결을 보장하지 않습니다.

**근거:** [설계 기준선](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/docs/architecture.md), [Browser.cs](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Browser.cs), [Infrastructure.cs](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Infrastructure.cs)

[← 코드 지도](ko-Code-Map.md) · [목차](ko-Home.md) · [다음: 생명주기 →](ko-Browser-Lifecycle.md)
