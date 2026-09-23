<!-- browserdock-ko-wiki:v1 -->
# 11. 테스트와 CI 이해하기

> 핵심 질문: 테스트가 통과했다는 결과는 어떤 계약과 환경을 확인한 것인가요?

## 네 단계의 근거를 구분합니다

테스트 코드가 존재하는 것, 컴파일되는 것, 실제로 실행되는 것, 목표 환경에서 기대 조건을 만족하는 것은 서로 다릅니다. Wiki나 이슈에 결과를 쓸 때 이 단계를 생략하지 않습니다.

예를 들어 가짜 WebSocket 서버가 역순 응답을 보내는 테스트는 요청 대응 로직을 검증합니다. 그 결과만으로 특정 Chrome 버전에서 실제 페이지의 navigation까지 검증했다고 말할 수는 없습니다.

## 테스트 구역

| 구역 | 확인하려는 것 |
|---|---|
| `BrowserDock.Tests` 공통 시험 | 상태·프로토콜·참조·진단 등 브라우저 없이 확인 가능한 계약 |
| Windows category | 실제 Chrome/ChromeDriver와 함께 확인할 계약 |
| Stress category | 반복 연결 교체와 병렬 인스턴스의 자원·경합 |
| `BrowserDock.FrameworkTests` | Legacy API와 대상 런타임의 호환성 |
| 패키지 소비 시험 | 소스 프로젝트 참조가 아닌 패키지 사용 경로 |
| `seleniumbase-reference` | 공통 fixture의 정해진 시나리오 비교와 판정 |
| `tests/tooling` | 배포·결과 검증 도구의 동작 |

단순히 net481 DLL을 빌드했다는 사실로 Windows Framework 런타임 실행을 통과했다고 기록하지 않습니다.

## 공통 테스트 실행

저장소 최상위에서 복원과 빌드를 마친 뒤 실행합니다. `net8.0`으로 바꿀 때는 해당 런타임도 설치되어 있어야 합니다.

```sh
dotnet test tests/BrowserDock.Tests -f net10.0 --no-restore --filter "TestCategory!=Windows" --logger trx
dotnet test tests/BrowserDock.FrameworkTests -f net10.0 --no-restore --filter "TestCategory!=Windows" --logger trx
```

하나의 계약을 따라가려면 다음처럼 범위를 좁힐 수 있습니다.

```sh
dotnet test tests/BrowserDock.Tests -f net10.0 --filter "FullyQualifiedName~CdpMultiplexesOutOfOrderResponsesAndIncludesPageSession"
```

필터가 잘못되어 0건이 실행된 결과를 성공 증거로 사용하지 마세요. 실제 테스트 이름과 실행 건수를 확인합니다.

## Windows 실제 브라우저 시험

Windows 11 x64의 실제 실행 환경에서 호환 바이너리 경로를 지정합니다.

```powershell
./scripts/test-windows.ps1 -Chrome C:\Chrome\chrome.exe -Driver C:\Chrome\chromedriver.exe
./scripts/test-windows.ps1 -Chrome C:\Chrome\chrome.exe -Driver C:\Chrome\chromedriver.exe -Stress
```

Stress 범위에는 100회 lifecycle과 20개 병렬 인스턴스를 포함하는 시나리오가 있습니다. 이 수치는 테스트의 규모이지, 문서 작업에서 해당 실행을 완료했다는 결과가 아닙니다.

실제 시험에서는 브라우저 유지, 새 세션 생성, 오래된 참조 거부뿐 아니라 종료 후 소유 PID·포트·프로필·작업 정리도 확인해야 합니다. 내부 계측은 OS 전체 누수 분석의 대체물이 아닙니다.

## 두 종류의 CI를 구분합니다

`ci.yml`의 Common contracts는 Ubuntu와 Windows hosted runner에서 net8/net10 공통 테스트를 실행하도록 정의되어 있습니다. 별도 Framework job은 Windows에서 net481 runtime과 패키지 소비 경로를 확인합니다.

`windows-browser.yml`의 실제 브라우저 수용 시험은 수동 실행이며, `self-hosted`, `Windows`, `X64`, `windows-11-interactive` 라벨과 `windows-browser` environment를 사용합니다. 일반적인 Windows hosted runner 공통 테스트가 이 수용 시험을 대신하지 않습니다.

runner 라벨만 같다고 환경이 실제로 맞는 것도 아닙니다. 대화형 사용자 세션, OS·아키텍처, 바이너리 경로와 보호 설정을 확인해야 합니다.

## SeleniumBase 비교의 의미

동일한 fixture에서 사전에 정한 네 가지 시나리오를 실행하고, 각 구현의 관찰 결과를 기대값과 비교하는 시험입니다. 한쪽 결과와 비슷하다는 이유만으로 성공시키지 않고, 필요한 session 교체나 cleanup 조건의 누락을 구분합니다.

이 비교는 모든 기능의 동등성이나 외부 탐지 사이트 통과를 증명하는 시험이 아닙니다. 범위와 runner 사용법은 기존 테스트 계획과 비교 runner README를 따릅니다.

## 실행 결과를 남기는 양식

```text
소스 커밋:
OS / 프로세스 아키텍처:
SDK / runtime:
Chrome / ChromeDriver 버전:
명령 또는 workflow run:
전체 / 통과 / 실패 / skip:
cleanup 결과:
수행하지 않은 범위:
증거 파일 또는 로그:
```

2026-09-12 구현 기록에는 macOS에서 core 44개, Legacy 22개 공통 시험 통과가 남아 있습니다. 이는 해당 날짜·환경의 기록이며 이후 커밋의 현재 결과로 치환하지 않습니다. 현재 커밋의 결과는 연결된 workflow와 산출물로 따로 확인해야 합니다.

**근거:** [테스트 계획](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/docs/test-plan.md), [공통 CI](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/.github/workflows/ci.yml), [Windows 수용 시험](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/.github/workflows/windows-browser.yml), [과거 실행 기록](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/docs/implementation.md)

[← 자원 안전성](ko-Resource-Safety.md) · [목차](ko-Home.md) · [다음: 디버깅과 변경 →](ko-Debugging.md)
