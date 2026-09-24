# BrowserDock 오픈소스 전환 가이드라인

최초 작성일: 2026-09-11. 현황 갱신일: 2026-09-24. 아래 표는 저장소 파일과 기존 실행 기록을 기준으로 갱신했다. 후속 적용으로 영어/한국어 README, 기여·보안·행동강령, 이슈/PR 양식, Dependabot, SHA 고정 및 최소 권한 CI, 공통 alpha 버전·심볼 패키징과 배포 검증을 추가했다. 실제 적용 상태와 남은 설정은 [배포 절차](releasing.md)를 따른다. GitHub 원격은 `mete0rfish/BrowserDock`에 연결했다. 2026-09-24 소유자 확인에 따라 MIT 라이선스, 저작권자·작성자 mete0rfish, 비공개 보안·행동강령 신고 이메일 sungwonyoon326@gmail.com을 반영했다. 공개 전환이나 NuGet 게시는 수행하지 않았다. 로컬 Git 이력에 대한 제한적 secret 패턴 검사는 수행했지만 개인정보·권한·포괄적 비밀정보 감사를 완료했다는 의미는 아니다.

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

2026-09-24 [공개 전 저장소 점검 및 조치](publication-audit.md)로 main 보호·필수 CI·main 한정 환경·Actions SHA 고정·이메일 이력 교체를 적용했다. 환경 승인과 secret scanning은 계정·저장소 제약으로 적용하지 못했고 전용 Windows runner도 남아 있다. PUBLIC_RELEASE_ENABLED=false를 유지한다.

| 항목 | 확인한 현재 상태 | 남은 조치 |
|---|---|---|
| 라이선스 | MIT LICENSE, 저작권자·작성자 mete0rfish, 패키지 메타데이터 반영 | 배포물에 동일 LICENSE 포함 확인 |
| 출처·의존성 | THIRD-PARTY-NOTICES의 직접 의존성 버전을 csproj·requirements.txt와 대조해 정리 | 전이 의존성 및 실제 배포 파일의 라이선스·고지 검토 |
| 설명서 | 영어·한국어 README, 설계·검증·Framework 안내 존재; Windows 일반 258건 통과 기록 반영 | 릴리스 후보 커밋의 검증 결과로 갱신 |
| 공통 시험 | 2026-09-24 기록상 Core net8/net10 각 43건, Legacy net481/net8/net10 각 22건, 총 152건 통과 | 공개할 커밋의 CI 결과로 다시 확인 |
| 실제 브라우저 | Windows 11 x64, Chrome/Driver 154.0.8037.57에서 일반 258건 통과, 실패·건너뜀 0 | 동일 후보 커밋의 Stress; Stable-1·Python 비교·권한 필요 시험·실제 recipe의 미검증 범위 명시 |
| 패키지 | 공통 버전 0.2.0-alpha.1, 저자·MIT·저장소·README·심볼 설정 존재 | 실제 패키지 검사, 소비자 검증, 공개 커밋 Source Link 확인 |
| 기여·보안 운영 | CONTRIBUTING·SECURITY·행동강령·템플릿 존재; 비공개 신고 이메일 확정 | GitHub 비공개 취약점 신고 활성화 여부는 미확인 |
| CI | 공통·Windows·release workflow, SHA 고정·최소 권한·Dependabot 설정 존재 | 필수 검사·브랜치 보호 적용 완료; environment 승인 지원 조건과 runner 격리 준비 필요 |
| 공개 기준 | 로컬 변경과 기존 검증 기록 존재 | 공개 이력 검토, 변경 커밋 정리, 릴리스 기준 SHA 확정 |

시험 수치는 [구현 및 검증 기록](implementation.md)의 기존 실행 결과다. 이번 문서 갱신에서 새로 실행한 시험이 아니며, 전체 릴리스 승인을 뜻하지 않는다.

## 3. 소스 공개 전에 할 일

### 3.1 확정된 라이선스와 남은 출처 검토

