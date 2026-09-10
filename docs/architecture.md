# UcDotNet 아키텍처

- 문서 상태: 구현 전 아키텍처 기준선
- 기준 명세: [spec.md](spec.md)
- 초기 배포 대상: Windows 11 x64, headed Chrome, 지원 중인 .NET LTS
- 범위: 구조와 책임의 기준선이다. 구현 및 검증 상태는 [implementation.md](implementation.md)에 기록한다.

## 1. 문서 목적

이 문서는 UcDotNet을 어떤 컴포넌트로 나누고, 각 컴포넌트가 어떤 상태와 자원을 소유하며, 시작·이동·연결 해제·재연결·종료가 어떻게 협력하는지 정의한다.

[spec.md](spec.md)는 외부에서 관찰할 수 있는 요구사항과 수용 기준의 기준 문서다. 이 문서는 그 요구사항을 구현 가능한 구조로 옮긴다. 두 문서가 충돌하면 임의로 구현하지 않고 두 문서를 같은 변경에서 함께 수정한다.

## 2. 아키텍처 요약

UcDotNet은 Chrome, CDP, WebDriver의 수명을 분리한 하이브리드 구조다.

- **Chrome**은 실제 브라우저 상태와 프로필을 가진 장수명 프로세스다.
- **CDP control plane**은 Chrome 및 page target을 관찰·선택·이동·복구한다.
- **WebDriver attachment**는 core W3C 명령을 제공하는 교체 가능한 단기 연결이다.
- **UcBrowser**는 이 세 계층의 소유권과 상태 전이를 직렬화하는 aggregate root다.

disconnect는 브라우저 종료가 아니다. ChromeDriver와 WebDriver session만 폐기하고 Chrome과 CDP를 유지한다. reconnect는 기존 Selenium 객체를 되살리지 않고 새 ChromeDriver 프로세스와 새 WebDriver session을 만든다.

```mermaid
flowchart LR
    App[사용자 애플리케이션] --> API[UcBrowser / guarded API]
    API --> LC[Lifecycle Coordinator]
    LC --> Host[Chrome Process Host]
    LC --> CP[CDP Control Plane]
    LC --> WA[WebDriver Attachment]
    CP <--> Chrome[Chrome]
    WA <--> Driver[ChromeDriver]
    Driver <--> Chrome
    Host --> Chrome
    LC --> Patch[Driver Artifact / Patch Cache]
    LC --> Diag[Diagnostics]
```

## 3. 설계 원칙과 불변조건

다음 규칙은 구현 편의를 위해 완화할 수 없는 시스템 불변조건이다.

1. Chrome, ChromeDriver, WebDriver session은 서로 다른 수명 단위다.
2. ChromeDriver disconnect는 소유한 ChromeDriver PID만 종료한다.
3. reconnect는 항상 새 process, client, executor, W3C SessionId를 만든다.
4. disconnect를 시작할 때 `AttachmentEpoch`를 먼저 변경한다.
5. 모든 WebDriver 명령은 `WebDriverAttached` 상태와 현재 epoch를 로컬에서 확인한다.
6. raw `IWebDriver`와 `IWebElement`는 공개 경계를 통과하지 않는다.
7. page/runtime CDP 명령은 명시적인 target session에 전송한다.
8. target이 모호하면 최근 탭이나 배열 순서로 추측하지 않고 실패한다.
9. ChromeDriver 바이너리 원본은 수정하지 않는다. 패치는 검증된 cache 사본에만 적용한다.
10. 프로세스 이름이나 미확인 PID로 종료하지 않는다.
11. 사용자 callback은 transport read loop나 lifecycle lock 안에서 실행하지 않는다.
12. cancellation 이후에도 소유 자원 정리는 제한 시간 안에서 계속한다.
13. Selenium private API 또는 reflection에 의존하지 않는다.
14. 특정 사이트의 탐지 회피 결과는 아키텍처 계약이 아니다.

## 4. 시스템 경계

### 4.1 시스템이 소유하는 것

MVP에서 UcDotNet은 다음 자원을 생성하고 추적한다.

