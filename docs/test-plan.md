# SeleniumBase를 기준으로 한 BrowserDock 테스트 계획

작성일: 2026-09-11 · 조사한 BrowserDock 커밋: `83a2fb4`

이 문서는 **테스트 수행 계획**이다. 최초 작성 시에는 테스트를 실행하지 않았으며, 후속 구현·실행 상태는 아래 구현 현황과 [검증 기록](implementation.md)에 구분한다. 우선순위는 **공통 회귀 → Windows 실제 lifecycle → SeleniumBase 동작 비교 → 실패 복구·누수 → 지원 버전별 승인**이다.

## 후속 구현 현황

후속 작업에서 `tests/seleniumbase-reference`의 버전 고정·공통 manifest·Python runner·결과 비교기와 .NET `ReferenceTests`를 추가했다. SeleniumBase 원본 버전 파일과 Driver/UC 소스를 재조회하여 기준 커밋의 버전이 **4.53.7**임을 확인했다. 실행 방법은 [비교 runner 안내](../tests/seleniumbase-reference/README.md)를 따른다.

추가한 검사: admission 동기화·원격 호출 차단, driver 정리 제한 시간, 실제 프로세스에서 멈춘 W3C session 생성, reconnect timeout, 실행 중 명령과 disconnect, cleanup 중 caller 취소, CDP task/socket/subscription 정리, 네트워크 pending/실패/잘못된 page session, CDC 스크립트 검증·중복 제거, 중첩 iframe, CDP/WebDriver 100회 target 동시 관찰, logger 지연·overflow·예외, Legacy net8 실행 대상. Windows runner는 stress를 명시적으로 제외/포함하고 필수 시험 skip·0건·미생성 TRX를 실패로 처리한다. 별도 수동 Windows 11 workflow도 제공한다.

아래 단계별 설명은 원래의 목표와 추가 검증 기준을 유지한다. Windows 브라우저·SeleniumBase 실제 비교·PowerShell 실행은 지원 환경에서 검증해야 한다. 별도 프로세스 간 profile/cache 경쟁, 모든 fault 지점, 개별 파일 핸들 귀속, 실제 패치 recipe 검증은 아직 추가 검증 대상으로 남는다. 단순 계측 코드 추가를 실제 수용 시험 통과로 해석하지 않는다.

## 1. 비교 범위와 판정 기준

