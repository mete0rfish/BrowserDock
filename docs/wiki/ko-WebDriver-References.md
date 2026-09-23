<!-- browserdock-ko-wiki:v1 -->
# 08. WebDriver 연결과 오래된 참조 다루기

> 핵심 질문: 같은 Chrome과 같은 페이지인데 이전 참조는 왜 사용할 수 없나요?

## attachment를 만드는 과정

`Attachment.CreateAsync()`는 후보 포트에서 ChromeDriver를 시작하고, 소유 프로세스가 살아 있으며 그 프로세스가 loopback 포트를 소유하는지 확인합니다. `/status`가 준비 상태를 반환한 뒤 Selenium의 공개 `RemoteWebDriver` 생성자를 사용합니다.

Chrome 옵션에는 이미 소유한 브라우저의 debugger address가 전달됩니다. 새 ChromeDriver가 원래 Chrome에 연결하는 구조이지, 매번 Chrome을 새로 실행하는 구조가 아닙니다.

정리 경로에서는 `DetachAwareCommandExecutor`가 `Quit`을 원격으로 전달하지 않습니다. 현재 구현은 detaching 여부와 무관하게 Quit을 로컬 성공으로 처리합니다. 실제 소유 Chrome 종료 책임은 `Browser`에 있기 때문입니다. 설계 설명의 이름만 보고 조건부 차단이라고 추측하지 마세요.

## lease가 제공하는 것

`GetWebDriverAsync()`는 현재 attachment를 사용하는 `WebDriverLease`를 반환합니다. 명령은 `lease.Commands`로 실행합니다. 같은 연결에서 새 lease를 얻는 것 자체가 새 WebDriver 세션 생성은 아닙니다.

명령은 lease 폐기 여부, 현재 상태, epoch를 확인한 뒤 진입합니다. 연결 해제가 시작되면 이전 epoch의 명령은 더 이상 정상 진입할 수 없습니다.

lease의 `DisposeAsync()`는 해당 lease만 폐기합니다. 그 lease를 통해 찾았다는 이유만으로 모든 `ElementRef`의 수명을 lease의 dispose에 종속된 것으로 설명하지 않습니다. 요소는 자신의 attachment epoch와 target/frame 문맥으로 검사됩니다.

## 오래된 참조에는 두 종류가 있습니다

| 상황 | 오류 분류 | 기본 대응 |
|---|---|---|
| 같은 세션에서 DOM 노드가 교체됨 | `StaleDomElement` | 현재 DOM에서 명시적으로 다시 찾기 |
| WebDriver 연결의 유효성 경계가 바뀜 | `StaleAttachment` | 새 연결을 확보하고 참조 재획득 |
| 대상 탭이 닫힘 | `TargetClosed` 등 현재 상태에 따른 오류 | target의 존재와 선택부터 확인 |

`ElementRef`는 locator, target, frame 경로, generation, epoch를 보관합니다. `ReacquireAsync()`는 이 탐색 문맥을 사용해 **새 객체를 반환**합니다. 기존 DOM 노드 자체를 복구하거나, 같은 locator가 항상 같은 의미의 요소를 찾는다는 것을 보장하지 않습니다.

## 재연결 전후를 비교하는 예제

다음은 `using BrowserDock;`가 있고 `browser`가 시작되어 있으며 [03장의 로컬 페이지](ko-Getting-Started.md)가 열린 문맥에서 실행하는 부분 예제입니다. 연결 교체 효과만 보기 위해 중간에 navigation을 하지 않습니다.

```csharp
await using var first = await browser.GetWebDriverAsync();
var oldInput = await first.Commands.FindAsync(Locator.Id("name"));
await oldInput.SendKeysAsync("BrowserDock");

await browser.DisconnectWebDriverAsync();
await using var next = await browser.ReconnectWebDriverAsync();

try
{
    await oldInput.SendKeysAsync(" 이전 참조");
    throw new InvalidOperationException("이전 참조가 거부되어야 합니다.");
}
catch (StaleAttachmentException)
{
    Console.WriteLine("이전 attachment 참조가 거부되었습니다.");
}

var freshInput = await oldInput.ReacquireAsync();
await freshInput.ClearAsync();
await freshInput.SendKeysAsync("새 참조");
```

이 예제는 기본 `FindOptions`를 사용합니다. `ReacquireOnSessionChange=true`를 명시하면 연결 변경 시 자동 재획득을 시도하는 경로가 있으므로 같은 실패 관찰을 기대하면 안 됩니다. 자동 재획득도 대상이 사라졌거나 locator가 맞지 않으면 실패합니다.

## 입력값 확인 시 주의할 점

`GetAttributeAsync()`의 현재 구현은 Selenium의 `GetDomAttribute()`를 호출합니다. `<input>`에 사용자가 입력한 현재 `value` property와 HTML에 기록된 `value` attribute는 다를 수 있습니다.

현재 입력값이 필요하다면 JSON 호환 값으로 반환하는 스크립트를 사용할 수 있습니다. 아래 코드는 위 예제와 같은 문맥에서 실행합니다.

```csharp
var value = await next.Commands.ExecuteScriptAsync(
    "return document.getElementById('name').value");
Console.WriteLine(value.GetString());
```

`return document.getElementById('name')`처럼 DOM 요소를 반환하는 방식은 공개 경계의 정책에 맞지 않습니다. 요소 조작에는 `ElementRef`를 사용합니다.

## 탭과 iframe도 참조의 일부입니다

`FindOptions.FramePath`는 바깥 iframe부터 안쪽 iframe까지 locator 경로를 지정합니다. 요소를 재획득할 때 이 경로와 원래 target을 사용합니다. 닫힌 탭의 key를 다른 탭에 임의로 대응시키지 않습니다.

CDP target과 WebDriver window의 대응은 무작위 임시 JavaScript marker를 양쪽에서 관찰해 확인합니다. window handle 문자열에서 target ID를 잘라내거나 마지막 탭을 선택하는 가정에 기대지 않습니다.

**근거:** [Elements.cs](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Elements.cs), [WebDriver.cs](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/WebDriver.cs), [target 대응과 명령 검사](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Browser.cs)

[← CDP](ko-CDP.md) · [목차](ko-Home.md) · [다음: 상황별 API →](ko-API-Guide.md)
