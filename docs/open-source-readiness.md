# BrowserDock 오픈소스 전환 가이드라인

가이드 작성 기준일: 2026-09-11. 아래 현황 표는 적용 전 진단이다. 후속 적용으로 영어/한국어 README, 기여·보안·행동강령, 이슈/PR 양식, Dependabot, SHA 고정 및 최소 권한 CI, 공통 alpha 버전·심볼 패키징과 배포 검증을 추가했다. 실제 적용 상태와 남은 설정은 [배포 절차](releasing.md)를 따른다. GitHub 원격은 `mete0rfish/BrowserDock`에 연결했다. 라이선스·권리자·신고 연락처는 소유자 확인 대기이며, 공개 전환이나 NuGet 게시는 수행하지 않았다. 로컬 Git 이력에 대한 제한적 secret 패턴 검사는 수행했지만 개인정보·권한·포괄적 비밀정보 감사를 완료했다는 의미는 아니다.

권장 경로는 **실험 단계 소스 공개 → Windows 검증 후 NuGet 시험판 → 지원 범위를 검증한 안정판**이다. 모든 기능이 완성될 때까지 소스 공개를 미룰 필요는 없지만, 사용자가 확인할 수 있는 기능·제약·검증 상태가 필요하다.

## 1. 프로젝트의 정체성

소개 문구 제안:

> BrowserDock is an experimental .NET library for managing a separately launched Chrome process, independent CDP connections, and replaceable WebDriver sessions on Windows. Its lifecycle design is inspired by SeleniumBase UC Mode.

강점은 다음 세 가지로 설명한다.

- Chrome을 유지한 채 WebDriver 연결을 해제하고 새 session으로 연결할 수 있다.
- 연결이 바뀌면 기존 lease·element를 무효화하고 명시적으로 재획득한다.
- 현대 .NET API와 C# 7.3/.NET Framework 4.8.1용 Legacy API를 함께 제공한다.

문서의 “SeleniumBase를 참고했다”는 설명과 실제 코드의 출처를 구분한다. 현재 `THIRD-PARTY-NOTICES.md`는 독립 구현이며 GPL C# 프로젝트의 코드를 복사·번역하지 않았다고 기록한다. 이 기록은 provenance 검토의 시작점이며 전체 이력 감사를 대신하지 않는다. 공식 SeleniumBase .NET 포트라는 표현이나 upstream 로고를 공식 제휴처럼 사용하지 않는다. 탐지 회피·CAPTCHA 통과는 지원 보증에 포함하지 않는 현재 방침을 유지한다.

## 2. 현재 상태와 우선순위

| 항목 | 확인한 현재 상태 | 필요한 조치 |
|---|---|---|
| 라이선스 | 루트 LICENSE 없음, 고지 문서에 미결정 명시 | 권리 보유자와 라이선스 확정 |
| 출처·의존성 | THIRD-PARTY-NOTICES 존재 | 직접·전이 의존성 및 test 전용 의존성, 실제 복사된 파일의 고지를 구분해 보완 |
| 설명서 | 한국어 README, 설계·검증·Framework 안내 존재 | 영어 시작 페이지, 한국어 연결, 첫 실행 경로 정리 |
| 공통 시험 | 기록상 .NET 10 core 44개·Legacy 22개, Python 판정기 5개 통과 | 공개할 커밋의 CI 결과로 다시 확인 |
| 실제 브라우저 | Windows 시험 구현, 실제 실행 미검증 | Windows 11에서 선언할 지원 조합을 실행 |
| 패키지 | 두 csproj의 버전 0.2.0, README 패키징 설정 존재 | 라이선스·저자·저장소·Source Link·심볼·공개 버전 정책 보완 |
| 기여·보안 운영 | CONTRIBUTING/SECURITY/행동강령·issue template 없음 | 기본 운영 문서와 연락 경로 추가 |
| CI | 공통 CI와 수동 self-hosted Windows workflow 존재 | 공개 PR 경계, 최소 권한, release workflow 및 실제 runner 준비 |
| 변경 기록 | 최근 구현 변경이 아직 커밋되지 않은 상태 | 리뷰 가능한 커밋으로 정리하고 공개 기준 SHA 확정 |

## 3. 소스 공개 전에 할 일

### 3.1 라이선스 결정

