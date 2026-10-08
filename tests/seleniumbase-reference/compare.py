"""Validate both sides against the manifest, including missing/failed runs."""
import json
import re
from pathlib import Path


def compare(directory, manifest, metadata=None):
    errors = []
    if not manifest["scenarios"]:
        return ["Scenario manifest must not be empty"]
    if any(s.get("suite") == "interactions" for s in manifest["scenarios"]):
        if not isinstance(metadata, dict):
            errors.append("Interactions require fixture metadata")
            metadata = {}
        for key in ("seleniumBaseVersion", "seleniumBaseCommit"):
            if metadata.get(key) != manifest.get(key):
                errors.append(f"Fixture {key} differs from pinned manifest")
        for key, length in (("browserdockCommit", 40), ("chromeSha256", 64), ("vendorDriverSha256", 64)):
            if not re.fullmatch(r"[0-9a-f]{%d}" % length, str(metadata.get(key, ""))):
                errors.append(f"Fixture {key} is not a valid identity")
    for scenario in manifest["scenarios"]:
        scenario_id = scenario["id"]
        for implementation in ("browserdock", "seleniumbase"):
            path = Path(directory) / f"{implementation}-{scenario_id}.json"
            try:
                result = json.loads(path.read_text(encoding="utf-8-sig"))
                if result.get("scenarioId") != scenario_id:
                    errors.append(f"{path.name}: wrong scenario identity")
                expected_implementation = "BrowserDock" if implementation == "browserdock" else "SeleniumBase"
                if result.get("implementation") != expected_implementation:
                    errors.append(f"{path.name}: wrong implementation identity")
                for required in ("passed", "cleanupPassed", "chromePreserved"):
                    if result.get(required) is not True:
                        errors.append(f"{path.name}: {required} must be true")
                if scenario_id == "SB-02" and result.get("sessionChanged") is not True:
                    errors.append(f"{path.name}: session was not replaced")
                if result.get("observed") != scenario["expected"]:
                    errors.append(f"{path.name}: observation differs from manifest")
                if scenario.get("suite") == "interactions":
                    if result.get("sessionChanged") is not False:
                        errors.append(f"{path.name}: interactions must retain their session")
                    for key in ("browserdockCommit", "seleniumBaseVersion", "seleniumBaseCommit", "chromeSha256", "vendorDriverSha256", "framework", "project", "executionEnvironment", "fixtureId", "chromeVersion", "os", "architecture"):
                        if not metadata or not metadata.get(key) or result.get(key) != metadata[key]:
                            errors.append(f"{path.name}: missing or mismatched {key}")
                    if metadata and result.get("driverSha256") != metadata.get("vendorDriverSha256"):
                        errors.append(f"{path.name}: executed driver differs from pinned vendor binary")
                    if not metadata or result.get("browserVersion") != metadata.get("chromeVersion"):
                        errors.append(f"{path.name}: executed Chrome version differs from fixture")
            except (OSError, ValueError, TypeError, AttributeError) as error:
                errors.append(f"{path.name}: unavailable or invalid: {error}")
    return errors
