"""Validate both sides against the manifest, including missing/failed runs."""
import json
from pathlib import Path


def compare(directory, manifest):
    errors = []
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
            except (OSError, ValueError, TypeError, AttributeError) as error:
                errors.append(f"{path.name}: unavailable or invalid: {error}")
    return errors