| 자원 | 소유권 | 식별 정보 | 최종 정리 |
|---|---|---|---|
| Chrome process tree | library | PID, create time, canonical executable path | 정상 종료 후 필요 시 해당 tree만 강제 종료 |
| ChromeDriver process | attachment | PID, create time, service port | disconnect마다 종료 |
| temporary profile | library | canonical path, nonce marker | Chrome 종료 확인 후 삭제 |
| caller-specified profile | caller | canonical path, exclusive lock | 삭제하지 않고 lock만 해제 |
| CDP sockets와 read loop | library | endpoint, connection id | bounded drain 후 dispose |
| patch cache result | library persistent | source/result hash, recipe id | 실행 중 보존; 별도 prune 정책 |

호출자가 이미 실행한 외부 Chrome attach는 MVP 경계 밖이다. 기본 Chrome profile도 사용하지 않는다.

### 4.2 외부 의존성

- Chrome 또는 Chrome for Testing executable
- 호출자가 제공하는 호환 ChromeDriver executable
- Selenium.WebDriver의 공개 API
- Chrome DevTools Protocol의 최소 `Browser`, `Target`, `Page`, `Runtime` 기능
- .NET BCL과 `Microsoft.Extensions.Logging` 추상화
- Windows process, file locking 및 process-tree 관리 기능

Selenium typed DevTools API, Selenium Manager 내부 경로, Selenium private service API는 의존하지 않는다.

## 5. 논리 컴포넌트

### 5.1 Public API

`UcBrowser`가 한 브라우저 인스턴스의 유일한 lifecycle 진입점이다. 상태 조회, navigation, target 선택, WebDriver lease, CDP 고급 명령, 종료를 제공한다.

`WebDriverLease`는 raw Selenium 객체가 아니라 `IUcWebDriver` guarded facade를 제공한다. `ElementRef` 역시 raw element가 아니라 locator와 수명 정보를 가진 라이브러리 객체다.

공개 모델은 Selenium 형식을 최대한 노출하지 않는다. 이 경계 덕분에 Selenium package 업데이트와 attachment 교체가 애플리케이션 상태를 직접 오염시키지 않는다.

### 5.2 Lifecycle Coordinator

`UcBrowser` 내부의 조정자이며 다음을 책임진다.

- 상태 전이와 lifecycle operation 직렬화
- 전체 deadline을 단계별 timeout에 배분
- 시작 중 부분 생성된 자원의 rollback
- disconnect 시 command admission 차단과 in-flight drain
- Chrome/CDP 건강성에 따른 복구 수준 선택
- 최종 종료의 역순 정리

조정자는 구체적인 process, socket, Selenium 구현을 직접 다루지 않고 하위 port를 호출한다. 하위 컴포넌트는 독자적으로 전역 상태를 변경하지 않는다.

### 5.3 Hosting

`ProfileManager`는 프로필 경로 검증, ownership marker, caller-owned profile lock과 안전한 cleanup을 담당한다.

`ChromeProcessHost`는 별도 user-data directory와 `--remote-debugging-port=0`으로 Chrome을 시작하고 정확한 PID tree를 추적한다. `DevToolsEndpointDiscovery`는 `DevToolsActivePort`와 `/json/version`을 함께 검증한 뒤 endpoint를 확정한다.

`ProcessTreeTracker`는 PID뿐 아니라 create time과 executable path를 기록하여 PID 재사용이나 다른 세션 오종료를 방지한다.

### 5.4 CDP Control Plane

CDP는 두 수준으로 나뉜다.

- `CdpConnection`: browser WebSocket, JSON-RPC id, read/write loop, reconnect와 protocol 협상을 관리한다.
- `TargetRegistry`: target 생성·삭제·detach event를 처리하고 library의 `TargetKey`를 CDP `targetId`에 매핑한다.
- `CdpTargetSession`: page target에 attach한 `sessionId`를 소유한다.
- `PageNavigator`: target session을 지정해 navigation을 실행하고 완료 조건을 판정한다.
- `RuntimeScriptRegistry`: 등록할 script의 논리 id와 적용된 target/session을 추적한다.

browser 수준의 `Browser.*`, `Target.*` 명령은 browser connection으로 보낸다. `Page.*`, `Runtime.*` 명령은 반드시 `CdpTargetSession.sessionId`와 함께 보낸다. CDP transport가 복구되면 target을 다시 열거하고 controlled target에 재attach한 뒤 runtime script를 재등록한다.

