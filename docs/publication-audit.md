# 공개 전 저장소 점검 — 2026-09-24

## 조치 후 상태

main 보호와 필수 CI 5개, PR 경유, 강제 push·삭제 금지, 관리자 보호 적용을 완료했다.
Actions 전체 SHA 고정 정책과 read-only 기본 권한, Dependabot 취약점 알림을 적용했다.
windows-browser와 release 환경은 main 브랜치만 허용한다.

현재 비공개 저장소의 요금제는 필수 환경 승인자를 지원하지 않아 API가 422를 반환했다.
Secret scanning도 이 저장소에서 사용할 수 없다는 422 응답을 받았다.
비공개 취약점 신고 활성화는 404로 실패했으며 기존 이메일 신고 경로를 유지한다.
PUBLIC_RELEASE_ENABLED=false를 명시하여 패키지 게시를 비활성화했다.
Windows x64 실행기 준비와 ARM 환경 구성은 사용자 요청으로 보류했다. 비공개 저장소와 게시 차단을 유지하기로 확정했다.

사용자 요청에 따라 원격 10개 브랜치와 로컬 refs의 naver.com 작성자·커미터 이메일을
GitHub 비공개 주소로 교체했다. 모든 변경 ref의 끝에서 파일 tree가 동일함을 확인했다.
원격은 atomic push와 각 ref의 기존 SHA를 명시한 force-with-lease로 갱신했고,
일시 조정한 main 보호는 복원한 뒤 protected=true를 다시 확인했다.
로컬 user.email도 비공개 주소로 지정했다. 현재 main은
`09ba18d566c26c784f252898fdf42fa694b7c4ad`이다.
이력 변경으로 기존 커밋 SHA·서명이 바뀌었으며 기존 체크아웃은 새 이력과 동기화해야 한다.
작업 중인 파일과 보류 중인 ARM 변경은 보존했다.

원본 이력은 `.artifacts/publication-audit/before-email-rewrite.bundle`에 보관했다.
이메일 교체 계획·결과는 같은 디렉터리의 `email-rewrite-plan.json`,
`email-rewrite-applied.json`에 있다. 이 백업에는 이전 이메일이 남으므로 공개하지 않는다.
GitHub PR 참조·캐시, 기존 clone, 별도 wiki까지 삭제된 것은 아니다.
[GitHub 이력 제거 안내](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository)에 따라
잔존 참조 정리 여부는 별도 확인해야 하며, 지원팀의 제거 가능 여부도 보장되지 않는다.

