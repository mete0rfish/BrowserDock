import copy
import json
from pathlib import Path
import tempfile
import unittest

from compare import compare


class ComparisonTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.manifest = json.loads(Path(__file__).with_name("scenarios.json").read_text(encoding="utf-8"))
        for scenario in self.manifest["scenarios"]:
            for implementation in ("browserdock", "seleniumbase"):
                report = {"scenarioId": scenario["id"], "passed": True, "cleanupPassed": True,
                          "chromePreserved": True, "sessionChanged": True, "observed": scenario["expected"]}
                report["implementation"] = "BrowserDock" if implementation == "browserdock" else "SeleniumBase"
                (self.directory / f"{implementation}-{scenario['id']}.json").write_text(json.dumps(report), encoding="utf-8")

    def test_complete_matching_runs_pass(self):
        self.assertEqual(compare(self.directory, self.manifest), [])

    def test_missing_run_does_not_turn_into_a_successful_comparison(self):
        (self.directory / "seleniumbase-SB-01.json").unlink()
        self.assertTrue(compare(self.directory, self.manifest))

    def test_matching_but_wrong_observations_fail_against_manifest(self):
        for implementation in ("browserdock", "seleniumbase"):
            path = self.directory / f"{implementation}-SB-01.json"
            report = json.loads(path.read_text())
            report["observed"]["button"] = "both are wrong"
            path.write_text(json.dumps(report))
        self.assertEqual(len(compare(self.directory, self.manifest)), 2)

    def test_failure_cleanup_identity_and_unchanged_session_are_rejected(self):
        path = self.directory / "browserdock-SB-02.json"
        original = json.loads(path.read_text())
        for field, value in (("passed", False), ("cleanupPassed", False), ("chromePreserved", False),
                             ("sessionChanged", False), ("scenarioId", "wrong"), ("implementation", "wrong")):
            with self.subTest(field=field):
                report = copy.deepcopy(original)
                report[field] = value
                path.write_text(json.dumps(report))
                self.assertTrue(compare(self.directory, self.manifest))

    def test_malformed_artifact_is_a_failure(self):
        (self.directory / "browserdock-SB-01.json").write_text("{broken")
        self.assertTrue(compare(self.directory, self.manifest))


if __name__ == "__main__":
    unittest.main()
