<!-- browserdock-ko-wiki:v1 -->
# 12. 문제를 추적하고 작은 변경 해보기

> 목표: 추측으로 코드를 바꾸기 전에 실패한 계약과 재현 조건을 찾습니다.

## 증상부터 분류합니다

| 증상 | 먼저 확인할 정보 | 읽을 코드 |
|---|---|---|
| 시작 직후 환경 오류 | Windows 버전, x64 프로세스 여부 | `Browser.Validate()` |
| 버전 또는 경로 오류 | 두 실행 파일의 절대 경로와 버전 | `Hosting.cs` |
| 프로필 사용 실패 | 개인 기본 프로필 여부, 다른 실행의 잠금 | `Hosting.cs`의 프로필 처리 |
| endpoint를 찾지 못함 | Chrome 시작 결과, 소유 프로세스, 프로필 경로 | endpoint 발견 경로 |
| 세션 생성 실패 | driver readiness, 포트 소유권, 내부 예외 | `Attachment.CreateAsync()` |
| `StaleAttachment` | 마지막 disconnect, 현재 epoch, lease 폐기 여부 | `Admission`, `Elements.cs` |
| `StaleDomElement` | DOM 교체와 locator, frame 경로 | `ElementRef`, 오류 매핑 |
| 탭 선택 오류 | target 후보 수와 명시 선택 여부 | `BindTargetAsync()`, `CdpController` |
| 전체 종료 후 자원 잔존 | `Health`, cleanup 실패, 정확한 소유 자원 | `StopAsync()`와 Hosting |

오류 메시지만 복사하지 말고, 어떤 API를 어떤 순서로 호출했는지 기록합니다. 같은 오류 분류라도 시작 중 실패인지 재연결 중 실패인지에 따라 읽을 경로가 달라집니다.

## 재현을 작게 만듭니다

외부 사이트 대신 [03장의 로컬 페이지](ko-Getting-Started.md)를 사용합니다. 병렬 실행, 사용자 스크립트, patch 설정을 먼저 제외하고 기본값으로 재현되는지 확인합니다.

그다음 시작 → 한 번의 이동 → 한 번의 연결 교체 → 종료만 남깁니다. 이 작은 흐름에서도 실패하면 로그와 상태 변화의 원인을 좁히기 쉽습니다. 재현되지 않으면 제외한 조건을 하나씩 되돌립니다.

timeout을 크게 늘리거나 모든 예외를 catch해서 무시하면 실패를 숨길 수 있습니다. 특히 cleanup 실패는 다음 실행의 간섭 원인이 될 수 있으므로 별도로 기록해야 합니다.

## 첫 실습: 기존 계약 테스트를 따라가기

`ContractTests.cs`의 `PublicSeleniumConstructorAndDisposeNeverSendDeleteSession`을 읽어 보세요. 가짜 서버가 요청을 기록하고, dispose 이후 DELETE 요청이 발생하지 않았는지 검사합니다.

다음 세 가지를 설명할 수 있으면 테스트의 목적을 이해한 것입니다. 준비 단계에서는 어떤 응답을 만들었는지, 실행 단계에서는 어떤 공개 Selenium 경로를 사용했는지, 검증 단계에서는 어떤 요청이 없어야 하는지입니다.

이 시험은 실제 Chrome 종료 여부를 직접 확인하는 시험이 아니라 executor의 원격 명령 계약을 확인하는 시험입니다.

## 두 번째 실습: epoch 불변조건 확인하기

아래는 `BrowserDock.Tests` 안에 추가할 수 있는 학습용 NUnit 테스트입니다. 기존 테스트에 같은 경우가 있는지 먼저 확인하고, 중복이라면 기존 시험의 설명을 개선하는 방식으로 바꿉니다.

```csharp
using NUnit.Framework;

namespace BrowserDock.Tests;

[TestFixture]
public sealed class WikiAdmissionExerciseTests
{
    [Test]
    public void ReopeningDoesNotRestoreAnOldEpoch()
    {
        var admission = new Admission();
        var oldEpoch = admission.Open();
        _ = admission.Close();
        var newEpoch = admission.Open();

        Assert.That(newEpoch, Is.GreaterThan(oldEpoch));
        Assert.Throws<StaleAttachmentException>(() =>
        {
            using var rejected = admission.Enter(oldEpoch);
        });
        using var accepted = admission.Enter(newEpoch);
    }
}
```

`Admission`은 제품의 공개 API가 아닙니다. 이 예제는 internal 접근이 허용된 저장소 테스트 프로젝트용이며, 일반 소비 앱에 복사하는 사용 예제가 아닙니다.

아래 필터로 해당 실습만 실행하고, 실제 1건 이상 수행되었는지 확인합니다. 이 문서를 추가한다고 테스트 파일 자체가 제품 저장소의 시험 목록에 자동으로 추가되는 것은 아닙니다.

```sh
dotnet test tests/BrowserDock.Tests -f net10.0 --filter "FullyQualifiedName~WikiAdmissionExerciseTests"
```

## 변경의 효과를 증명합니다

학습용 별도 브랜치에서 검사를 의도적으로 깨뜨려 테스트가 실패하는지 확인한 뒤 반드시 원복해 보세요. 이는 현재 코드에 결함이 있다는 주장이 아니라, 테스트가 실제 불변조건을 감지하는지 확인하는 실습입니다.

제품 코드를 변경했다면 작은 테스트만 통과한 것으로 끝내지 않습니다. 영향을 받는 공통 테스트와 전체 빌드를 실행하고, 브라우저 경로를 바꿨다면 Windows 실제 시험 범위도 정합니다.

## 좋은 변경 설명의 형태

“테스트를 추가했다”보다 “연결을 다시 열어도 이전 epoch로 명령에 진입할 수 없다는 계약을 검사했다”가 유용합니다. 변경한 파일, 계약, 재현 조건, 실행한 검증, 실행하지 않은 검증을 함께 적습니다.

이제 [기여와 릴리스 안내](ko-Contributing.md)에서 저장소의 실제 기여 문서와 검토 절차를 확인할 수 있습니다.

**근거:** [ContractTests.cs](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/tests/BrowserDock.Tests/ContractTests.cs), [Admission 구현](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Infrastructure.cs), [오류 분류](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Errors.cs)

[← 테스트와 CI](ko-Testing.md) · [목차](ko-Home.md) · [용어집 →](ko-Glossary.md)
