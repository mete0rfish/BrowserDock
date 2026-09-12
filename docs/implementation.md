# 구현 및 검증 기록

## 2026-09-12 BrowserDock 이름 전환

공개 전 작업명을 `BrowserDock`으로 전환했다. 솔루션과 source/test 프로젝트 경로,
assembly·NuGet ID(`BrowserDock`, `BrowserDock.Legacy`), namespace, 문서, CI 및 배포
검증기를 함께 변경했다. 공개 진입 타입은 `Browser`, 예외 기반 타입은
`BrowserDockException`, 명령 facade는 `IBrowserCommands`다. 프로필/패치 캐시 표식과
Windows 시험 환경 변수도 `browserdock`/`BROWSERDOCK_*`를 사용한다. 아직 공개
release가 없으므로 이전 작업명에 대한 호환 alias는 추가하지 않았다.

검증 결과(macOS arm64, .NET SDK 10.0.103/runtime 10.0.3):

- `BrowserDock.slnx` Release 전체 빌드 성공: 경고 0·오류 0.
- .NET 10 core 공통 시험 44개와 Legacy 공통 시험 22개 통과, skip 0.
- SeleniumBase 비교 판정기 5개와 배포 검증기 5개 통과.
- `BrowserDock.0.2.0-alpha.1`과 `BrowserDock.Legacy.0.2.0-alpha.1`의 nupkg/snupkg
  생성 및 내용 검사 통과. Legacy 패키지가 같은 버전의 core를 의존하는 것도 확인했다.
- ProjectReference가 없는 package consumer는 net8/net10으로 빌드됐고 net10에서
  두 assembly를 로드했다. 이 호스트에는 .NET 8 runtime이 없어 net8 실행은 하지 못했다.
- C# 7.3 classic consumer가 `BrowserDock.Legacy` alpha 버전을 참조하는 것을 MSBuild
  평가로 확인했다. 실제 실행은 Windows/.NET Framework 4.8.1 검증에 남아 있다.
- 저장소 파일에서 이전 작업명과 이전 공개 타입/환경 변수 표기가 남지 않은 것을
  검사했다. 상위 checkout 디렉터리의 로컬 경로명은 저장소 내용이 아니며 이 작업에서
  이동하지 않았다.

실행 결과와 패키지는 `.artifacts/browserdock-rename/`에 보관했다. 실제 Windows
Chrome 시험과 GitHub workflow는 이번 이름 전환 환경에서 실행하지 않았다.

## 2026-09-12 오픈소스 공개 준비 적용

영어 README와 한국어 연결, CONTRIBUTING·SECURITY·CODE_OF_CONDUCT·CHANGELOG,
이슈/PR 양식, Dependabot을 추가했다. CI Action을 공식 저장소에서 확인한 SHA로
고정하고 기본 권한을 읽기로 제한했다. Windows 시험에는 승인용 environment를
지정했다. 환경 보호 규칙은 GitHub에서 별도로 설정해야 한다.

두 패키지와 소비 시험은 `build/Version.props`의 `0.2.0-alpha.1`을 사용한다.
NuGet용 README, 공통 패키지 메타데이터 설정, portable 심볼, 패키지 구조 및 배포
검증기와 후보/승인 배포 workflow를 추가했다. 게시 job은 기본 비활성이며 계정 설정,
같은 커밋의 Windows stress 결과, 필수 실행 시험과 공개용 메타데이터 검증을 요구한다.
상세 절차와 자동화 밖의 확인 항목은 [배포 절차](releasing.md)에 기록했다.

검증 결과(macOS arm64, .NET SDK 10.0.103/runtime 10.0.3):

- Release 전체 빌드: 경고 0·오류 0.
- .NET 10 공통 시험: core 44개·Legacy 22개 통과, skip 0.
- Python 비교 판정기 5개와 배포 검증기 5개 시험 통과. 잘못된 커밋·실패 실행·다른 저장소 증거, 누락된 TFM/심볼·잘못된 의존성·추가 실행 파일·상대 README 링크를 거부하는 경우를 포함한다.
- 두 alpha nupkg와 snupkg 생성, 실제 패키지 내용 검사 통과. 공개용 검사는 저자 정보 미정으로 예상대로 실패했다.
- 패키지만 참조하는 별도 소비 프로젝트의 net8/net10 빌드와 net10 실행/두 라이브러리 로딩 성공. classic 소비 프로젝트가 같은 alpha 버전을 읽는 것을 MSBuild 평가로 확인했다.
- 모든 GitHub YAML 구문 검사와 `git diff --check` 통과. workflow 자체를 GitHub에서 실행했다는 뜻은 아니다.
- 검사 당시 작업 파일 85개와 전체 로컬 refs에서 접근 가능한 Git blob 99개에서 개인키·GitHub/Slack 토큰·AWS access ID 패턴은 발견되지 않았다. 검사 결과는 `.artifacts/open-source/secret-pattern-scan.json`에 보관했다. 이는 제한적 패턴 검사이며 개인정보·출처·비정형 secret 및 원격에만 존재하는 이력의 감사를 대체하지 않는다.

