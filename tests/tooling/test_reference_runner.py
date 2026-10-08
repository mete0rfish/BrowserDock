import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch


REFERENCE = Path(__file__).resolve().parents[1] / "seleniumbase-reference"
spec = importlib.util.spec_from_file_location("reference_runner", REFERENCE / "run.py")
runner = importlib.util.module_from_spec(spec)
with patch.object(sys, "path", [str(REFERENCE), *sys.path]):
    spec.loader.exec_module(runner)


class BuildReached(Exception):
    pass


class ReferenceRunnerTests(unittest.TestCase):
    def test_windows_reads_literal_binary_metadata_without_launching_chrome(self):
        binary = Path("Chrome's $folder [literal] 한글") / "chrome.exe"
        with patch.object(runner.sys, "platform", "win32"), patch.object(runner, "windows_product_version", return_value="154.0.8037.57") as metadata, patch.object(runner.subprocess, "check_output") as execute:
            self.assertEqual(runner.read_chrome_version(binary), "154.0.8037.57")
        metadata.assert_called_once_with(binary)
        execute.assert_not_called()

    def test_linux_reads_cli_version(self):
        binary = Path("/controlled/chromium")
        with patch.object(runner.sys, "platform", "linux"), patch.object(runner.subprocess, "check_output", return_value="Chromium 154.0.8037.57") as execute:
            self.assertEqual(runner.read_chrome_version(binary), "154.0.8037.57")
        execute.assert_called_once_with([str(binary), "--version"], text=True, timeout=10)

    def test_missing_windows_metadata_fails_without_cli_fallback(self):
        with patch.object(runner.sys, "platform", "win32"), patch.object(runner, "windows_product_version", return_value="") as metadata, patch.object(runner.subprocess, "check_output") as execute:
            with self.assertRaisesRegex(RuntimeError, "Chrome binary did not return"):
                runner.read_chrome_version(Path("chrome.exe"))
        self.assertEqual(metadata.call_count, 1)
        execute.assert_not_called()

    def test_windows_metadata_failure_is_not_retried_by_launching_chrome(self):
        for error in (FileNotFoundError("metadata file missing"), OSError("metadata resource unavailable")):
            with self.subTest(error=type(error).__name__), patch.object(runner.sys, "platform", "win32"), patch.object(runner, "windows_product_version", side_effect=error) as metadata, patch.object(runner.subprocess, "check_output") as execute:
                with self.assertRaises(type(error)):
                    runner.read_chrome_version(Path("chrome.exe"))
                self.assertEqual(metadata.call_count, 1)
                execute.assert_not_called()

    def check_preflight(self, suite, chrome_version):
        with tempfile.TemporaryDirectory() as temporary:
            # Windows may return a short (8.3) temp path; main resolves its
            # binary arguments before invoking processes or recording metadata.
            root = Path(temporary).resolve()
            chrome = root / "chrome.exe"; chrome.write_bytes(b"controlled binary")
            driver = root / "chromedriver.exe"; driver.write_bytes(b"controlled binary")
            output = root / "results"
            calls = []

            def read(command, **kwargs):
                calls.append(command)
                if command == ["git", "status", "--short"]:
                    return ""
                if command == ["git", "rev-parse", "HEAD"]:
                    return "a" * 40
                if command == [str(driver), "--version"]:
                    return "ChromeDriver 154.0.8037.57"
                if command == [sys.executable, "-m", "pip", "freeze"]:
                    return "controlled==1.0"
                raise AssertionError(f"Unexpected process: {command}")

            arguments = ["run.py", "--suite", suite, "--chrome", str(chrome), "--driver", str(driver), "--results", str(output)]
            with patch.object(runner.sys, "platform", "win32"), patch.object(runner.sys, "getwindowsversion", return_value=SimpleNamespace(build=22631), create=True), patch.object(runner.platform, "platform", return_value="Windows-controlled"), patch.object(runner.platform, "machine", return_value="AMD64"), patch.object(runner.sys, "argv", arguments), patch.object(runner.subprocess, "check_output", side_effect=read), patch.object(runner, "windows_product_version", return_value=chrome_version) as metadata, patch.object(runner, "invoke", side_effect=BuildReached) as build:
                if chrome_version == "154.0.8037.57":
                    with self.assertRaises(BuildReached):
                        runner.main()
                    self.assertEqual(build.call_args.args[0], ["dotnet", "restore", "BrowserDock.slnx"])
                    fixture = json.loads((output / "fixture.json").read_text())
                    self.assertEqual(fixture["chromeVersion"], chrome_version)
                    self.assertEqual(fixture["driverVersion"], chrome_version)
                else:
                    with self.assertRaisesRegex(RuntimeError, "same four-part version"):
                        runner.main()
                    build.assert_not_called()
            self.assertNotIn([str(chrome), "--version"], calls)
            metadata.assert_called_once_with(chrome)

    def test_both_windows_suites_reach_build_with_matching_metadata(self):
        for suite in ("baseline", "interactions"):
            with self.subTest(suite=suite):
                self.check_preflight(suite, "154.0.8037.57")

    def test_both_windows_suites_reject_mismatched_metadata(self):
        for suite in ("baseline", "interactions"):
            with self.subTest(suite=suite):
                self.check_preflight(suite, "154.0.8037.58")

    @unittest.skipUnless(sys.platform == "win32", "Windows PE version metadata")
    def test_actual_windows_metadata_preserves_literal_paths(self):
        # Read a known PE file without executing it; no browser fixture needed.
        with tempfile.TemporaryDirectory() as temporary:
            copied = Path(temporary) / "quote' $value [literal] 한글.exe"
            shutil.copyfile(sys.executable, copied)
            expected = runner.windows_product_version(Path(sys.executable))
            self.assertRegex(expected, r"\d+\.\d+")
            self.assertEqual(runner.windows_product_version(copied), expected)

    @unittest.skipUnless(sys.platform == "win32", "Windows PE version metadata")
    def test_actual_windows_metadata_rejects_missing_or_unversioned_files(self):
        with tempfile.TemporaryDirectory() as temporary:
            binary = Path(temporary) / "missing.exe"
            with self.assertRaises(OSError):
                runner.windows_product_version(binary)
            binary.write_bytes(b"not a versioned PE")
            with self.assertRaises(OSError):
                runner.windows_product_version(binary)


if __name__ == "__main__":
    unittest.main()
