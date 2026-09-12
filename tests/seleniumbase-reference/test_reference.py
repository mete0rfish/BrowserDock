"""Run only through run.py on Windows 11; no third-party target websites."""
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import sys
import time
import urllib.request
from urllib.parse import urlsplit

import pytest

ROOT = Path(__file__).parent
MANIFEST = json.loads((ROOT / "scenarios.json").read_text(encoding="utf-8"))
OBSERVATION = """JSON.stringify({path:location.pathname+location.hash,title:document.title,
button:document.querySelector('#button').textContent,input:document.querySelector('#input').value,
frame:document.querySelector('#frame').contentDocument.querySelector('#frame-input').value,
storage:localStorage.getItem('fixture'),cookie:document.cookie.split('; ').includes('fixture=kept'),
marker:document.body.dataset.marker||''})"""


def wait_for(predicate, timeout=10):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(0.05)
    raise AssertionError("Condition did not become true before deadline")


@pytest.mark.parametrize("scenario", MANIFEST["scenarios"], ids=lambda x: x["id"])
def test_reference(scenario):
    assert sys.platform == "win32" and sys.getwindowsversion().build >= 22000
    import psutil
    from seleniumbase import Driver
    from selenium.webdriver.common.by import By
    from selenium.webdriver.support.ui import WebDriverWait

    assert importlib.metadata.version("seleniumbase") == MANIFEST["seleniumBaseVersion"]
    base = os.environ["BROWSERDOCK_REFERENCE_URL"].rstrip("/")
    assert urlsplit(base).scheme == "http" and urlsplit(base).hostname in ("127.0.0.1", "localhost")
    output = Path(os.environ["BROWSERDOCK_REFERENCE_RESULTS"])
    driver = None
    chrome = None
    service = None
    profile = None
    failure = None
    report = {"scenarioId": scenario["id"], "implementation": "SeleniumBase", "passed": False,
              "seleniumBaseVersion": importlib.metadata.version("seleniumbase")}
    started = time.monotonic()
    try:
        driver = Driver(uc=True, headed=True, headless=False,
                        binary_location=os.environ["BROWSERDOCK_CHROME"],
                        driver_version=os.environ["BROWSERDOCK_REFERENCE_DRIVER_VERSION"])
        chrome = psutil.Process(driver.browser_pid)
        creation = chrome.create_time()
        service = driver.service.process
        actual_driver = Path(driver.service.path)
        report["driverSha256"] = hashlib.sha256(actual_driver.read_bytes()).hexdigest()
        report["browserVersion"] = driver.capabilities["browserVersion"]
        report["driverVersion"] = driver.capabilities["chrome"]["chromedriverVersion"]
        assert report["browserVersion"].split(".")[:3] == os.environ["BROWSERDOCK_REFERENCE_DRIVER_VERSION"].split(".")[:3]
        profile = driver.user_data_dir
        session = driver.session_id
        endpoint = driver.options.debugger_address
        driver.default_get(base + "/reference/page")
        driver.execute_script("localStorage.setItem('fixture','kept');document.cookie='fixture=kept;path=/';document.body.dataset.marker='kept'")
        if scenario["id"] == "SB-01":
            driver.find_element(By.ID, "button").click()
            driver.find_element(By.ID, "input").send_keys("reference")
            driver.switch_to.frame(driver.find_element(By.ID, "frame"))
            driver.find_element(By.ID, "frame-input").send_keys("frame-value")
            driver.switch_to.default_content()
        elif scenario["id"] == "SB-02":
            driver.disconnect()
            wait_for(lambda: service.poll() is not None, 5)
            # Independent endpoint probe while WebDriver is absent.
            with urllib.request.urlopen("http://" + endpoint + "/json/version", timeout=5) as response:
                assert json.load(response)["Browser"]
            assert chrome.is_running() and chrome.create_time() == creation
            driver.connect()
            assert driver.session_id != session
            report["sessionChanged"] = True
        elif scenario["id"] == "SB-03":
            driver.uc_open_with_reconnect(base + "/reference/redirect", reconnect_time=0.1)
        else:
            driver.activate_cdp_mode(base + "/reference/page?phase=cdp#cdp")

        if scenario["id"] == "SB-04":
            # CDP evaluation must remain disconnected; do not call WebDriver here.
            wait_for(lambda: driver.cdp.evaluate("document.readyState") == "complete")
            report["observed"] = json.loads(driver.cdp.evaluate(OBSERVATION))
            assert not driver.is_connected()
        else:
            WebDriverWait(driver, 10).until(lambda d: d.execute_script("return document.readyState") == "complete")
            report["observed"] = json.loads(driver.execute_script("return " + OBSERVATION))
        assert report["observed"] == scenario["expected"]
        assert chrome.is_running() and chrome.create_time() == creation
        assert driver.user_data_dir == profile and driver.options.debugger_address == endpoint
        report["chromePreserved"] = True
        report["passed"] = True
    except BaseException as error:
        failure = error
        report["error"] = repr(error)
        raise
    finally:
        try:
            if driver is not None:
                current_service = driver.service.process
                driver.quit()
                wait_for(lambda: current_service is None or current_service.poll() is not None, 5)
            if service is not None:
                wait_for(lambda: service.poll() is not None, 5)
            wait_for(lambda: chrome is not None and not chrome.is_running(), 10)
            if profile is not None:
                wait_for(lambda: not Path(profile).exists(), 10)
            report["cleanupPassed"] = True
        except Exception as error:
            report.update(cleanupPassed=False, cleanupError=repr(error), passed=False)
            if failure is None:
                raise
        finally:
            report["elapsedMs"] = round((time.monotonic() - started) * 1000)
            output.mkdir(parents=True, exist_ok=True)
            (output / ("seleniumbase-" + scenario["id"] + ".json")).write_text(json.dumps(report, indent=2), encoding="utf-8")