시험 결과와 로컬 패키지는 `.artifacts/open-source/`에 있다. 실제 Windows Chrome,
net8/net481 runtime, classic 소비 앱 실행, PowerShell·GitHub workflow·OIDC 게시와
공개 커밋으로의 Source Link 해석은 이 환경에서 실행하지 않았다. 라이선스/권리자,
비공개 신고 연락처는 소유자 확인 대기다. GitHub 원격은
`https://github.com/mete0rfish/BrowserDock.git`을 `origin`으로 연결했고 패키지
metadata에도 프로젝트 URL을 반영했다. LICENSE 추가·공개 여부 및 GitHub 보호 규칙
설정·NuGet 게시는 아직 수행하지 않았다.

## 2026-09-11 테스트 구현 후속 검증

SeleniumBase 기준 커밋 `4ee7dfc4ae83c19385f5ac129f2cda0cfa863d80`의 버전 파일과 Driver/UC 소스를 직접 확인했다. 비교 harness는 해당 버전인 4.53.7을 사용한다. [테스트 계획](test-plan.md)과 [비교 runner 사용법](../tests/seleniumbase-reference/README.md)을 추가했다.

- 공통 HTML fixture와 SB-01~04 manifest, NUnit reference 시험, Python SeleniumBase 시험·순차 실행기·JSON 비교기를 구현했다. 양쪽 관찰 결과를 각각 기대값과 대조하고 누락·잘못된 구현/시나리오 식별·session 미교체·cleanup 실패를 거부한다.
- Windows 시험의 임의 1ms 대기를 admission barrier로 교체하고 원격 호출 수를 확인한다. stress category, 종료 시간·CDP pending/worker/subscription/socket·Chrome Job Object 잔존 계측을 보강했다.
- 멈춘 W3C new-session을 제공하는 실제 자식 프로세스 fixture, reconnect timeout, 실행 중 WebDriver 명령과 disconnect, cleanup 중 caller 취소, 중첩 iframe 재획득, CDP/WD의 100회 target 생성·선택·닫기 시험을 추가했다.
- 공통 시험에서 네트워크 pending/실패/완료·다른 page session 이벤트, script 중복·CDC 이름 검증·복구, logger 지연/overflow/예외를 검사한다. 기존 Legacy 입력 시험의 `value` HTML 속성 검사를 실제 DOM property 검사로 바로잡았다.
- FrameworkTests에 net8.0을 추가했다. Windows 스크립트는 core net8/net10과 Legacy net481/net8/net10을 실행하고 필수 fixture 누락·0건·skip·비통과 TRX를 거부한다. 이전 결과를 덮어쓰지 않는다. 수동 Windows 11 대화형 self-hosted workflow를 추가했으며 runner 등록과 실제 실행은 별도다.

실행한 검증(macOS arm64, .NET SDK 10.0.103/runtime 10.0.3):

- `dotnet build BrowserDock.slnx --no-restore --disable-build-servers -m:1`: 모든 대상 빌드 성공, 경고 0·오류 0.
- .NET 10 core 공통 시험 **44개 통과**, Legacy/Framework 공통 시험 **22개 통과**, skip 0. 결과: `.artifacts/test-implementation/verified-core.trx`, `verified-legacy.trx`.
- Python 결과 비교기 **5개 시험 통과**, runner·reference Python 소스의 구문 컴파일 성공.
- Windows 시험은 discovery 및 컴파일만 확인했다. 실제 Chrome/SeleniumBase 비교·stress, net481/net8 runtime 실행, PowerShell 스크립트와 self-hosted workflow는 이 환경에서 실행하지 않았다. 이번 작업 중 NuGet 복원을 거쳐 추가한 net8 target을 빌드했지만 net8 실행 통과로 표현하지 않는다.

남은 검증: Windows 실제 결과 및 full source/runtime matrix, 개별 파일 핸들 귀속 분석, 프로세스 간 profile/cache 경쟁, 모든 fault 지점, 실제 바이너리 patch recipe. 내부 자원 계측은 라이브러리가 추적하는 작업·구독·socket 및 Chrome Job Object를 검사하며 OS 전체 누수 분석의 대체물이 아니다.

아래는 이전 구현 및 실행 기록이다.

