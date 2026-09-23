<!-- browserdock-ko-wiki:v1 -->
# 10. 프로세스·프로필·로그를 안전하게 관리하기

> 핵심 질문: 작업 실패 후에도 무엇을 보호하고 무엇을 정리해야 하나요?

## 시작한 프로세스에만 책임을 집니다

BrowserDock은 Chrome과 ChromeDriver를 별도로 소유합니다. 구현 기록에는 Chrome을 suspended 상태에서 Windows Job Object에 먼저 할당한 뒤 실행해, 자식 프로세스가 소유권 추적 밖으로 나가는 시작 race를 피하는 처리가 설명되어 있습니다.

프로세스 이름이나 화면에 보이는 창의 개수만으로 소유권을 판단하지 않습니다. “남은 chrome.exe를 모두 종료”하는 복구 절차는 개인 브라우저까지 영향을 줄 수 있으므로 사용하지 마세요.

WebDriver 연결 해제 후 Chrome이 남는 것은 정상 계약입니다. 전체 종료 후 소유 Chrome이 남는 문제와 구분해서 진단합니다.

## 임시 프로필과 지정 프로필

| 설정 | 소유권과 정상 정리 정책 |
|---|---|
| `Profile.Directory` 생략 | 임시 프로필 생성, 소유 Chrome 종료 확인 후 삭제 |
| 전용 디렉터리 지정 | 독점 잠금, 종료 후 디렉터리는 보존하고 잠금 해제 |
| 개인 기본 Chrome 프로필 | 사용하지 않음 |

임시 프로필에는 소유권을 확인하는 marker가 사용됩니다. 프로필 경로 검증과 잠금은 잘못된 디렉터리 삭제 및 동시 사용을 방지하기 위한 경계입니다. 임의 경로를 강제로 지워 잠금 오류를 해결하지 마세요.

지정 프로필은 브라우저를 종료한 뒤에도 데이터를 남길 수 있습니다. 따라서 테스트 데이터에 계정 정보나 토큰을 넣었다면 해당 데이터의 보존까지 별도로 관리해야 합니다.

## 패치가 비활성화된 기본 경로

Chrome과 ChromeDriver의 버전 검증은 기본 사용 흐름에 포함됩니다. 패치 비활성화는 모든 호환성 검사를 생략한다는 뜻이 아닙니다.

| 모드 | 동작 |
|---|---|
| `Disabled` | 바이너리 패치를 적용하지 않음 |
| `ValidateOnly` | 지원 recipe의 패턴 개수를 검증하고 원본 사용 |
| `BinaryCompatibility` | 원본을 보존하고 검증된 캐시 사본 사용 |

`IDriverPatchStrategy`는 버전 범위와 패턴, recipe를 제공하는 확장점입니다. 캐시는 원본과 결과 hash 등을 확인하며, recipe나 검증이 맞지 않을 때 조용히 원본으로 fallback하지 않습니다.

**기준 구현에는 검증된 실제 ChromeDriver recipe가 포함되어 있지 않습니다.** 확장 구조가 존재하는 것과 실제 바이너리에 적용할 준비가 된 것은 다릅니다. 첫 학습 예제에서는 기본값을 유지합니다.

## 스크립트 등록의 범위

`BrowserOptions.NewDocumentScripts`는 새 문서에 적용할 스크립트 등록 경로입니다. 대상 session별 중복 등록을 관리하며 CDP 복구 후 다시 등록합니다. 이미 실행 중인 모든 문서를 과거부터 다시 실행시키는 기능으로 이해하지 않습니다.

CDC 관련 선택 설정 역시 기본 비활성화이며 효과를 보장하지 않습니다. 이 Wiki에서는 fingerprint 위장이나 특정 사이트 판정 우회를 학습 목표로 삼지 않습니다.

## 로그는 제어 경로를 막지 않아야 합니다

`BrowserOptions.Logger`로 `Microsoft.Extensions.Logging.ILogger`를 전달할 수 있습니다. `Diagnostics`는 제한된 큐를 사용하고 별도 worker에서 logging sink를 실행합니다.

현재 큐 용량은 256이며 `TryWrite()`가 실패하면 drop 수를 집계합니다. sink가 느리거나 예외를 던질 수 있으므로, 로그를 완전한 실행 이력 저장소로 간주하지 않습니다. 사용자의 sink 실패가 lifecycle 자체를 중단시키지 않도록 예외도 분리합니다.

기본 로그는 쿠키, storage, 페이지 내용, 스크립트 결과, URL, CDP payload를 기록하지 않는 정책입니다. 하지만 사용자 코드가 직접 출력하거나 사용자 sink가 추가한 데이터까지 자동으로 정제해 준다는 뜻은 아닙니다.

## 실패 기록에서 확인할 값

`Browser.Health`에는 상태, generation, epoch, Chrome/driver PID, 각 연결의 상태와 cleanup 실패 등이 포함됩니다. 오류를 보고할 때는 이 값들과 재현 순서를 함께 남깁니다.

민감한 URL이나 페이지 내용을 추가하기 전에 필요한 최소 정보인지 확인하세요. 프로필 디렉터리 전체, 쿠키, storage dump, 토큰을 이슈에 그대로 첨부하지 않습니다.

정리 도중 오류가 발생했다면 원래 작업 오류와 cleanup 실패를 함께 검토합니다. 예외가 반환되었다는 사실만으로 모든 자원이 사라졌다고 판단하지 않습니다.

**근거:** [소유권과 구현 기록](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/docs/implementation.md), [Hosting.cs](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Hosting.cs), [Patching.cs](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Patching.cs), [Diagnostics](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Infrastructure.cs)

[← 상황별 API](ko-API-Guide.md) · [목차](ko-Home.md) · [다음: 테스트 →](ko-Testing.md)
