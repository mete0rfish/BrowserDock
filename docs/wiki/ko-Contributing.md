<!-- browserdock-ko-wiki:v1 -->
# 부록 D. 기여와 릴리스 안내

이 페이지는 학습 후 작은 기여를 시작하기 위한 안내입니다. 저장소의 실제 정책을 복제하는 대신 원문으로 연결합니다. 정책을 변경하는 문서가 아닙니다.

## 작은 변경부터 시작하기

수정할 계약을 하나 선택합니다. 예를 들어 설정 검증, 참조 무효화, 오류 분류, 문서의 실행 조건처럼 범위가 분명한 항목이 적합합니다.

현재 동작을 재현하는 테스트를 확인하고, 변경으로 바뀌는 결과를 설명합니다. 관련 없는 리팩터링과 대규모 포맷 변경을 같은 수정에 섞지 않으면 검토할 차이가 명확해집니다.

작업 절차와 코딩·PR 안내의 기준은 [CONTRIBUTING.md](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/CONTRIBUTING.md)입니다. 행동 규범은 [CODE_OF_CONDUCT.md](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/CODE_OF_CONDUCT.md)를 확인합니다.

## PR에 포함할 설명

무엇을 바꿨는지와 함께 왜 필요한지, 어떤 계약이 영향을 받는지, 실행한 검증과 실행하지 않은 검증을 적습니다. 브라우저 테스트가 필요한 변경을 공통 테스트 결과만으로 완료라고 표현하지 않습니다.

재현 예제는 가능하면 개인 계정이나 외부 사이트 대신 로컬 fixture로 만듭니다. 진단 자료에 쿠키·토큰·개인 프로필을 포함하지 않습니다. 취약점 의심 사항은 공개 이슈에 민감한 세부사항을 바로 게시하기보다 [SECURITY.md](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/SECURITY.md)의 현재 신고 경로를 확인합니다.

## 문서 변경도 코드와 연결합니다

Wiki의 근거 소스는 커밋으로 고정되어 있습니다. 코드가 바뀌면 설명의 기준 커밋, 예제의 API, 오류 분류, 테스트 이름을 같이 검토합니다.

페이지 하나가 길어지면 같은 기능을 여러 문서에 복제하기보다 핵심 설명을 한곳에 두고 연결합니다. 영어판을 추가할 때도 한국어 문서의 코드·주의사항·검증 범위가 빠지지 않도록 1:1로 대응시키는 것이 이 학습 문서의 작성 원칙입니다. 영어 페이지가 없을 때는 가짜 언어 전환 링크를 만들지 않습니다.

## 패키지 생성과 공개 게시는 다릅니다

로컬 `dotnet pack` 성공은 공개 NuGet 게시 완료가 아닙니다. 패키지 메타데이터, 대상 프레임워크, 심볼, 소비 프로젝트, Windows 검증, 라이선스와 권리자 확인 등 각각의 조건을 확인해야 합니다.

구체적인 게이트와 실행 절차는 [docs/releasing.md](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/docs/releasing.md)를 사용합니다. 공개 준비 체크리스트는 [docs/open-source-readiness.md](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/docs/open-source-readiness.md), 변경 기록은 [CHANGELOG.md](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/CHANGELOG.md)에 있습니다.

검증된 라이선스 조건이나 실제 게시 여부를 확인하지 않고 배포 버튼을 누르는 절차는 이 문서에 포함하지 않습니다.

## 문서 검토 체크

설명이 현재 소스와 일치하는지 확인합니다. 다음으로 예제의 시작 상태와 정리 방법이 보이는지, 내부 링크가 실제 페이지로 연결되는지 확인합니다. 마지막으로 기대 동작·관찰한 결과·제안 사항이 서로 다른 상태로 표시되어 있는지 읽습니다.

문서 자체가 잘 렌더링되는 것과 제품이 목표 환경에서 잘 동작하는 것도 별개입니다. Markdown 검사 결과를 Windows 수용 시험 결과로 보고하지 않습니다.

[학습 가이드로 돌아가기](ko-Home.md) · [디버깅 실습](ko-Debugging.md)