기준: `spec.md` §18.1·§18.3 및 `architecture.md`. 공통 엔진과 현대 API는 `src/BrowserDock`에 두고 Hosting, CDP, WebDriver, Patching, Diagnostics를 내부 경계로 나눈다. C# 7.3용 Task API는 `src/BrowserDock.Legacy`가 제공한다. 두 프로젝트는 `net481;net8.0;net10.0`으로 빌드한다. 시험은 `BrowserDock.Tests`와 `BrowserDock.FrameworkTests`에서 공통 시험과 `Windows`/`Stress` category로 구분한다.

## 구현한 계약

- Windows Job Object에 Chrome을 **suspended 상태에서 먼저 할당한 후 실행**하여 자식 프로세스가 소유권 추적을 벗어나는 시작 race를 피한다. ChromeDriver는 별도 PID로 소유한다.
- 임시 프로필 nonce marker, 사용자 프로필 exclusive lock, 기본 프로필 및 reparse point 거부, loopback DevTools 발견을 구현한다.
- Chrome·ChromeDriver build 버전 비교, opt-in copy-on-write 패치 및 원자적 캐시 검증을 제공한다. 현재 내장 실물 recipe는 없다.
- CDP browser/page session 분리, 이벤트 registry, numeric request id 다중화, 사용 method 검사, 동일 endpoint 1회 복구와 script 재등록을 구현한다.
- 공개 `RemoteWebDriver(ICommandExecutor, ICapabilities)`로 session을 만들고 executor에서 Quit을 차단한다. 요소 ID는 공개 executor가 받은 W3C 응답에서 추출하며 Selenium의 비공개 속성에 접근하지 않는다.
- W3C session과 CDP target은 무작위 임시 JavaScript marker를 양쪽에서 확인하여 대응시킨다. 창 순서나 내부 window handle 문자열 형식에 의존하지 않으며 marker는 제거한다.
- lifecycle 직렬화, admission 차단, epoch 무효화, 세션 generation, 취소 및 제한 시간 정리를 제공한다. JSON script 결과에 raw Selenium 객체가 포함되면 거부한다.

## 수용 기준 추적

| 기준 | 제공한 시험 | 현재 실행 상태 |
|---|---|---|
| AC-01 Chrome 유지 | 30초 분리 유지·원 PID/create-time·CDP·driver port 종료 | Windows 실행 대기 |
| AC-02 session 복구 | session ID 교체·profile/endpoint 유지·storage·DOM·click | Windows 실행 대기 |
| AC-03 오래된 참조 | admission 경쟁 단위 시험, Windows lease/element 원격 호출 수 확인·DOM stale | 공통 시험과 Windows 시험을 분리 보고 |
| AC-04 누수 | warm-up 5회 뒤 100회 반복, PID/port/profile·handle/thread/private bytes 측정 | `-Stress` 실행 대기 |
| AC-05 병렬성 | 20개 인스턴스, profile lock·합성 patch cache 병렬 시험 | 공통 시험과 Windows 시험을 분리 보고 |
| AC-06 fault/cancel | 시작 단계 취소, driver/Chrome crash, CDP socket 단절 계약 | 공통 시험과 Windows 시험을 분리 보고 |
| AC-07 attach 제약 | resize 지원/미지원 및 browser 버전 기록 | Windows 실행 대기 |
| AC-08 patch | 합성 fixture의 match/hash/idempotency/변조/원본 보존 | 실제 Stable/Stable-1 recipe 검증 대기 |
| AC-09 동시 CDP/WD | 반복 lifecycle, 명시 target 대응·CDP 복구·script 재등록 | Windows 실행 대기 |
| AC-10 제품 문구 | 외부 탐지 사이트는 시험 또는 release gate에 포함하지 않음 | 문서에 반영 |

## 검증 환경과 남은 release 검증

개발 환경은 macOS arm64, .NET SDK 10.0.103이다. .NET Framework 4.8.1과 .NET 8/10을 빌드할 수 있지만 설치된 runtime은 .NET 10이므로 Framework와 .NET 8 실행 시험은 별도 환경이 필요하다. Windows 시험을 skip한 결과를 브라우저 호환성 통과로 해석하지 않는다.

2026-09-09 로컬 검증:

- .NET 8/10 빌드 성공, 경고·오류 없음.
- .NET 10 단위·HTTP/WebSocket 계약 시험 34개 통과.
- Windows 시험 22개는 OS/fixture 조건 때문에 skip. 실제 브라우저 실행 결과가 아니다.
- Release NuGet 패키지 생성 확인: `.artifacts/packages/BrowserDock.0.1.0.nupkg`. 공개 registry에는 게시하지 않았다.
- 테스트 상세 결과: `tests/BrowserDock.Tests/TestResults/common.trx` (생성 파일).