BrowserDock은 SeleniumBase UC Mode의 동작을 참고한 독립 .NET 구현이다. SeleniumBase 전체 테스트 프레임워크와 API가 동일한 제품은 아니다. 비교 기준은 Chrome을 유지하면서 WebDriver를 분리·재연결하는 동작, 페이지 이동, 브라우저 상태 보존이다. 제품의 최종 합격 기준은 [spec.md의 AC-01~10](spec.md#20-검증-가능한-수용-기준)을 따른다.

SeleniumBase 공식 UC 문서는 Chrome을 먼저 시작하고 ChromeDriver를 연결하며, 필요할 때 연결을 해제하는 구조와 `disconnect()`, `connect()`, `uc_open_with_reconnect()`를 설명한다. 이 동작을 테스트 시나리오의 출발점으로 삼는다. [공식 UC Mode 문서](https://seleniumbase.io/help_docs/uc_mode/)

SeleniumBase의 예제는 pytest 실행, 매개변수화, 실패 로그와 보고서 등을 제공한다. 이 프로젝트에서는 NUnit과 TRX로 같은 운영 목적을 달성하고, Python은 비교 실행 도구로만 추가한다. 예제 중에는 실패를 의도한 것도 있으므로 전체 예제 폴더의 성공률을 제품 기준으로 삼지 않는다. [공식 예제 실행 문서](https://seleniumbase.io/examples/ReadMe/)

| 비교 시나리오 | SeleniumBase 기준 경로 | BrowserDock 대응과 확인할 결과 |
|---|---|---|
| 일반 이동·DOM 조작 | UC의 `default_get()`, find/click/type | `NavigateAsync(Standard)`와 lease 명령으로 최종 URL·텍스트·입력값 확인 |
| 브라우저를 유지한 연결 해제 | `driver.disconnect()` | `DisconnectWebDriverAsync()` 후 기존 Chrome 생존, driver 종료, CDP 조회 성공 |
| 재연결 | `driver.connect()` / `reconnect()` | `ReconnectWebDriverAsync()` 후 새 session, 기존 cookie·storage·동일 문서의 DOM 보존 |
| 연결을 끊은 이동 후 복구 | `uc_open_with_reconnect()` | `Detached` + `ReconnectAfterNavigation=true`; 최종 문서·연결 상태 확인 |
| 연결을 끊은 상태 유지 | `uc_open_with_disconnect()` | `Detached` + `ReconnectAfterNavigation=false`; `CdpOnly` 유지 |
| CDP로 제어 전환 | `activate_cdp_mode()` | `EnterCdpOnlyAsync()` 후 `NavigateAsync(CdpOnly)`·`ExecuteCdpAsync()` |
| 브라우저 종료 | manager 종료 / `quit()` | browser의 `StopAsync()`·`DisposeAsync()` 후 소유 자원 정리 |

이 표는 시나리오 대응표이며 내부 호출 순서의 완전한 동등성을 뜻하지 않는다. SeleniumBase의 CDP Mode는 별도 CDP 동작과 WebDriver 호출 전환을 제공한다. BrowserDock의 CDP API 전체가 이에 대응한다고 가정하지 않는다. [공식 CDP Mode 문서](https://seleniumbase.io/examples/cdp_mode/ReadMe/)

다음 차이는 실패로 판정하지 않고 BrowserDock의 계약을 별도로 검사한다.

- `CdpOnly` 이동 전에 명시적으로 연결을 해제해야 한다. 암묵적인 재연결은 기대하지 않는다.
- 기존 lease·element는 disconnect 시작 후 `StaleAttachmentException`으로 로컬에서 실패해야 한다. Python 객체 재사용 방식과 같을 필요가 없다.
- 여러 탭이 모호하면 `AmbiguousTargetException`을 내고 명시적으로 선택한다. 마지막 탭을 임의 선택하지 않는다.
- SeleniumBase의 새 탭 기반 UC 이동에 가까운 비교는 `TargetPolicy.ReplaceControlled`로 별도 구성한다. 일반 disconnect/reconnect의 DOM 보존 시험과 문서를 교체하는 이동 시험을 구분한다.
- GUI 입력·CAPTCHA 처리·headless·자동 드라이버 다운로드·사이트별 사전 HTTP 판별은 현재 MVP 범위 밖이다. 외부 사이트 관찰 결과는 AC-10에 따라 참고 기록으로만 남긴다.

기존 명세가 참조한 SeleniumBase SHA는 `4ee7dfc4ae83c19385f5ac129f2cda0cfa863d80`이다. 이번 조사에서는 이 고정 소스의 웹 재조회가 실패했으므로 세부 동작은 기존 명세와 현재 공식 문서를 구분해 참고했다. 비교 harness를 만들 때 해당 SHA의 checkout 가능 여부와 실제 구현을 먼저 확인한다. 불가능하면 실행 가능한 release/SHA를 새 기준으로 확정하고 변경 사유를 기록한다. [기존 기준과 소스 링크](spec.md#21-분석한-저장소)

## 2. 현재 준비된 테스트와 검증 상태

| 영역 | 현재 자산 | 현재 판단 |
|---|---|---|
| 단위 시험 | `tests/BrowserDock.Tests/UnitTests.cs` | admission 경쟁, 버전, navigation 조합, 패치·프로필 등 |
| 프로토콜 계약 | `ContractTests.cs`, `NavigationContractTests.cs` | 실제 HTTP/WebSocket fixture를 통한 W3C·CDP 응답, stale 차단, loader·복구 검사 |
| 실제 Chrome | `tests/BrowserDock.Tests/WindowsTests.cs` | lifecycle·이동·탭·취소·crash·attach 제약·stress 구현됨; Windows 실행 대기 |
| Legacy/Framework | `tests/BrowserDock.FrameworkTests` | 호환성·프로토콜과 Windows Legacy 시험; net481 실행 검증 필요 |
| 테스트 서버 | core의 `LocalServer`, `tests/BrowserDock.FixtureHost` | `/page`, `/frame`, `/redirect`, `/slow`, `/download` 등 활용 가능 |
| 실행 스크립트 | `scripts/test-windows.ps1` | 버전·hash·runtime 메타데이터, TFM별 TRX, `-Stress` 지원 |
| CI | `.github/workflows/ci.yml` | 공통 시험 및 Windows net481 패키지 소비 검사 구성; 실제 Windows 11 Chrome 시험 제외 |

[구현 기록](implementation.md)에 따르면 2026-09-10 .NET 10 Release에서 기존 공통 시험 34개와 호환성 시험 21개가 통과했고 Windows 전용 22개·12개는 skip됐다. 이는 이전 기록이며 이번 커밋에서 재실행한 결과가 아니다. 원격 CI의 성공 여부도 이번 작업에서는 확인하지 않았다.

현재 작업 환경에는 .NET 10 runtime만 설치되어 있다. macOS에서 공통 시험을 수행할 수 있지만 Windows 11 x64의 실제 Chrome 및 net481 실행 승인은 별도 환경에서 받아야 한다.

## 3. 실행 환경과 버전 고정

| 실행 층 | 환경·TFM | 실행 시점 |
|---|---|---|
| 공통 회귀 | 기존 CI의 Ubuntu/Windows, core net8.0·net10.0 및 Legacy net10.0 | 모든 PR |
| Framework 공통·소비 | 실제 .NET Framework 4.8.1, C# 7.3/x64, Windows MSBuild | 모든 PR |
| 첫 브라우저 검증 | Windows 11 x64, 대화형 데스크톱, CfT Stable, core net10.0 | 가장 먼저 실행 |
| 브라우저 전체 | 같은 환경에서 core net8.0·net10.0, Legacy net481·net10.0 | 정기 회귀·release |
| 브라우저 버전 회귀 | 위 네 실행 조합 × CfT Stable·Stable-1 | release |
| 설치형 Chrome 확인 | Windows 11 x64의 일반 Chrome Stable와 대응 driver | release 전 프로필·endpoint·lifecycle 확인 |

PowerShell 7, .NET 10 SDK, .NET 8/10 runtime, Framework 4.8.1 runtime을 준비한다. Framework 테스트용 서버 때문에 테스트 머신에는 .NET 10이 필요하지만 제품의 net481 실행 의존성은 아니다. 패키지 소비 시험에는 Visual Studio Build Tools/MSBuild도 필요하다.

Chrome/ChromeDriver는 M115 이상이며 프로젝트 계약상 `MAJOR.MINOR.BUILD`가 일치해야 한다. 재현성을 위해 같은 전체 버전의 CfT pair를 우선 사용한다. Stable-1은 이전 milestone의 확보 가능한 pair를 명시적으로 고정한다. 버전 번호를 문서에 추정해 적지 않고 준비 시점의 공식 배포 정보를 기록한다. [ChromeDriver 공식 버전 선택](https://developer.chrome.com/docs/chromedriver/downloads/version-selection)

각 실행에서 BrowserDock SHA, SeleniumBase SHA·패키지 버전·Python 의존성 목록, OS build, TFM, Chrome/driver 전체 버전·SHA256, launch 옵션, patch mode, fixture 버전을 남긴다. SeleniumBase는 UC용으로 수정된 driver를 쓸 수 있으므로 vendor 원본과 실제 실행 artifact의 버전·hash를 따로 기록한다. 두 구현에 동일한 패치 바이너리를 강제로 공유하지 않는다.

각 테스트는 독립 profile·browser instance를 사용한다. 공통 브라우저 시험은 직렬 실행하고 20개 동시 실행은 전용 stress 시나리오 안에서만 수행한다. 일반 hosted Windows runner의 공통 시험 통과와 Windows 11 대화형 브라우저 시험 결과를 구분한다.

## 4. 수행 순서

### 단계 1 — 기존 공통 회귀 재실행

저장소 루트에서 실행한다. 이 명령은 현재 파일로 실행 가능하다.

```sh
dotnet restore BrowserDock.slnx
dotnet build BrowserDock.slnx --no-restore -m:1
dotnet test tests/BrowserDock.Tests/BrowserDock.Tests.csproj -f net10.0 --no-build --no-restore --filter 'TestCategory!=Windows' --logger 'trx;LogFileName=core-net10.trx' --results-directory .artifacts/test-plan/common
dotnet test tests/BrowserDock.FrameworkTests/BrowserDock.FrameworkTests.csproj -f net10.0 --no-build --no-restore --filter 'TestCategory!=Windows' --logger 'trx;LogFileName=legacy-net10.trx' --results-directory .artifacts/test-plan/common
```

각 명령의 종료 코드를 확인하고 실패하면 다음 단계로 진행하지 않는다. .NET 8 설치 환경에서는 core를 `-f net8.0`으로, 실제 Framework 환경에서는 FrameworkTests를 `-f net481`로 추가 실행한다. 합격 조건은 발견된 필수 시험 모두 통과, 예상하지 못한 skip·0건 실행 없음이다.

### 단계 2 — 핵심 Windows smoke

Windows 준비 후 전체 빌드까지 완료한 상태에서 가장 중요한 한 시험을 먼저 실행한다.

```powershell
$env:BROWSERDOCK_CHROME = 'C:\Chrome\chrome.exe'
$env:BROWSERDOCK_DRIVER = 'C:\Chrome\chromedriver.exe'
$env:BROWSERDOCK_STRESS = '0'
dotnet test tests/BrowserDock.Tests/BrowserDock.Tests.csproj -f net10.0 --no-build --no-restore --filter 'FullyQualifiedName~AC01_02_03_DisconnectPreservesChromeAndReconnectReplacesSession' --logger 'trx;LogFileName=lifecycle-smoke.trx' --results-directory .artifacts/test-plan/smoke
```

이 시험은 의도적으로 30초간 disconnected 상태를 유지한다. Chrome PID·생성 시각·profile·endpoint 유지, driver 종료, 새 SessionId·generation, cookie·localStorage·DOM 보존, stale/reacquire를 확인한다. skip은 준비 실패로 처리한다. 실패 시 버전·프로필·attach·정리 문제를 해결한 후에 전체 시험으로 확장한다.

### 단계 3 — 기존 Windows 전체 시험

```powershell
./scripts/test-windows.ps1 -Chrome C:\Chrome\chrome.exe -Driver C:\Chrome\chromedriver.exe -Results .artifacts/test-plan/stable-normal
./scripts/test-windows.ps1 -Chrome C:\ChromePrevious\chrome.exe -Driver C:\ChromePrevious\chromedriver.exe -Results .artifacts/test-plan/stable-minus-one-normal
```

각 호출은 core net8.0·net10.0과 Legacy net481·net10.0을 실행한다. 스크립트는 테스트 실패 시 중단하므로 생성되지 않은 뒤쪽 TFM 결과도 미실행으로 기록한다. 반복 실행은 별도 결과 디렉터리를 지정하여 기존 TRX와 `fixture.json`을 보존한다.

### 단계 4 — SeleniumBase와 같은 로컬 페이지 비교

**Python harness와 공통 manifest를 구현했다.** `run.py`는 FixtureHost의 시작 시 출력되는 `BROWSERDOCK_FIXTURE_URL`을 두 runner에 전달한다. `/reference/*` 경로는 공유 HTML 구현을 사용하므로 기존 core·Framework 페이지의 차이와 무관하게 같은 콘텐츠를 비교한다.

1. Python venv에 검증한 SeleniumBase SHA/release와 의존성을 고정한다. Chrome 경로와 실행된 driver artifact를 검증한다.
2. SeleniumBase와 BrowserDock에 동일 origin·HTML·초기 storage·window 조건을 주되, 서로 다른 profile에서 순차 실행한다.
3. `SB-01` 일반 이동·버튼·입력·iframe, `SB-02` 같은 문서에서 disconnect/connect, `SB-03` detached navigation+reconnect, `SB-04` CDP-only 이동을 우선 구현한다.
4. `SB-02`에서는 동일 Chrome 생존·session 교체·cookie/storage/DOM을 검사한다. `SB-03`에서는 문서 교체 후 최종 URL·title·요소 상태·storage를 검사하고 이전 DOM marker 보존은 요구하지 않는다.
5. `ReplaceControlled` 이동과 다중 탭은 별도 시나리오로 추가한다. BrowserDock의 명시 target·stale 오류는 프로젝트 전용 기대값으로 기록한다.
6. 고정 sleep 대신 요소·URL·lifecycle 조건을 기다린다. 연결 해제 시간 자체를 검사하는 구간은 별도 monotonic timer로 측정한다. Python의 reconnect 대기값과 .NET의 `ReconnectDelay`를 내부 순서까지 같은 것으로 간주하지 않는다.
7. 시나리오마다 fresh profile로 최소 3회 실행한다. 관찰 가능한 결과를 JSON으로 저장하고 첫 실패를 보존한다. 재실행 성공으로 최초 실패를 지우지 않는다.

공통 결과 schema: `scenarioId`, 구현·버전, 실행 모드, 최종 URL·title·fixture 값, 전후 Chrome identity, session 교체 여부, 제어 target, 오류 범주, 경과 시간, cleanup 결과. 서로 다른 실행 사이에 PID·SessionId 원문이 같을 것을 요구하지 않고 **각 실행 안에서 유지·변경되어야 할 관계**를 비교한다. BrowserDock에만 있는 generation·epoch는 별도 필드다.

Python의 일반 시험은 pytest의 JUnit XML/실패 로그로, .NET은 NUnit TRX로 저장한다. 프로세스 반환이나 내부 connected flag만 보지 않고 실제 DOM 명령·CDP 응답·프로세스 생존을 함께 검사한다. SeleniumBase만 실패하면 원본 버전·환경을 조사하고, BrowserDock만 실패하면 공통 계약인지 의도된 차이인지 먼저 분류한다.

### 단계 5 — 실패·경계 조건 강화

| 우선순위 | 추가·강화할 검사 | 합격 조건 |
|---|---|---|
| P0 | AC-01 driver 종료 시간, AC-03 disconnect 진입 동기화 | driver 5초 내 종료; `Task.Delay(1)`에 의존하지 않고 admission 폐쇄 시점을 기다린 뒤 원격 요청 0건으로 stale 확인 |
| P0 | 멈춘 new-session, reconnect timeout, cleanup 중 취소 | 단계별 typed 오류, generation 유지, 정리 전체 10초 상한과 소유 PID·transport 정리 확인 |
| P0 | 100회 반복에서 CDP·WD target 생성/닫기 및 event 상호 관찰 | 잘못된 session dispatch·event 누락·다른 탭 선택 없음; 단순 start/stop 100회와 별도 입증 |
| P0 | 누수의 background task·event subscription·socket·자식 process tree 계측 | stop 후 라이브러리 작업 0, 소유 자원 잔존 없음; handle/thread 합계만으로 대체하지 않음 |
| P1 | `ReplaceControlled`, 선택 target 종료·crash, 동일 URL 여러 탭 | 제어 target만 교체, 무관 탭 생존, 모호함·닫힘·crash의 정확한 오류 |
| P1 | 지연 DOM·중첩 iframe·재획득·부적합 locator | frame 경로 재현, DOM stale과 attachment stale 분리, 정해진 timeout |
| P1 | 지연·실패 요청과 지속 네트워크 요청 | 4가지 WaitUntil 계약, 이전 loader 무시, NetworkIdle 미달 시 timeout |
| P1 | new-document script·CDC 옵션 off/on·CDP 복구 | 문서별 중복 없는 실행, 복구 후 재등록, 정상 속성 보존; 탐지 사이트 통과로 대체하지 않음 |
| P1 | 동일 profile·patch cache를 서로 다른 테스트 프로세스에서 경쟁 | profile 두 번째 사용 명시 실패, 단일 유효 cache, 부분 파일 없음 |
| P1 | logger 지연·overflow·실패 및 payload | lifecycle 정지 방지 또는 bounded failure, overflow 진단, 기본 로그에 민감 payload 없음 |
| P1 | Legacy net8 소비·실행 smoke, WinForms/WPF await·정리 | 실제 제공하는 net8 Legacy asset도 확인; 현재 FrameworkTests에는 net8 target이 없으므로 전용 consumer 추가 |

현재 AC-07 resize 시험은 지원/미지원 결과를 기록한다. 버전별 baseline을 만든 뒤 성공 시 실제 크기, 미지원 시 `UnsupportedAttachedCommand`를 확인하도록 강화한다. 다른 예외를 단순 미지원으로 처리하지 않는다.

### 단계 6 — stress와 자원 정리

```powershell
./scripts/test-windows.ps1 -Chrome C:\Chrome\chrome.exe -Driver C:\Chrome\chromedriver.exe -Stress -Results .artifacts/test-plan/stable-stress
./scripts/test-windows.ps1 -Chrome C:\ChromePrevious\chrome.exe -Driver C:\ChromePrevious\chromedriver.exe -Stress -Results .artifacts/test-plan/stable-minus-one-stress
```

기존 stress는 warm-up 5회 후 측정 100회와 20개 병렬 인스턴스를 제공한다. AC-04에 따라 handle 증가 ≤5, thread 증가 ≤2, 마지막 20회 private bytes 중앙값 ≤baseline의 120%를 적용한다. 라이브러리 background task 0 및 process tree·socket·file handle 완전 정리는 단계 5의 추가 계측으로 확인한다. driver/port 5초, profile 삭제 10초, profile lock 해제 5초의 수치도 직접 측정한다.

core의 `AC05_TwentyInstancesHaveDistinctEndpointsAndPorts`에 `[Category("Stress")]`를 추가했다. `-Stress`는 category 선택과 환경변수를 함께 설정하며 normal 실행에서는 stress 시험을 필터로 제외한다.

현재 runner는 normal 실행에서 stress 시험을 선택하지 않는다. `-Stress` 실행에서는 모든 필수 Windows/stress 시험이 실제 실행되어야 한다. Windows에서 권한 때문에 의도적으로 skip하는 symlink 단위 시험은 명시적 필터로 제외하며 해당 경로의 별도 검증 증거를 둔다. 선택된 시험의 skip은 허용하지 않는다.

### 단계 7 — Framework·패키지 소비

기존 CI 절차대로 core/Legacy를 Release pack하고, ProjectReference가 없는 `tests/BrowserDock.ClassicConsumer`를 Windows MSBuild로 빌드한다. binding redirect·x64·C# 7.3 및 net481 의존 DLL 선택을 확인한다. [현재 실행 절차](framework481.md)

인자 없이 실행하여 usage 종료 코드 2를 확인하는 현재 CI는 로딩 smoke다. release에서는 실제 Chrome 인자를 전달한 소비 프로그램의 navigation·disconnect/reconnect·종료까지 확인한다. net10에서 같은 테스트 코드가 통과해도 net481 성공으로 대체하지 않는다.

### 단계 8 — 패치 선택 기능

기본 release 검증은 `PatchMode = DriverPatchMode.Disabled`로 수행한다. 현재 실물 recipe가 없으므로 합성 fixture 성공은 패치 엔진의 검증으로만 기록한다.

실물 `BinaryCompatibility` 지원을 선언하려면 Stable·Stable-1별 recipe 범위·원본 hash·정확한 match 수·결과 hash를 고정하고, 패치 결과의 실제 launch·attach·disconnect/reconnect를 다시 실행한다. `ValidateOnly`, 알 수 없는 recipe, 0건·초과 match, cache 변조·동시 생성을 포함하고 vendor 원본의 byte 보존을 검사한다. recipe가 없으면 해당 호환성 승인은 미검증 상태로 남긴다.

## 5. 결과 수집과 release 승인

기존 `fixture.json`과 TFM별 TRX에 다음을 추가할 계획이다.

- 실행 명령·git SHA·fixture/SeleniumBase 버전 및 지원 matrix별 pass/fail/skip/not-run.
- 실패 직전 상태·generation·epoch·소유 PID·endpoint probe 결과·cleanup failure·monotonic 경과 시간.
- 로컬 fixture에 한한 screenshot·HTML·정규화된 관찰 JSON. 브라우저가 죽은 경우에는 수집 실패 자체와 마지막 확보 진단을 기록한다.
- 반복별 handle/thread/private bytes와 소유 자원 목록. 평균만 남기지 않고 원시 측정값을 보존한다.

진단 수집은 별도 제한 시간 안에서 수행하고 원래 실패를 덮어쓰지 않으며, 수집 실패 여부와 관계없이 finally에서 cleanup한다. 테스트용 artifact 수집 때문에 제품 기본 로그의 비수집 정책을 바꾸지 않는다.

승인 조건은 다음과 같다.

1. 필수 TFM·브라우저 pair의 공통 시험과 실제 Windows 시험이 모두 실행되고 통과한다. 예상 밖 skip·0건 실행·미생성 TRX는 승인 불가다.
2. AC-01~03을 실제 브라우저로 입증하고, 지원 범위에서 AC-04~09의 누수·병렬·fault·target 항목을 계측 결과로 충족한다. AC-08의 실물 패치는 지원 여부를 별도로 명시한다.
3. SeleniumBase 공통 시나리오 결과가 일치하고 의도된 차이는 대응표에 기록한다. 이 비교만으로 .NET 고유의 stale·취소·정리 계약을 승인하지 않는다.
4. 추가가 필요한 P0 검사를 완료한다. P1 중 명세상 필수 동작은 release 전에 확인하고, 미지원 기능은 지원 matrix에서 명확히 구분한다.
5. `docs/implementation.md`에 실제 실행 환경·결과·미검증 항목을 갱신한다. 첫 실패와 재실행 결과를 함께 기록하며 외부 탐지 사이트 성공을 합격 조건에 넣지 않는다.

## 6. 구현할 작업 단위

| 순서 | 작업 | 산출물·완료 판단 |
|---|---|---|
| 1 | 환경 확보·기존 공통 및 Windows smoke 실행 | 버전/hash·TRX, AC-01~03 최초 실측 |
| 2 | category·실행 누락 판정·시간 계측 보완 | 필수 시험 skip 시 실패하는 실행 검증, 명시적 timing 결과 |
| 3 | 공통 HTML fixture와 비교 runner 작성 | 제안 경로 `tests/seleniumbase-reference/`, scenario JSON, SB-01~04 비교 보고 |
| 4 | P0 fault·동시 CDP/WD·누수 계측 | 새 회귀 시험과 자원 측정 artifact |
| 5 | P1 기능·Legacy 소비·버전 matrix 확장 | Stable/Stable-1 및 지원 runtime별 보고서 |
| 6 | 전용 Windows 11 실행 자동화와 승인 기록 | 기존 공통 CI + 별도 브라우저 job, 보존된 TRX·메타데이터·승인표 |

처음에는 실행 환경과 기존 핵심 시험의 실제 결과를 확보한다. 그 결과로 가장 위험한 Chrome 보존·재연결 가정을 확인한 후 비교 harness와 확장 시험을 추가한다.