`TargetKey`는 호출자가 사용할 수 있는 불투명 식별자다. 내부 CDP `targetId`가 사라지면 해당 key는 terminal stale 상태가 되며 다른 탭에 재할당하지 않는다.

### 5.5 WebDriver Attachment

`ChromeDriverProcessHost`는 후보 service port를 선택하고 ChromeDriver를 `--port=N`으로 시작한다. stdout/stderr를 비동기 소비하고, 동일 PID가 살아 있는 상태에서 loopback `/status`가 ready를 반환해야 다음 단계로 넘어간다.

`WebDriverAttachmentFactory`는 다음 입력으로 새 attachment를 만든다.

- 현재 Chrome debugger address
- `detach=true`/`LeaveBrowserRunning`
- 현재 session creation deadline
- 소유 ChromeDriver PID와 service endpoint

공개 `RemoteWebDriver` 경로를 사용하므로 지원 계약은 core W3C 기능이다. Selenium의 Chrome vendor convenience command는 MVP 계약에 포함하지 않는다.

`DetachAwareCommandExecutor`는 정상 명령을 service endpoint로 전달하지만 detaching 상태에서는 `Quit`을 로컬 성공으로 처리한다. ChromeDriver PID를 먼저 종료한 뒤 WebDriver dispose가 죽은 endpoint에 매달리거나 Chrome에 종료 명령을 전달하지 않도록 하기 위함이다.

`AttachmentEpochGuard`는 모든 facade 명령 앞에서 state와 epoch를 검사한다. 검사를 통과한 명령만 in-flight counter에 등록되고, 완료 시 반드시 해제된다.

### 5.6 Patching

`DriverArtifactResolver`는 Chrome과 ChromeDriver 버전 및 원본 hash를 읽는다. `DriverPatchCache`는 원본을 불변으로 두고 별도 임시 사본을 만든다.

`IDriverPatchStrategy`는 지원 version 범위, pattern별 정확한 예상 match 수, recipe id를 선언한다. 결과는 원본 hash, 결과 hash, recipe와 match count가 포함된 manifest와 함께 원자적으로 승격한다. 어떤 값이라도 예상과 다르면 패치하지 않은 원본으로 조용히 fallback하지 않고 시작을 실패시킨다.

패치는 기본 비활성화다. lifecycle이나 CDP 모듈은 patch 구현을 알지 못하며, 최종 driver executable 경로만 입력받는다.

### 5.7 Diagnostics

모든 operation에는 correlation id가 있다. 상태 전이, PID, endpoint 발견, target/session 변경, generation/epoch, timeout, cleanup 결과를 구조화 로그와 `BrowserHealthSnapshot`에 기록한다.

로그 기본값은 cookie, storage, page source, script result와 query/fragment를 포함한 전체 URL을 제외한다. 사용자 callback과 log sink 오류가 lifecycle을 중단시키지 않도록 비동기 경계와 제한된 buffer를 둔다.

## 6. 의존 방향

의존성은 외부 경계에서 구체 구현 쪽으로 한 방향만 흐른다.

```text
Public API
    ↓
Application / Lifecycle
    ↓
Ports: Hosting | CDP | WebDriver | Patching | Diagnostics
    ↓
Infrastructure implementations
    ↓
Windows / Chrome / ChromeDriver / Selenium / WebSocket / File system
```

의존 규칙:

- Public API는 Selenium concrete type과 Windows handle type을 참조하지 않는다.
- CDP와 WebDriver 모듈은 서로 직접 시작하거나 종료하지 않는다.
- Hosting은 navigation policy를 알지 못한다.
- Patching은 process를 시작하지 않는다.
- Diagnostics는 business state를 변경하지 않는다.
- Lifecycle만 여러 하위 모듈을 조합하고 상태를 commit한다.

초기 구현은 하나의 NuGet package와 assembly 안에서 namespace/internal 경계로 시작할 수 있다. native input과 legacy facade만 별도 package다. 내부 경계가 안정되기 전에 assembly를 과도하게 분할하지 않는다.

## 7. 런타임 상태 모델

### 7.1 Aggregate state

외부에 노출하는 `BrowserState`는 lifecycle의 큰 단계를 나타낸다.