2026-09-10 Framework 4.8.1 확장 로컬 검증:

- 원본 MVP를 `main`의 `91a3685`로 기록하고 `feature/net481-support` 브랜치에서 구현했다.
- Debug 및 Release 솔루션 빌드 성공. 공통 core와 Legacy의 `net481;net8.0;net10.0`, Framework 테스트의 `net481;net10.0`, C# 7.3/x64 예제를 포함하며 경고·오류는 없다.
- .NET 10 Release 기존 시험 34개와 새 호환성 시험 21개 통과. Windows 전용 기존 22개·신규 12개는 skip했다. net481 바이너리를 실행한 결과는 아니다.
- 새 공통 시험은 timeout/cancel/예외 전파/UI context, 프로세스 종료 대기와 Windows 인수 escaping, 옵션·스크립트 입력 복사, Legacy API 경계, 실제 HTTP Selenium constructor/find/detach 및 CDP 응답 순서·socket 단절·navigation/recovery를 검증한다.
- 신규 Windows 시험은 Legacy lifecycle, stale/reacquire, cookie·storage·DOM·iframe 보존, navigation, 시작 취소, 프로필·PID·포트 정리와 100회 반복/20개 병렬 시험을 제공한다. 실제 실행은 대기 상태다.
- `BrowserDock.0.2.0.nupkg`과 `BrowserDock.Legacy.0.2.0.nupkg`을 `.artifacts/packages`에 생성했다. 세 TFM별 DLL과 의존성 그룹, 빌드용 참조 어셈블리의 런타임 의존성 제외를 확인했다. 공개 registry에는 게시하지 않았다.
- Framework 예제와 테스트 출력의 binding redirect 설정 파일 생성을 확인했다. ProjectReference가 없는 별도 SDK 형식 C# 7.3/net481 프로젝트에 두 패키지를 설치해 빌드했고 경고·오류는 없다. 두 라이브러리의 net481 DLL과 Selenium의 net462 DLL 선택을 확인했다. Windows용 기존 형식 소비 프로젝트도 마련했다.
- macOS의 일반 MSBuild는 기존 형식 프로젝트에서 NuGet compile 참조를 해석하지 못했다. 이 검증은 Windows의 Visual Studio/MSBuild를 사용하는 CI 단계로 구성했으며 아직 통과했다고 주장하지 않는다.
- 결과 파일: `tests/BrowserDock.Tests/TestResults/regression-release.trx`, `tests/BrowserDock.FrameworkTests/TestResults/framework-release.trx` (생성 파일).

Framework CI는 설치된 runtime의 Release 값(`>= 533320`)을 확인하고 `net481` 공통 시험과 C# 7.3 소비 프로젝트를 실행한다. CI 설정을 추가한 것이며 이 로컬 작업에서 원격 CI 실행 결과를 확인한 것은 아니다. 테스트 서버만 별도 .NET 10 프로세스로 실행되며 제품의 Framework runtime 의존성에는 포함되지 않는다.

Windows Stable 및 Stable-1 각각의 Chrome/CfT pair에서 `scripts/test-windows.ps1`을 실행하고 hash·version·TRX를 보관한다. GitHub의 일반 Windows runner는 Windows 11 interactive desktop 수용 시험을 대신하지 않으며 CI workflow는 공통 시험만 실행한다.

ChromeDriver detach/DELETE-session 조합별 비교와 navigation/reconnect 취소 시험도 제공한다. 실제 바이너리 패치 recipe, 실제 브라우저 fault 지점 전체, background task·file-handle 수준의 누수 분석은 Windows 검증 기록과 함께 release 전에 확인해야 한다. 합성 fixture의 패치 엔진 통과만으로 실행 바이너리의 호환성을 주장하지 않는다.

처음 `StartAsync()`가 실패하면 반환 가능한 browser 인스턴스가 없으므로 건강한 CDP-only 중간 상태라도 소유 자원을 정리하고 예외를 반환한다. 반환된 인스턴스의 reconnect 실패는 Chrome/CDP 건강성에 따라 `CdpOnly` 또는 `Faulted`가 된다.

로그는 기본적으로 민감 payload를 수집하지 않는다. 사용자 logging sink가 영구적으로 반환하지 않으면 해당 외부 코드의 강제 중단은 보장할 수 없고 제한 시간 뒤 cleanup failure를 보고한다.

공개 배포 전 프로젝트 라이선스를 확정하고 의존성 notices를 검토한다. 명세의 날짜 기반 정책에 따라 .NET 8 지원 종료 이후 신규 release의 TFM·CI matrix를 갱신한다.
