# 구현 및 검증 기록

기준: `spec.md` §18.1 및 `architecture.md`. 공개 API와 구현은 `src/UcDotNet`의 한 assembly 안에 두고 Hosting, CDP, WebDriver, Patching, Diagnostics를 내부 경계로 나눈다. 시험은 `tests/UcDotNet.Tests`에서 공통 시험과 `Windows`/`Stress` category로 구분한다.

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

개발 환경은 macOS arm64, .NET SDK 10.0.103이다. .NET 8/10을 빌드할 수 있지만 설치된 runtime은 .NET 10이므로 .NET 8 실행 시험은 별도 환경이 필요하다. Windows 시험을 skip한 결과를 브라우저 호환성 통과로 해석하지 않는다.

2026-09-09 로컬 검증:

- .NET 8/10 빌드 성공, 경고·오류 없음.
- .NET 10 단위·HTTP/WebSocket 계약 시험 34개 통과.
- Windows 시험 22개는 OS/fixture 조건 때문에 skip. 실제 브라우저 실행 결과가 아니다.
- Release NuGet 패키지 생성 확인: `.artifacts/packages/UcDotNet.0.1.0.nupkg`. 공개 registry에는 게시하지 않았다.
- 테스트 상세 결과: `tests/UcDotNet.Tests/TestResults/common.trx` (생성 파일).

Windows Stable 및 Stable-1 각각의 Chrome/CfT pair에서 `scripts/test-windows.ps1`을 실행하고 hash·version·TRX를 보관한다. GitHub의 일반 Windows runner는 Windows 11 interactive desktop 수용 시험을 대신하지 않으며 CI workflow는 공통 시험만 실행한다.

ChromeDriver detach/DELETE-session 조합별 비교와 navigation/reconnect 취소 시험도 제공한다. 실제 바이너리 패치 recipe, 실제 브라우저 fault 지점 전체, background task·file-handle 수준의 누수 분석은 Windows 검증 기록과 함께 release 전에 확인해야 한다. 합성 fixture의 패치 엔진 통과만으로 실행 바이너리의 호환성을 주장하지 않는다.

처음 `StartAsync()`가 실패하면 반환 가능한 browser 인스턴스가 없으므로 건강한 CDP-only 중간 상태라도 소유 자원을 정리하고 예외를 반환한다. 반환된 인스턴스의 reconnect 실패는 Chrome/CDP 건강성에 따라 `CdpOnly` 또는 `Faulted`가 된다.

로그는 기본적으로 민감 payload를 수집하지 않는다. 사용자 logging sink가 영구적으로 반환하지 않으면 해당 외부 코드의 강제 중단은 보장할 수 없고 제한 시간 뒤 cleanup failure를 보고한다.

공개 배포 전 프로젝트 라이선스를 확정하고 의존성 notices를 검토한다. 명세의 날짜 기반 정책에 따라 .NET 8 지원 종료 이후 신규 release의 TFM·CI matrix를 갱신한다.