- `Stopped`
- `StartingChrome`
- `ChromeReady`
- `AttachingWebDriver`
- `WebDriverAttached`
- `Disconnecting`
- `CdpOnly`
- `Reattaching`
- `Faulted`
- `Disposing`

Chrome process, CDP transport, WebDriver attachment와 profile은 별도 health 축으로 유지한다. 예를 들어 `CdpOnly`는 Chrome이 살아 있고 CDP가 건강하지만 WebDriver attachment는 없다는 의미다.

### 7.2 SessionGeneration과 AttachmentEpoch

두 번호의 의미를 섞지 않는다.

| 값 | 변경 시점 | 용도 |
|---|---|---|
| `SessionGeneration` | 새 W3C session 생성이 성공한 뒤 | 성공한 session 순서와 진단 |
| `AttachmentEpoch` | disconnect 시작 시 즉시 폐기하고 새 attachment 확정 시 commit | lease와 element의 사용 가능성 검사 |

generation만 사용하면 disconnect 후 reconnect가 아직 성공하지 않은 구간에서 이전 lease가 유효해 보이는 문제가 생긴다. 따라서 실제 command admission은 state와 epoch를 기준으로 한다.

### 7.3 상태 commit 규칙

하위 작업 성공만으로 외부 상태를 먼저 바꾸지 않는다. lifecycle coordinator가 필요한 불변조건을 모두 확인한 뒤 상태를 한 번에 commit한다.

예를 들어 `WebDriverAttached`가 되려면 다음이 모두 참이어야 한다.

- Chrome PID와 create time이 시작 때 기록한 값과 같다.
- CDP browser connection과 controlled target가 건강하다.
- ChromeDriver PID가 살아 있고 `/status` readiness를 통과했다.
- W3C session id가 생성됐다.
- guarded executor와 새 epoch가 설치됐다.

## 8. 핵심 실행 흐름

### 8.1 시작

1. 옵션, 지원 OS/runtime, binary version과 profile ownership을 검증한다.
2. 필요하면 검증된 patch cache 사본을 준비한다.
3. Chrome을 시작하고 `DevToolsActivePort`를 찾는다.
4. browser CDP WebSocket에 연결하고 protocol/browser version을 기록한다.
5. target discovery/auto-attach를 활성화하고 controlled page session을 확정한다.
6. ChromeDriver를 별도 프로세스로 시작하고 `/status` readiness를 확인한다.
7. debugger address와 `detach=true`로 새 W3C session을 만든다.
8. generation과 epoch를 commit하고 `WebDriverAttached`를 반환한다.

각 단계 실패 시 이미 소유한 자원만 역순 정리한다.

### 8.2 Standard navigation

1. lifecycle/command admission이 `WebDriverAttached`와 현재 epoch를 확인한다.
2. guarded W3C navigation을 실행한다.
3. 결과 URL과 오류 category를 facade 모델로 변환한다.
4. attachment와 generation은 유지한다.

### 8.3 Detached navigation과 reconnect

```mermaid
sequenceDiagram
    participant App
    participant L as Lifecycle
    participant G as Epoch Guard
    participant C as CDP Target Session
    participant D as ChromeDriver
    participant W as WebDriver Client

    App->>L: Navigate(Detached, reconnect=true)
    L->>G: close admission; rotate epoch
    L->>G: drain in-flight commands
    L->>C: snapshot controlled target and scripts
    L->>W: executor := detaching
    L->>D: terminate exact owned PID
    L->>W: bounded dispose; Quit is local no-op
    L->>C: Page.navigate + wait milestone
    L->>D: start new PID; verify /status
    L->>W: create new session
    L->>C: re-enumerate and reconcile target
    L->>G: install new attachment; commit epoch/generation
    L-->>App: NavigationResult
```

`reconnect=false`이면 CDP navigation 완료 후 `CdpOnly`로 남는다. 중간에 Chrome이나 CDP가 사라지면 새 Chrome을 자동 시작하지 않고 `Faulted`가 된다.

### 8.4 명시적 disconnect

