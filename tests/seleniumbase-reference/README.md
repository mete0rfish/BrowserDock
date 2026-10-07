# SeleniumBase comparison tests

The **interactions** suite adds SB-05 independently of the SB-01–04 baseline.
It calls BrowserDock's high-level click/type/append/text/hidden/absence APIs and
the corresponding SeleniumBase DriverMethods bindings (`Driver.type` binds to
`update_text`). SeleniumBase is extended onto a normal Selenium driver launched
with the supplied vendor binary: no UC patching or implicit driver downloads.
The scenario includes delayed visibility/readiness, exactly one click, replace
then append, an input value differing from its HTML attribute, explicit frame
input/value reads followed by a top-level operation, and hiding before removal.

Use a clean, committed candidate and fresh result directories:

```powershell
python tests/seleniumbase-reference/run.py --suite interactions --project core --framework net10.0 --chrome C:\Chrome\chrome.exe --driver C:\Chrome\chromedriver.exe --results .artifacts/interactions-core --repeat 3
python tests/seleniumbase-reference/run.py --suite interactions --project legacy --framework net481 --chrome C:\Chrome\chrome.exe --driver C:\Chrome\chromedriver.exe --results .artifacts/interactions-legacy --repeat 3
```

The Windows interaction suite is also available on Core net8.0 and Legacy
net8.0/net10.0. The runner checks both binary and actual browser versions, records
Chrome/vendor/executed-driver hashes, the BrowserDock commit, pinned SeleniumBase
version/commit, project/runtime, fixture identity and `dependencies.lock.txt`.
The comparator rejects missing/mismatched identities, session replacement, failed
cleanup and matching observations that disagree with the manifest.

For a separately labeled **Linux investigation** using Core or Legacy on net10:

```sh
python tests/seleniumbase-reference/run.py --suite interactions --linux-investigation --project core --framework net10.0 --chrome /path/to/chromium --driver /path/to/chromedriver --results /tmp/interactions-core --repeat 3
# Repeat with --project legacy and a different result directory.
```

This launches a test-only .NET host that explicitly owns headless Chrome and its
driver, using the same interaction scenario as the optional Windows test. It does
not extend BrowserDock's supported Windows startup API or establish Windows,
net481 browser, UC, patching or native-input acceptance. Linux zombie processes
are considered exited for this investigation; no profile-handle/Windows Job claim
is made from that result. Python uses a standard driver-created browser; BrowserDock
attaches to the investigation's externally launched Chrome. Only selected
interaction observations are compared, not their different startup mechanisms.

The ordinary click fixture does not exercise intercepted-click fallback or
uncertain completion. Append is compared at the caret left by typing; deliberate
caret/selection differences remain intentional deviations. Invalid selector,
missing-frame, stale-reference, transport, timeout and cancellation behavior is
covered separately by .NET contract tests; SeleniumBase's broad exception-to-hidden
behavior is deliberately not copied. Exact text uses .NET `String.Trim`; Python
`str.strip` also strips C0 separators U+001C–U+001F, which are outside this selected
fixture. A passing SB-05 result does not close #32 or #36.

Run on an interactive Windows 11 x64 desktop. The shared fixture covers ordinary interaction (SB-01), same-document disconnect/connect (SB-02), detached navigation replacing a tab (SB-03), and CDP-only navigation (SB-04). It does not visit external sites.

**Current coverage:** The .NET SB-03/SB-04 cases are removed pending diagnosis and
restoration in [#41](https://github.com/mete0rfish/BrowserDock/issues/41).
`ReferenceTests` currently emits only SB-01/SB-02 results. The manifest, Python
scenarios, and comparator still require all four scenarios, so the full comparison
command below fails for missing .NET results until those cases are restored.

The [compatibility matrix](../../docs/seleniumbase-compatibility.md) defines the selected phased APIs and row-specific acceptance requirements. SB-01–04 cover only the subsets listed there; they do not verify full UC ordering/timing, high-level interactions, native input, Pure CDP startup or real patch parity. #36 tracks the expanded differential evidence. The additional acceptance cases are not implemented scenarios in this runner.

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