새 main의 [공통 CI](https://github.com/mete0rfish/BrowserDock/actions/runs/35965425139)는
필수 작업 5개 모두 성공했다. 아래 표와 스캔 통계는 조치 전 점검 기록으로 보존한다.

## 검사 범위와 방법

- `git fetch origin --tags` 후 로컬의 전체 refs 30개, 접근 가능한 커밋 36개,
  고유 파일 blob 253개와 현재 작업 파일 92개를 검사했다.
- 기준 HEAD/main: `0c71d756b0f5265a8ad1e922ea80ab429a3b19f0`.
  로컬에는 라이선스·문서 수정과 보류 중인 ARM 실험 변경이 있다.
- 개인키, GitHub/Slack/Google/AWS 키, URL 내 인증정보, 비밀번호·토큰 할당,
  이메일·개인 절대 경로 패턴을 검사했다. 파일 이름과 바이너리 여부도 확인했다.
- 원시 값 없이 위치만 기록한 결과는 로컬 `.artifacts/publication-audit/scan.json`,
  작성자 정보는 `.artifacts/publication-audit/commit-identities.txt`에 있다.
  이 파일들은 `.gitignore`의 `.artifacts/` 제외 대상이다.
- 원격에서 가져온 브랜치·태그와 로컬 refs의 도달 가능한 객체가 범위다.
  삭제된 원격 refs, 별도 wiki 저장소, LFS 원격 객체, GitHub artifact·로그·첨부물,
  접근 불가능한 객체 및 서버 측 데이터는 이 검사에 포함되지 않는다.

## 파일·이력 결과

| 항목 | 결과 | 남은 조치 |
|---|---|---|
| 알려진 비밀정보 패턴 | 해당 패턴 일치 없음 | 공개 기준 커밋에서 재검사; 패턴 검사는 비밀정보 부재를 증명하지 않음 |
| 바이너리·민감 파일 이름 | 검사한 blob에 NUL 포함 바이너리 없음; exe/dll/archive/key/profile/cookie/TRX 후보 없음 | 실제 배포 패키지와 CI 산출물은 별도 검사 |
| 이메일 | 현재 문서에는 사용자가 승인한 신고 이메일 존재 | Git 작성자/커미터 정보의 별도 naver.com 주소 공개 여부 확인 |
| 개인 경로 | 미추적 ARM 문서에 개인 SSD 경로 3곳 확인 | `/Volumes/ExternalSSD/` 예시로 수정 완료; ARM 구현 작업은 보류 유지 |
| 출처·재배포 | 소스·스크립트의 URL/저작권/복사 표시와 고지·참조 runner의 고정 버전 확인 | 독립 구현 고지와 참조 정보만으로 전체 복제 여부·권리 관계를 증명할 수 없음; 배포 파일·전이 의존성 고지 검토 필요 |

Git 작성자·커미터 이메일에는 GitHub 주소와 별도 naver.com 주소가 있다.
전체 원문은 위 로컬 파일에서 확인할 수 있다. 승인된 신고 이메일과 다르다는
이유만으로 삭제하거나 이력을 바꾸지 않았다. 공개를 원하지 않는다면 대상 refs와
협업 영향을 정한 후 별도 이력 정리가 필요하다.

## 조치 전 GitHub 운영 설정 확인

API로 확인한 저장소: `mete0rfish/BrowserDock`.

| 항목 | 관찰 결과 | 필요한 조치 |
|---|---|---|
| 공개 상태 | PRIVATE, 기본 브랜치 main | 소스 공개 조건 충족 후 공개 여부 결정 |
| 원격 라이선스 | licenseInfo 없음 | 로컬 MIT·문서 변경을 검토 후 반영 |
| main 보호 | protected=false | 강제 push·삭제 제한 및 필수 CI 적용 |
| Rulesets | 없음 | 브랜치 보호 또는 활성 ruleset 구성 |
| Environments | 없음 | windows-browser와 release 생성, 실행 ref 제한·승인 설정 |
| 자체 호스팅 runner | 등록 0개 | Windows 11 x64 대화형 격리 환경 준비; 현재 격리 상태 검증 불가 |
| Actions 기본 권한 | read, PR 승인 권한 false | 유지 |
| Actions 허용 정책 | all, sha_pinning_required=false | 현재 YAML은 SHA 고정; 저장소 차원 강제 정책 검토 |
| 보안 기능 | security_and_analysis=null | secret scanning·push protection의 이용 가능 여부와 활성화 상태 별도 확인; null을 비활성화로 단정하지 않음 |
| 비공개 취약점 신고 | 이전 API 조회 404 | 활성화 확인 불가; SECURITY의 이메일 경로 사용 |
| 저장소 Actions 변수 | 없음 | PUBLIC_RELEASE_ENABLED가 없어 현재 publish 조건 불충족; 준비 완료 전 활성화하지 않음 |

최신 main의 [Common contracts 실행](https://github.com/mete0rfish/BrowserDock/actions/runs/35948349911)은
기준 HEAD에서 성공했다. 필수 검사로 지정할 실제 작업 이름은 다음과 같다.

- `common (ubuntu-latest, net8.0)`
- `common (ubuntu-latest, net10.0)`
- `common (windows-latest, net8.0)`
- `common (windows-latest, net10.0)`
- `framework481`

현재 로컬 변경은 이 CI의 검증 대상이 아니다.

## 조치 전 workflow 검토

- 일반 PR은 hosted runner의 공통 시험만 실행한다. `pull_request_target`은 없다.
- Windows browser workflow는 수동 실행이며 self-hosted Windows X64 전용 label과
  windows-browser environment를 요구한다. 실제 environment와 runner는 아직 없다.
- 세 workflow의 Actions 참조는 전체 SHA 고정이며 checkout 자격증명 저장은 꺼져 있다.
- publish job에만 OIDC 쓰기 권한을 주고 release environment를 지정한다.
  게시에는 명시적 입력과 PUBLIC_RELEASE_ENABLED=true가 필요하다.
- 게시 검사는 같은 커밋의 stress 실행, clean checkout, 필수 TRX 결과와 패키지
  메타데이터를 확인한다. 이 정적 검토는 실제 release 실행 성공을 뜻하지 않는다.
- 브라우저 실행기는 개인 계정·프로필·게시 자격증명이 없는 전용 환경으로 준비하고,
  신뢰하지 않는 작업 실행 뒤 재생성할 수 있어야 한다. 실행기가 없어 현재 확인 불가하다.

## 후속 검사 결과

- 새 main `09ba18d`의 CI 5개가 모두 통과했다.
- 새로 받은 PR refs 23개(67개 도달 가능 커밋)를 검사했다. 병합된 PR #1~#12 및 #18의 head 이력에 이전 이메일이 남는다. 열린 PR #13~#17의 head/merge refs에는 해당 주소가 없다.
- 별도 wiki master의 2개 커밋에는 해당 이메일이 없다.
- 상세 검사는 로컬 `.artifacts/publication-audit/remaining-ref-audit.json`, 보내지 않은 GitHub 지원 요청 초안은 같은 디렉터리의 `github-support-draft.md`에 있다. GitHub가 관리하는 PR refs와 캐시는 일반 push로 정리할 수 없으며, 지원팀의 삭제 가능 여부를 확인해야 한다.
- 비공개 유지 방침에 따라 SHA 고정 Gitleaks workflow를 추가했다. push/PR 뒤 실행하는 검사이며 서버 push protection이나 환경 승인을 대체하지는 않는다. 게시 게이트는 false로 유지한다.
- 직접·전이 의존성의 복원된 NuGet manifest와 포함된 라이선스 파일을 대조했다. [의존성 목록](dependency-inventory.md)에 버전·근거·배포 관계를 기록하고 THIRD-PARTY-NOTICES를 보완했다. ARM 변경을 제외한 별도 작업 복사본에서 두 nupkg와 두 snupkg를 빌드하고 `check-packages.py --release`를 통과했다. 세 TFM의 자체 DLL만 포함하며 LICENSE·README·고지·심볼·작성자·MIT 메타데이터를 확인했다. 미커밋 문서에 대한 로컬 검사이므로 최종 후보의 Source Link·Windows 소비자 실행을 대신하지 않는다.

## 보류·외부 처리 항목

1. Windows x64 실행기 준비·격리 검증: 사용자 요청으로 보류. ARM 작업도 보류 유지.
2. 환경 승인·GitHub secret scanning: 비공개 저장소를 유지하며 지원되지 않는 기능은 미적용. 게시 차단과 대체 CI 검사 유지.
3. 이전 이메일의 PR refs·캐시 정리: 지원 요청 초안 준비 완료, 아직 전송하지 않음. 완전 삭제 완료로 표시하지 않는다.
4. Windows Stress와 패키지 소비자 실행을 포함한 최종 릴리스 후보 검증은 별도 단계다. 소스 출처 고지는 검토했지만 전체 upstream 코드의 권리를 보증한 것은 아니다.