1. lifecycle gate를 얻는다.
2. epoch를 즉시 바꾸고 새 WebDriver 명령을 차단한다.
3. in-flight 명령을 `DisconnectDrain`까지 기다린다.
4. controlled target 복구 정보를 snapshot한다.
5. executor를 detaching으로 전환한다.
6. 소유 ChromeDriver PID를 종료한다.
7. stale WebDriver client와 transport를 bounded dispose한다.
8. CDP로 Chrome PID와 target 건강성을 확인한다.
9. 건강하면 `CdpOnly`, 아니면 `Faulted`를 commit한다.

### 8.5 reconnect

1. `CdpOnly`와 Chrome/CDP 건강성을 확인한다.
2. target registry를 갱신하고 controlled target의 존재를 확인한다.
3. 새 ChromeDriver process와 WebDriver session을 만든다.
4. target과 WebDriver current context를 조정한다.
5. 모호하면 임의 선택하지 않고 `AmbiguousTarget`으로 실패한다.
6. 성공한 경우에만 generation을 증가시키고 새 epoch를 commit한다.

### 8.6 최종 종료

최종 `StopAsync`/`DisposeAsync`는 disconnect와 달리 library-owned Chrome까지 종료한다.

1. 새 lifecycle/command 요청을 닫고 epoch를 폐기한다.
2. in-flight WebDriver 명령을 bounded drain한다.
3. attachment가 있으면 detaching executor와 정확한 ChromeDriver PID를 정리한다.
4. Chrome에 정상 종료를 요청하고 제한 시간 동안 기다린다.
5. 필요하면 기록한 library-owned process tree만 강제 종료한다.
6. CDP read loop, event channel과 socket을 종료한다.
7. background task와 event subscription을 drain한다.
8. caller-owned profile lock을 해제한다.
9. marker가 일치하는 temporary profile만 삭제한다.
10. 잔존 PID, port, file handle과 cleanup failure를 최종 health에 기록한다.

호출자 cancellation은 1단계 진입을 재촉할 수 있지만, 시작된 cleanup은 별도 scope에서 최대 10초까지 계속된다.

## 9. 동시성 모델

### 9.1 Lifecycle gate

start 이후 navigation, disconnect, reconnect와 stop은 인스턴스별 단일 async gate로 직렬화한다. 같은 gate를 기다리는 operation은 자신의 cancellation과 전체 deadline을 유지한다.

stop이 요청되면 대기 중인 새 operation보다 우선하며, 이후 요청은 `ObjectDisposed` 또는 상태 오류로 빠르게 실패한다.

### 9.2 WebDriver command admission

guarded command는 다음 순서를 따른다.

1. 현재 state와 lease epoch를 검사한다.
2. admission이 열려 있으면 in-flight counter를 증가시킨다.
3. 증가 직후 epoch를 다시 확인하여 disconnect와의 race를 닫는다.
4. Selenium command를 실행한다.
5. `finally`에서 counter를 감소시킨다.

disconnect는 admission을 닫고 epoch를 회전한 뒤 counter가 0이 될 때까지 제한 시간만 기다린다. timeout이면 attachment process를 종료하고 command 결과를 `AttachmentLost`로 정규화한다.

### 9.3 CDP transport와 event

CDP request/response는 numeric id로 multiplex한다. target mutation과 navigation은 target별 queue로 순서를 보존한다.

transport read loop는 message parsing과 internal channel publish만 수행한다. 사용자 event handler와 logging sink는 별도 scheduler에서 실행한다. event buffer overflow는 정책에 따라 오래된 관찰 event를 버릴 수 있지만 target lifecycle event는 버리지 않고 connection을 unhealthy로 표시한다.

## 10. 오류와 복구 경계

오류는 발생 위치가 아니라 호출자가 취할 조치에 맞춰 분류한다.

| 분류 | 기본 복구 | 상태 결과 |
|---|---|---|
| configuration/version/patch | 자동 복구 없음 | `Stopped` 또는 `Faulted` |
| ChromeDriver start/session failure | 정확한 driver PID 정리 후 재시도 한도 적용 | Chrome/CDP가 건강하면 `CdpOnly` |
| attached command transport failure | epoch 폐기 후 attachment 제거 | `CdpOnly` 또는 `Faulted` |
| CDP socket drop | 같은 Chrome endpoint에 1회 복구 | 성공 시 기존 큰 상태 유지 |
| target close/crash | 다른 target로 자동 전환하지 않음 | 명시 target 오류 |
| Chrome exit | 자동 browser restart 없음 | `Faulted` |
| cleanup incomplete | 잔존 자원 진단 보존 | `Faulted` |