넓은 사용과 간단한 기여 절차가 목적이라면 **MIT를 우선 제안**한다. 저작권·라이선스 고지를 유지하는 조건으로 상업적 사용과 수정·재배포를 허용하는 간결한 라이선스다. [MIT 라이선스 안내](https://choosealicense.com/licenses/mit/)

기여자의 특허 허여와 관련 조건을 명시하는 것이 중요하면 **Apache-2.0**을 비교한다. 해당 특허 허여는 라이선스에 정해진 기여자의 특허 범위에 적용되며 모든 특허 문제를 해결한다는 뜻은 아니다. [Apache-2.0 원문 §3~4](https://www.apache.org/licenses/LICENSE-2.0)

확정 절차:

1. 개인·회사·공동 기여자 중 누가 공개 권한을 보유하는지 확인한다. 회사 업무나 다른 사람의 코드가 포함됐다면 그 부분의 공개 권한부터 확인한다.
2. 선택한 표준 라이선스를 루트 `LICENSE`로 추가하고 저작권 보유자 표기를 확정한다. 제한 문구를 임의로 덧붙여 다른 라이선스로 만들지 않는다.
3. 두 NuGet 패키지의 `PackageLicenseExpression`과 README·고지 문서의 미결정 문구를 일치시킨다.
4. 배포 파일에 들어가는 타사 소스·바이너리·고지를 실제 의존성 버전과 대조한다. 참조 링크만 있는 것과 소스를 복사한 것은 다르게 기록한다.

저장소를 Public으로 설정하는 것만으로 일반적인 오픈소스 사용·수정·재배포 권한이 생기지는 않는다. [GitHub 저장소 라이선스 안내](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)

SeleniumBase reference runner는 개발·비교용 Python 의존성으로 구분한다. 런타임 NuGet 의존성에 포함하지 않는다. Chrome/ChromeDriver 바이너리를 소스나 NuGet에 추가하지 않는 현재 구조를 유지한다. 배포 대상으로 결정한 파일별 라이선스 조건은 별도로 충족해야 한다.

### 3.2 공개할 내용 점검

현재 파일뿐 아니라 공개되는 브랜치·태그·Git 이력을 대상으로 토큰, 인증서, 비밀번호, 내부 URL, 고객 데이터, Chrome profile·cookie, 테스트 로그와 다운로드 파일을 검사한다. 현재 문서 작성은 이 검사를 수행한 결과가 아니다.

노출된 인증정보를 발견하면 먼저 폐기·교체하고 이력 정리 방식을 결정한다. `.gitignore` 추가는 과거 커밋을 제거하지 않는다. 이력 수정은 협업자의 checkout에 영향을 주므로 대상과 절차를 정한 별도 작업으로 수행한다. [GitHub의 노출된 secret 처리 안내](https://docs.github.com/en/actions/reference/security/secure-use)

소스 공개용 기준 커밋에 코드·시험·문서를 포함한다. 생성한 TRX 전체를 저장소에 넣기보다는 검증 요약과 CI artifact 링크를 제공하고, 공개 artifact에 개인 경로·page 내용 등이 들어가는지 확인한다.

### 3.3 최소 문서 세트

| 파일 | 담아야 할 내용 |
|---|---|
| `README.md` | 영어 개요, Experimental 표시, OS·TFM·브라우저 조건, 가장 짧은 실행 예제, 제한, 검증 상태, 문서 링크 |
| `README.ko.md` | 한국어 사용자 안내; 영어 문서와 같은 지원 범위 |
| `LICENSE` | 선택한 표준 라이선스와 권리자 정보 |
| `THIRD-PARTY-NOTICES.md` | 실제 포함·배포·참조 관계 및 필요한 타사 고지 |
| `CONTRIBUTING.md` | 개발환경, 공통 시험 명령, Windows 시험 조건, PR 범위, 리뷰·기여 라이선스 원칙 |
| `SECURITY.md` | 지원 버전, 비공개 취약점 제보 경로, 제공할 최소 정보 |
| `CODE_OF_CONDUCT.md` | 참여 행동 기준과 실제 관리할 수 있는 신고 경로 |
| `CHANGELOG.md` | 사용자 관점 변경·호환성·알려진 문제 |
| `.github/ISSUE_TEMPLATE/*` | 버그·기능 제안 폼; OS/Chrome/driver/TFM/패치 모드/재현 단계 수집 |
| `.github/pull_request_template.md` | 변경 목적·수행한 검증·문서/API 영향 |

처음부터 별도 문서 사이트나 복잡한 CLA 시스템을 만들 필요는 없다. 기여자는 공개할 권한이 있는 코드를 제출하고 프로젝트와 같은 라이선스 조건으로 기여한다는 원칙을 먼저 정한다. 대규모 기능은 구현 전 issue로 논의하도록 안내한다. 실제 대응 인력 없이 지원 SLA를 약속하지 않는다.

## 4. 공개 저장소 운영

기본 브랜치에 필수 CI와 force-push 제한을 설정하고, 유지관리자가 둘 이상이면 리뷰 승인도 요구한다. 1인 운영일 때는 자기 PR을 승인할 수 없는 규칙으로 release가 막히지 않도록 설정한다. Rulesets·권한의 실제 적용 여부는 GitHub에서 확인한다.

일반 PR의 공통 시험은 hosted runner에서 실행한다. Windows 실제 시험은 현재 수동 workflow를 출발점으로 사용한다. 공개 저장소에 연결하는 self-hosted runner는 임의 코드 실행 지점이므로 개인 개발 PC·회사 인증정보·NuGet 배포 권한과 분리된 재생성 가능한 시험 환경을 권장한다. 수동 실행도 선택된 branch/ref의 코드를 실행하므로 관리자가 검토한 정확한 커밋을 사용한다. [GitHub self-hosted runner 안내](https://docs.github.com/en/actions/concepts/runners/self-hosted-runners)

추가 설정은 `contents: read` 기본 권한, 필요한 job만 권한 확대, Actions의 검증한 전체 SHA 고정, Dependabot의 NuGet·pip·Actions 업데이트, secret scanning/push protection·취약점 제보 경로다. 특히 `pull_request_target`에서 외부 PR 코드를 checkout하고 비밀정보나 쓰기 권한을 주는 구성을 피한다. [GitHub Actions 보안 지침](https://docs.github.com/en/actions/reference/security/secure-use)

버그 폼은 기본적으로 로컬 fixture 재현을 요청한다. 공개 issue에 cookie·로그인 정보·사용자 profile을 첨부하도록 요구하지 않는다. 재현이 없는 외부 사이트 탐지 사례는 일반 lifecycle 결함과 분리해 관리한다.

## 5. NuGet 시험판 배포 전에 할 일

### 5.1 지원 범위 확정

첫 소스 공개에는 Windows 미검증 상태를 표시할 수 있다. 첫 NuGet 시험판은 **지원한다고 명시할 각 runtime에서 실제 브라우저의 시작 → 이동 → disconnect → reconnect → 종료**를 검증한 뒤 배포하는 것을 권장한다. net481을 지원한다고 안내한다면 net10에서 같은 코드가 통과한 결과로 대체하지 않는다.

최소 확인 항목은 Chrome PID·profile 유지, 새 session, 오래된 참조 차단, 실제 DOM 동작, driver/Chrome/port/profile 정리다. 넓은 Stable/Stable-1 호환성이나 누수 안정성을 선언하려면 [테스트 계획](test-plan.md)의 해당 matrix와 stress 결과가 필요하다. recipe가 없는 실제 바이너리 패치는 미지원·미검증으로 명시한다.

장기 중심은 .NET 10, 기존 프로젝트 사용자는 Framework 4.8.1 facade로 구분하는 방향을 제안한다. .NET 8은 Microsoft 지원 종료일이 **2026-11-10**이므로 공개 시점에 지원 종료 후의 유지 정책을 정한다. 기존 TFM을 제거하는 작업은 사용자 영향과 버전 정책을 함께 검토한다. [Microsoft .NET 지원 정책](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

### 5.2 패키지 완성도

현재 `BrowserDock`, `BrowserDock.Legacy` 두 패키지를 유지하되 사용자가 언제 어느 패키지를 설치해야 하는지 README 첫 부분에서 안내한다. 패키지 이름의 사용 가능 여부와 소유권은 NuGet에서 별도 확인한다.

추가·확인할 메타데이터는 `PackageId`, `Authors`, `PackageLicenseExpression`, `PackageProjectUrl`, `RepositoryUrl`, `RepositoryType`, 설명·태그, README, Source Link와 심볼 패키지다. SDK에 내장된 Source Link 지원을 확인하고 실제 PDB에서 공개 commit의 소스로 연결되는지 검사한다. NuGet README 안의 로컬 상대 문서 링크는 배포 페이지에서 열릴 수 있는 URL로 정리한다. [Microsoft NuGet 라이브러리 가이드](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/nuget)

release 후보에서 두 패키지를 pack한 다음 저장소 ProjectReference가 없는 소비 프로젝트에 설치하여 다음을 확인한다.

- core/Legacy 버전과 상호 의존성이 의도대로 맞는가?
- net481/net8/net10에 올바른 DLL과 의존성이 선택되는가?
- Framework 앱의 binding redirect와 C# 7.3 예제가 실제로 실행되는가?
- 라이선스·README·고지·심볼이 들어가고, fixture·profile·소스 임시파일·브라우저 바이너리가 섞이지 않는가?

### 5.3 버전과 배포 흐름

현재 미공개 0.2.0을 기준으로 첫 공개 버전을 **`0.2.0-alpha.1`**처럼 시작하는 방안을 제안한다. 이미 게시된 버전과 이름이 있다면 그 이력에 맞춰 조정한다. 두 패키지는 같은 release에서 같은 버전으로 관리한다. 0.x에서도 API·예외·동작의 호환성 변경을 CHANGELOG에 기록하고 1.0의 안정 계약을 명시한다.

배포 흐름은 기준 commit 확정 → 공통·Windows 검증 → pack → 패키지 소비 시험 → release artifact 고정 → 유지관리자 release 승인 → NuGet 게시로 둔다. 승인 후에는 검증한 동일 artifact를 게시한다. 평소 PR 시험 job에는 게시 권한을 주지 않는다.

NuGet 게시 인증은 GitHub Actions와 연결하는 **Trusted Publishing/OIDC**를 우선 검토한다. 저장소·workflow·필요 시 environment에 정책을 묶고 짧은 수명의 게시 자격증명을 사용한다. 초기 패키지 소유권과 정책 설정은 실제 NuGet 계정에서 확인한다. [NuGet Trusted Publishing 공식 문서](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)

## 6. 단계별 완료 기준

| 단계 | 완료 기준 | 공개할 결과 |
|---|---|---|
| A. 소스 공개 | 권한·라이선스·고지 확정, 공개 이력 검사, 최소 README/기여/보안 안내, 기준 commit | Experimental 저장소와 빌드·기여 안내 |
| B. 시험판 | 선언한 Windows/runtime 조합의 핵심 lifecycle·cleanup 검증, 패키지 소비 시험, 게시 흐름 확인 | NuGet alpha, 실제 지원 표와 알려진 제한 |
| C. 안정판 | 전체 선언 matrix·stress·실패 복구 확인, API/예외/지원 정책 확정 | 1.0 release와 변경·마이그레이션 안내 |
| D. 유지관리 | 의존성·Chrome 변경 감지, 이슈 분류, 검증 후 주기적 release | 지속적으로 갱신되는 지원·검증 기록 |

## 7. 바로 시작할 작업 목록

1. **라이선스와 권리자 확정:** MIT 우선 검토, 특허 허여 조건이 중요하면 Apache-2.0 비교.
2. **공개 준비 변경 묶음 작성:** LICENSE, 영어 README/한국어 안내, CONTRIBUTING, SECURITY, issue/PR template, CHANGELOG.
3. **기존 변경 정리·이력 검사:** 최근 테스트 구현을 커밋하고 공개 기준 SHA를 정한다.
4. **GitHub 운영 설정:** 필수 CI, 최소 권한, 의존성 갱신과 취약점 제보 경로를 준비한다.
5. **Experimental 소스 공개:** NuGet 미게시·Windows 검증 상태를 정확히 표시한다.
6. **Windows 실측과 패키징 작업:** 선언할 환경을 검증하고 두 패키지의 메타데이터·소비 시험을 완료한다.
7. **NuGet alpha 배포:** 검증 기록과 알려진 문제를 첨부하고 초기 사용자 재현 사례를 받는다.

현재 우선순위는 새 기능 확대보다 **라이선스·첫 사용 경험·Windows 검증·배포 재현성**이다.