소유자가 MIT 라이선스와 저작권자·작성자 `mete0rfish`를 확정했다. 표준 원문은 [LICENSE](../LICENSE), 패키지 설정은 `build/Package.props`에 있으며 두 README와 기여 안내에도 반영했다.

남은 검토는 실제 공개·배포할 파일의 출처와 권한, 직접·전이 의존성의 고지다. 타사 소스·바이너리·고지를 배포 버전과 대조하고, 참조 링크와 실제 복사한 코드를 구분해 기록한다.

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

현재 Windows 일반 시험 기록과 미검증 범위를 구분해 표시한다. 첫 NuGet 시험판은 **지원한다고 명시할 각 runtime에서 실제 브라우저의 시작 → 이동 → disconnect → reconnect → 종료**를 검증한 뒤 배포하는 것을 권장한다. net481을 지원한다고 안내한다면 net10에서 같은 코드가 통과한 결과로 대체하지 않는다.

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

현재 `build/Version.props`에 설정된 첫 공개 후보 버전은 **`0.2.0-alpha.1`**이다. 이미 게시된 버전과 이름이 있다면 그 이력에 맞춰 조정한다. 두 패키지는 같은 release에서 같은 버전으로 관리한다. 0.x에서도 API·예외·동작의 호환성 변경을 CHANGELOG에 기록하고 1.0의 안정 계약을 명시한다.

배포 흐름은 기준 commit 확정 → 공통·Windows 검증 → pack → 패키지 소비 시험 → release artifact 고정 → 유지관리자 release 승인 → NuGet 게시로 둔다. 승인 후에는 검증한 동일 artifact를 게시한다. 평소 PR 시험 job에는 게시 권한을 주지 않는다.

NuGet 게시 인증은 GitHub Actions와 연결하는 **Trusted Publishing/OIDC**를 우선 검토한다. 저장소·workflow·필요 시 environment에 정책을 묶고 짧은 수명의 게시 자격증명을 사용한다. 초기 패키지 소유권과 정책 설정은 실제 NuGet 계정에서 확인한다. [NuGet Trusted Publishing 공식 문서](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)

## 6. 단계별 완료 기준

| 단계 | 완료 기준 | 공개할 결과 |
|---|---|---|
| A. 소스 공개 | 권한·라이선스·고지 확정, 공개 이력 검사, 최소 README/기여/보안 안내, 기준 commit | Experimental 저장소와 빌드·기여 안내 |
| B. 시험판 | 선언한 Windows/runtime 조합의 핵심 lifecycle·cleanup 검증, 패키지 소비 시험, 게시 흐름 확인 | NuGet alpha, 실제 지원 표와 알려진 제한 |
| C. 안정판 | 전체 선언 matrix·stress·실패 복구 확인, API/예외/지원 정책 확정 | 1.0 release와 변경·마이그레이션 안내 |
| D. 유지관리 | 의존성·Chrome 변경 감지, 이슈 분류, 검증 후 주기적 release | 지속적으로 갱신되는 지원·검증 기록 |

## 7. 남은 작업 순서

라이선스·작성자·신고 연락처 반영과 문서 불일치 정리는 완료했다. 이후 순서는 다음과 같다.

1. **공개 이력과 출처 검토:** 전체 공개 대상 Git 이력의 비밀정보·개인정보·재배포 권한과 의존성 고지를 확인한다.
2. **GitHub 운영 설정 확인:** 필수 CI, 브랜치 보호, environment 승인, self-hosted runner 격리를 확인한다.
3. **공개·릴리스 기준 커밋 확정:** 변경을 정리하고 해당 SHA에서 공통 CI와 Windows Stress 검증을 수행한다. 추가 미검증 범위를 명시한다.
4. **패키지 검증:** 두 패키지와 심볼의 release 검사, 별도 소비자 실행, Source Link를 확인한다.
5. **게시 준비 및 실행:** NuGet ID 소유권·Trusted Publishing을 확인하고, 검증한 아티팩트를 게시한 뒤 깨끗한 환경에서 설치를 확인한다. 소스 공개와 패키지 게시는 각각의 완료 기준에 따라 진행한다.
