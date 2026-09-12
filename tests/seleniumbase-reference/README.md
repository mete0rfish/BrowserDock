# SeleniumBase 비교 시험

Windows 11 x64의 대화형 데스크톱에서 실행한다. 공통 fixture의 일반 조작(SB-01), 같은 문서의 disconnect/connect(SB-02), 탭을 교체하는 detached navigation(SB-03), CDP-only 이동(SB-04)을 검사한다. 외부 사이트를 방문하지 않는다.

SeleniumBase **4.53.7**을 고정했다. 기존 명세의 커밋 `4ee7dfc4ae83c19385f5ac129f2cda0cfa863d80`의 `seleniumbase/__version__.py`와 Driver/UC 호출 경로를 확인한 버전이다. Python 패키지는 PyPI release를 설치하며 전체 전이 의존성은 각 실행의 `fixture.json`에 `pip freeze`로 남긴다. 완전히 같은 환경을 재현하려면 해당 목록을 별도 lock 파일로 보관해 설치한다.

```powershell
python -m venv .artifacts/seleniumbase-venv
& ./.artifacts/seleniumbase-venv/Scripts/python.exe -m pip install -r tests/seleniumbase-reference/requirements.txt
& ./.artifacts/seleniumbase-venv/Scripts/python.exe tests/seleniumbase-reference/run.py --chrome C:\Chrome\chrome.exe --driver C:\Chrome\chromedriver.exe --results .artifacts/reference-stable-01 --repeat 3
```

Python 3.11/3.12 x64, .NET 10 SDK, 선택한 .NET 8/10 runtime이 필요하다. `--framework net8.0`으로 core의 다른 TFM을 검사한다. 결과 폴더는 새 경로여야 한다. Stable-1 pair는 다른 결과 폴더로 다시 실행한다. runner는 fixture 서버를 시작하고 같은 URL을 두 구현에 전달하며 반복마다 별도 브라우저를 시작한다. 빌드·시험 로그, JSON 관찰값, pytest JUnit XML, NUnit TRX와 최종 `comparison.json`을 저장한다. 두 구현이 같은 잘못된 값을 내도 manifest와 다르면 실패한다. 누락된 JSON과 cleanup 실패도 실패다.

SeleniumBase는 지정한 vendor driver의 **버전**에 맞는 UC driver를 자체 준비·패치할 수 있다. 최초 실행에는 다운로드 접근과 venv 쓰기 권한이 필요할 수 있다. 원본 vendor driver를 직접 패치하도록 전달하지 않는다. 실행 결과에는 vendor hash와 SeleniumBase가 실제 실행한 driver hash를 따로 남긴다. BrowserDock 쪽은 패치 비활성화 상태다.

`scripts/test-windows.ps1`은 .NET reference 시험도 포함하지만 Python과의 비교는 이 runner를 별도 실행해야 한다. 레거시 facade는 `BrowserDock.FrameworkTests`에서 net481/net8.0/net10.0으로 검사한다.

Python 판정기는 Chrome·SeleniumBase 설치 없이 검증할 수 있다.

```sh
python3 -m unittest discover -s tests/seleniumbase-reference -p test_compare.py -v
```

비교 범위는 공통 페이지 동작과 browser/session 관계다. SeleniumBase의 전체 기능, CAPTCHA, fingerprint, 패치 효과의 동등성을 주장하지 않는다. 실패 시 재실행으로 이전 결과를 덮어쓰지 않는다. 브라우저 실행 검증 전에는 runner가 해당 환경에서 통과했다고 간주하지 않는다.
