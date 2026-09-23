<!-- browserdock-ko-wiki:v1 -->
# 03. 개발 환경 구성과 첫 실행

> 목표: 공통 테스트와 실제 Chrome 실행을 구분하고, 저장소의 Lifecycle 샘플을 실행합니다.

## 실행 환경부터 구분하기

| 작업 | 필요한 환경 |
|---|---|
| 소스 복원·전체 빌드 | 저장소가 요구하는 .NET 10 SDK |
| net8/net10 공통 테스트 | 선택한 대상의 .NET 런타임 |
| 실제 Browser.StartAsync 실행 | Windows 11, x64 프로세스, Chrome, 호환 ChromeDriver |
| net481 실제 실행 | Windows의 .NET Framework 4.8.1 런타임 |

Mac에서 전체 솔루션을 빌드할 수 있다는 것과 Mac에서 제품의 브라우저 호스팅을 실행할 수 있다는 것은 다릅니다. `Browser.Validate()`는 Windows 11 x64를 검사합니다. ARM64 Windows의 실행 가능 여부도 x64 지원 완료와 동일하게 표시하지 마세요.

먼저 저장소의 `global.json`과 로컬 `dotnet --info`를 비교합니다. 이 페이지는 최신 SDK 설치를 무조건 권하는 문서가 아니라, 기준 소스에 맞는 도구를 사용하는 안내입니다.

## 소스 복원과 공통 테스트

다음 명령은 저장소 최상위 디렉터리에서 실행합니다. private 저장소라면 Git 인증이 설정되어 있어야 합니다.

```sh
git clone https://github.com/mete0rfish/BrowserDock.git
cd BrowserDock
dotnet --info
dotnet restore BrowserDock.slnx
dotnet build BrowserDock.slnx --no-restore -m:1
```

이어서 실제 Chrome을 요구하는 테스트를 제외하고 실행합니다.

```sh
dotnet test tests/BrowserDock.Tests -f net10.0 --no-restore --filter "TestCategory!=Windows"
dotnet test tests/BrowserDock.FrameworkTests -f net10.0 --no-restore --filter "TestCategory!=Windows"
```

성공 여부와 함께 실행 건수, skip, 대상 프레임워크를 기록합니다. 이 명령의 성공은 Windows 브라우저 수용 시험의 성공을 뜻하지 않습니다.

## Chrome과 ChromeDriver 준비

실행 파일은 직접 준비합니다. 기준 구현은 Chrome/ChromeDriver M115 이상에서 `MAJOR.MINOR.BUILD`의 일치를 검사합니다. 라이브러리가 실행 파일을 자동으로 내려받지는 않습니다.

Chrome 경로는 생략하면 표준 위치를 찾지만 ChromeDriver 경로는 필요합니다. 처음에는 두 경로 모두 명시하는 편이 문제를 추적하기 쉽습니다. 개인이 사용하는 기본 Chrome 프로필은 지정하지 않습니다.

## 첫 실행: 기존 샘플 사용하기

Windows 터미널에서 다음 명령을 실행합니다. 두 실행 파일 경로는 자신의 경로로 바꿉니다. 이 예제의 외부 URL 접근에는 네트워크가 필요합니다.

```powershell
dotnet run --project samples/Lifecycle -- "C:\Chrome\chrome.exe" "C:\Chrome\chromedriver.exe" "https://example.com"
```

샘플은 제목을 출력하고 WebDriver를 분리한 뒤, 상태와 Chrome PID를 출력합니다. 이어 재연결한 세대와 URL을 출력합니다. 종료 시 `await using`으로 브라우저를 정리합니다.

관찰할 것은 고정된 PID 숫자가 아니라 관계입니다. 정상 흐름에서는 분리 중 Chrome이 유지되고, 재연결은 새로운 세션 세대로 진행해야 합니다. 출력이 기대와 다르면 무조건 재시도하기보다 [12장의 진단 순서](ko-Debugging.md)를 따릅니다.

## 요소 실습용 로컬 페이지

외부 사이트의 변경을 피하려면 다음 내용을 `.artifacts/wiki-fixture/index.html`에 저장합니다. 디렉터리는 먼저 만드세요.

```html
<!doctype html>
<html lang="ko">
<meta charset="utf-8">
<title>BrowserDock 학습</title>
<label>이름 <input id="name"></label>
<button id="save" type="button">저장</button>
<p id="status">아직 저장하지 않았습니다.</p>
<script>
  document.querySelector('#save').addEventListener('click', () => {
    const value = document.querySelector('#name').value;
    localStorage.setItem('browserdock-study-name', value);
    document.querySelector('#status').textContent = value;
  });
</script>
</html>
```

Python 3이 설치된 별도 터미널에서 저장소 최상위 기준으로 실행합니다. Windows에서 `python` 대신 `py -3`을 사용하는 환경이라면 그 부분만 바꿉니다.

```sh
python -m http.server 8765 --bind 127.0.0.1 --directory .artifacts/wiki-fixture
```

샘플의 URL을 `http://127.0.0.1:8765/`로 바꾸면 됩니다. 실습 후 서버는 해당 터미널에서 `Ctrl+C`로 종료합니다. 이 페이지는 학습용이며 저장소의 정식 테스트 fixture를 대체하지 않습니다.

## 실행 결과를 기록하는 법

OS와 프로세스 아키텍처, SDK/runtime, Chrome/ChromeDriver 버전, 소스 커밋, 실행 명령, 성공·실패·skip을 함께 기록합니다. 이 Wiki의 설명 예제를 작성하면서 Windows 브라우저 실행까지 검증한 것은 아닙니다.

**근거:** [Lifecycle 샘플](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/samples/Lifecycle/Program.cs), [환경 검사](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/src/BrowserDock/Browser.cs), [개발 안내](https://github.com/mete0rfish/BrowserDock/blob/8a8612fffe681334c4e6ec8384ab18a6ba66271f/README.ko.md)

[← 배경지식](ko-Core-Concepts.md) · [목차](ko-Home.md) · [다음: 코드 지도 →](ko-Code-Map.md)
