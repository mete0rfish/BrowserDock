"""Selected SeleniumBase high-level methods on the same controlled SB-05 page."""
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import sys
from urllib.parse import urlsplit


IDENTITY_FIELDS = ("browserdockCommit", "seleniumBaseVersion", "seleniumBaseCommit",
                   "chromeSha256", "vendorDriverSha256", "framework", "project",
                   "executionEnvironment", "fixtureId", "chromeVersion", "os", "architecture")


def run_interactions(scenario):
    import psutil
    from selenium import webdriver
    from selenium.webdriver.chrome.service import Service
    from selenium.webdriver.common.by import By
    from seleniumbase.core.browser_launcher import extend_driver

    investigation = os.environ.get("BROWSERDOCK_REFERENCE_INVESTIGATION") == "linux"
    assert (investigation and sys.platform.startswith("linux")) or (sys.platform == "win32" and sys.getwindowsversion().build >= 22000)
    fixture = json.loads(Path(os.environ["BROWSERDOCK_REFERENCE_METADATA"]).read_text(encoding="utf-8"))
    assert importlib.metadata.version("seleniumbase") == fixture["seleniumBaseVersion"]
    base = os.environ["BROWSERDOCK_REFERENCE_URL"].rstrip("/")
    assert urlsplit(base).scheme == "http" and urlsplit(base).hostname in ("127.0.0.1", "localhost")
    output = Path(os.environ["BROWSERDOCK_REFERENCE_RESULTS"])
    driver_path = Path(os.environ["BROWSERDOCK_DRIVER"])
    report = {key: fixture[key] for key in IDENTITY_FIELDS}
    report.update(scenarioId=scenario["id"], implementation="SeleniumBase", passed=False,
                  driverSha256=hashlib.sha256(driver_path.read_bytes()).hexdigest())
    report["vendorDriverSha256"] = report["driverSha256"]
    report["chromeSha256"] = hashlib.sha256(Path(os.environ["BROWSERDOCK_CHROME"]).read_bytes()).hexdigest()
    driver = None
    chrome = None
    service = None
    failure = None
    try:
        options = webdriver.ChromeOptions()
        options.binary_location = os.environ["BROWSERDOCK_CHROME"]
        if investigation:
            options.add_argument("--headless=new")
            options.add_argument("--no-sandbox")
            options.add_argument("--disable-dev-shm-usage")
        driver = webdriver.Chrome(service=Service(str(driver_path)), options=options)
        service = driver.service.process
        chrome = psutil.Process(driver.capabilities["goog:processID"])
        report["browserVersion"] = driver.capabilities["browserVersion"]
        assert report["browserVersion"] == fixture["chromeVersion"]
        created = chrome.create_time()
        session = driver.session_id
        # This is the same DriverMethods binding used by SeleniumBase Driver.
        # Launch with the supplied vendor binary; no UC patching/provisioning.
        extend_driver(driver, use_uc=False)
        driver.get(base + "/reference/interactions")
        driver.execute_script("window.armStatus()")
        contains = driver.wait_for_text("ready", "#status", timeout=10).text
        driver.execute_script("window.armExact()")
        exact = driver.wait_for_exact_text(" ready ", "#status", timeout=10).text
        initial = driver.wait_for_exact_text("initial", "#input", timeout=10).get_property("value")
        driver.execute_script("window.armButton()")
        driver.click("#button", timeout=10)
        driver.type("#input", "reference", timeout=10)
        driver.send_keys("#input", " + appended", timeout=10)
        value = driver.wait_for_exact_text("reference + appended", "#input", timeout=10).get_property("value")
        driver.switch_to.frame(driver.wait_for_element("#frame", timeout=10))
        driver.type("#frame-input", "frame-value", timeout=10)
        frame = driver.wait_for_exact_text("frame-value", "#frame-input", timeout=10).get_property("value")
        driver.switch_to.default_content()
        top = driver.wait_for_exact_text("ready", "#status", timeout=10).text
        driver.execute_script("window.armHide()")
        driver.wait_for_element_not_visible("#marker", timeout=10)
        hidden = driver.execute_script("return !!document.getElementById('marker') && getComputedStyle(document.getElementById('marker')).display==='none'")
        driver.execute_script("window.armRemove()")
        driver.wait_for_element_absent("#marker", timeout=10)
        state = driver.execute_script("return {clicks:window.clicks,inputAttribute:document.getElementById('input').getAttribute('value'),absent:!document.getElementById('marker')}")
        observed = dict(containsText=contains, exactText=exact, initialValue=initial, input=value,
                        frame=frame, topLevelText=top, hiddenPresent=hidden, clicks=state["clicks"],
                        inputAttribute=state["inputAttribute"], absent=state["absent"], title=driver.title,
                        originalLeaseUsable=driver.session_id == session)
        report["observed"] = observed
        assert observed == scenario["expected"]
        assert chrome.is_running() and chrome.create_time() == created
        report["chromePreserved"] = True
        report["sessionChanged"] = False
        report["passed"] = True
    except BaseException as error:
        failure = error
        report["error"] = type(error).__name__
        raise
    finally:
        try:
            if driver is not None:
                driver.quit()
            if chrome is not None:
                psutil.wait_procs([chrome], timeout=10)
            # Linux investigation parents may retain dead zombies; those no
            # longer execute or hold profile handles. Record that scope explicitly.
            def alive(process):
                try:
                    return process.is_running() and process.status() != psutil.STATUS_ZOMBIE
                except psutil.NoSuchProcess:
                    return False
            clean = service is not None and service.poll() is not None and chrome is not None and not alive(chrome)
            report["cleanupPassed"] = clean
            if not clean and failure is None:
                raise AssertionError("Reference processes survived driver.quit")
        except BaseException as error:
            report.update(cleanupPassed=False, passed=False, cleanupError=type(error).__name__)
            if failure is None:
                raise
        finally:
            (output / f"seleniumbase-{scenario['id']}.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
