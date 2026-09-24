# SeleniumBase comparison tests

Run on an interactive Windows 11 x64 desktop. The shared fixture covers ordinary interaction (SB-01), same-document disconnect/connect (SB-02), detached navigation replacing a tab (SB-03), and CDP-only navigation (SB-04). It does not visit external sites.

SeleniumBase is pinned to **4.53.7**, verified against `seleniumbase/__version__.py` and the Driver/UC call paths at specification commit `4ee7dfc4ae83c19385f5ac129f2cda0cfa863d80`. Install the PyPI release; each run records transitive dependencies through `pip freeze` in `fixture.json`. Preserve that list as a separate lock file for exact environment reproduction.

```powershell
python -m venv .artifacts/seleniumbase-venv
& ./.artifacts/seleniumbase-venv/Scripts/python.exe -m pip install -r tests/seleniumbase-reference/requirements.txt
& ./.artifacts/seleniumbase-venv/Scripts/python.exe tests/seleniumbase-reference/run.py --chrome C:\Chrome\chrome.exe --driver C:\Chrome\chromedriver.exe --results .artifacts/reference-stable-01 --repeat 3
```

Requires Python 3.11/3.12 x64, the .NET 10 SDK, and the selected .NET 8/10 runtime. Use `--framework net8.0` for the other Core TFM. The result directory must be new; use another directory for Stable-1. The runner starts the fixture server, sends the same URL to both implementations, and launches separate browsers for each repetition. It stores build/test logs, JSON observations, pytest JUnit XML, NUnit TRX, and final `comparison.json`. Even matching wrong outputs fail if they disagree with the manifest. Missing JSON and cleanup failures also fail.

SeleniumBase may prepare and patch its own UC driver matching the vendor driver's **version**. The first run may require downloads and write access to the venv. The original vendor binary is not passed for direct patching. Results record separate hashes for the vendor driver and the driver SeleniumBase actually launches. BrowserDock runs with patching disabled.

`scripts/test-windows.ps1` includes .NET reference tests, but Python comparison requires this separate runner. `BrowserDock.FrameworkTests` checks the Legacy facade on net481/net8.0/net10.0.

The Python comparator can be tested without installing Chrome or SeleniumBase.

```sh
python3 -m unittest discover -s tests/seleniumbase-reference -p test_compare.py -v
```

Comparison covers shared page behavior and browser/session relationships. It does not claim equivalence for all SeleniumBase features, CAPTCHAs, fingerprints, or patch effects. Never overwrite failed results with a rerun. Until browser execution is verified, do not claim this runner passes in that environment.