복구는 “CDP socket 재연결 → WebDriver attachment 재생성”까지만 자동 허용한다. Chrome 재시작과 profile state migration은 후속 opt-in 기능이다.

## 11. Timeout과 cancellation 전파

모든 public async 호출은 하나의 operation deadline을 만든다. 실제 단계 timeout은 `min(단계 기본값, operation 남은 시간, 호출자 deadline)`이다.

동기적인 WebDriver new-session과 Selenium 명령은 전용 작업에서 실행한다. deadline이 지나면 작업이 스스로 취소됐다고 가정하지 않고 정확한 ChromeDriver PID를 종료해 I/O를 끊는다. 그 뒤 Chrome 상태를 CDP로 다시 확인한다.

cleanup은 caller token과 분리하되 10초 hard cap을 갖는다. timeout 숫자의 기준값은 [spec.md §14.1](spec.md#141-timeout과-cancellation)을 따른다.

## 12. 데이터와 영속성

MVP에서 서버형 데이터베이스는 없다.

### 12.1 메모리 상태

- lifecycle state와 operation id
- Chrome/CDP/WebDriver health 축
- PID/create time/path와 endpoint
- target registry와 target session
- runtime script registry
- session generation과 attachment epoch
- in-flight command와 background task registry

### 12.2 파일 상태

- temporary 또는 caller-specified Chrome profile
- profile ownership marker와 lock
- immutable source driver metadata
- patched driver cache와 manifest
- 선택적 진단 파일

marker와 manifest는 schema version을 가진다. 알 수 없는 새 schema를 예전 버전이 수정하거나 삭제하지 않는다. 민감한 browser storage는 라이브러리 별도 파일로 복사하지 않는다.

## 13. 보안 경계

- DevTools와 ChromeDriver endpoint는 loopback에만 bind한다.
- endpoint 주소와 profile 경로는 민감한 진단 정보로 취급한다.
- 기본 Chrome profile과 공유 profile 사용을 거부한다.
- executable은 canonical path, version과 hash를 기록한다.
- archive 추출을 추가할 경우 path traversal와 예상하지 않은 entry를 거부한다.
- process 종료는 기록한 PID/create time/path가 일치할 때만 수행한다.
- patch cache는 실행 권한과 쓰기 권한을 최소화하고 원자적으로 publish한다.
- arbitrary CDP API는 고급 기능으로 격리하고 URL allowlist와 redacted logging을 유지한다.

탐지 회피, 인증 우회, CAPTCHA 자동 풀이는 신뢰 경계나 성공 기준에 포함하지 않는다.

## 14. 관측 가능성

각 log event는 가능한 경우 다음 필드를 가진다.

- library/browser/driver/Selenium/protocol version
- browser instance id와 operation id
- 이전/다음 lifecycle state
- generation과 attachment epoch
- Chrome/ChromeDriver PID 및 redacted endpoint
- target key, target/session 변경 원인
- 단계명, 경과 시간, timeout source
- recovery attempt와 cleanup 결과

health snapshot은 “connected” boolean 하나가 아니라 Chrome, CDP, WebDriver, profile과 background task 상태를 각각 보여준다. 내부 flag만으로 건강성을 선언하지 않고 실제 process/endpoint probe 결과와 마지막 성공 시각을 포함한다.

## 15. 배포와 소스 구조 제안

초기 저장소는 다음 경계를 권장한다. 이는 구현 순서와 테스트 위치를 설명하는 제안이며 현재 파일을 생성하라는 뜻은 아니다.

```text
src/
  UcDotNet/                  public API와 lifecycle orchestration
  UcDotNet.Cdp/              최소 CDP transport/target/page/runtime
  UcDotNet.Selenium/         guarded W3C attachment adapter
  UcDotNet.Windows/          process/profile/lock 구현
  UcDotNet.Patching/         artifact 검증과 선택 patch
tests/
  UcDotNet.UnitTests/
  UcDotNet.ContractTests/
  UcDotNet.WindowsTests/
  UcDotNet.LeakTests/
```

배포 package를 반드시 프로젝트 수만큼 나누지는 않는다. `UcDotNet.NativeInput`과 `UcDotNet.Legacy`만 후속 선택 package로 격리한다.

## 16. 테스트 아키텍처

### 16.1 단위 테스트 seam

- clock와 deadline source
- process launcher와 process probe
- file system/profile lock
- CDP transport와 event stream
- ChromeDriver status endpoint
- WebDriver command executor
- patch recipe와 hash provider
- logging/event sink

process와 socket을 흉내 낸 단위 테스트는 상태 전이와 cleanup 순서를 빠르게 검증한다. 실제 브라우저 수용 시험을 대체하지 않는다.

### 16.2 Contract test

- 고정 Selenium package에 대한 public constructor/executor/dispose 경로
- Chrome major별 사용 CDP method와 event shape
- ChromeDriver `/status`와 W3C new-session 응답
- patch fixture별 pattern count와 결과 hash

### 16.3 Windows 통합 테스트

최우선 release gate는 다음이다.

1. disconnect 뒤 동일 Chrome PID와 CDP endpoint 유지
2. 새 W3C SessionId로 reconnect하고 기존 browser state 확인
3. disconnect 시작 즉시 이전 lease/element의 로컬 실패
4. 반복 실행 뒤 owned process, port, socket, task, profile 누수 없음
5. CDP와 ChromeDriver 동시 연결 중 target/session event 일관성

세부 수치와 반복 횟수는 [spec.md §20](spec.md#20-검증-가능한-수용-기준)을 따른다.

## 17. 주요 아키텍처 결정 기록

| 결정 | 선택 | 이유 | 재검토 조건 |
|---|---|---|---|
| 제어 구조 | 장수명 CDP + 교체형 WebDriver | disconnect 중 browser 제어와 W3C 기능을 함께 제공 | 동시 CDP/ChromeDriver 충돌이 수용 시험에서 확인될 때 |
| reconnect | 새 attachment 생성 | Selenium 객체 내부 재초기화와 reflection 제거 | 공식 reusable attachment API가 생길 때 |
| public WebDriver | guarded facade | epoch/lifecycle 우회 방지 | 안전한 raw proxy 계약과 완전한 command interception이 입증될 때 |
| stale guard | state + attachment epoch | disconnect 직후 즉시 무효화 | 없음; 핵심 불변조건 |
| target 선택 | 명시적 `TargetKey` | window 순서 기반 오선택 방지 | 표준화된 WebDriver/CDP target identity가 제공될 때 |
| driver lifecycle | 직접 child process 소유 | PID/readiness/cancellation/cleanup 통제 | Selenium public service API가 같은 통제를 제공할 때 |
| binary patch | 기본 off, 별도 strategy | 버전 위험과 lifecycle 분리 | 안정된 공식 대체 수단이 생기거나 기능을 제거할 때 |
| 외부 Chrome attach | MVP 제외 | 프로세스·profile 소유권과 보안 경계가 불명확 | 별도 API와 수용 시험이 설계될 때 |
| native GUI input | 별도 후속 package | interactive desktop/DPI/RDP 등 환경 의존 | 명확한 사용 요구와 전용 CI가 생길 때 |

## 18. 구현 전에 남은 검증

다음은 구조의 타당성을 좌우하므로 실제 Windows fixture에서 먼저 확인한다.

1. `detach=true`, Quit 차단과 ChromeDriver PID 종료 조합이 Chrome을 확실히 유지하는가?
2. `DetachAwareCommandExecutor`의 sync/async dispose가 죽은 endpoint에서 hard cap 안에 끝나는가?
3. 독립 CDP client와 ChromeDriver의 동시 연결이 target event와 navigation을 방해하지 않는가?
4. 재연결한 WebDriver와 controlled CDP target의 대응을 결정적으로 확정할 수 있는가?
5. Chrome/ChromeDriver 각 지원 버전에서 debugger attach 제한 명령은 무엇인가?
6. 선택 patch recipe가 실제 Stable/Stable-1 binary에서 선언한 match 수와 실행 가능성을 만족하는가?

검증 결과가 1~4번 가정을 깨면 세부 구현을 우회해서 맞추지 않는다. 해당 아키텍처 결정을 갱신하고 [spec.md](spec.md)의 요구사항·상태도·수용 기준도 함께 수정한다.
