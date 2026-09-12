import contextlib
import importlib.util
import io
from pathlib import Path
import tempfile
import unittest
from zipfile import ZipFile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / "scripts" / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


packages = load("check-packages")
runs = load("check-release-run")


class ReleaseChecks(unittest.TestCase):
    def test_accepts_exact_successful_windows_run(self):
        runs.validate(self.run_record(), "owner/repo", "a" * 40)

    @staticmethod
    def run_record():
        return dict(status="completed", conclusion="success", head_sha="a" * 40,
                    event="workflow_dispatch", path=".github/workflows/windows-browser.yml",
                    head_repository={"full_name": "owner/repo"})

    def test_rejects_wrong_failed_or_untrusted_evidence(self):
        for field, value in (("status", "in_progress"), ("conclusion", "failure"),
                             ("head_sha", "b" * 40), ("event", "pull_request"),
                             ("path", ".github/workflows/ci.yml"),
                             ("head_repository", {"full_name": "fork/repo"}),
                             ("head_repository", None)):
            with self.subTest(field=field, value=value):
                run = self.run_record()
                run[field] = value
                with self.assertRaises(ValueError):
                    runs.validate(run, "owner/repo", "a" * 40)

    def make_packages(self, directory, fault=None):
        version = ET.parse(ROOT / "build/Version.props").findtext(".//BrowserDockVersion")
        for name in ("BrowserDock", "BrowserDock.Legacy"):
            groups = "".join(f'<group targetFramework="{tfm}"><dependency id="BrowserDock" version="{version if fault != "dependency" else "0.0.1"}" /></group>' for tfm in packages.TFMS)
            spec = f'<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{name}</id><version>{version}</version><readme>README.md</readme><dependencies>{groups}</dependencies></metadata></package>'
            stem = directory / f"{name}.{version}"
            with ZipFile(str(stem) + ".nupkg", "w") as archive:
                archive.writestr(f"{name}.nuspec", spec)
                archive.writestr("README.md", "[bad](docs/missing.md)" if fault == "readme" else "Experimental")
                archive.writestr("THIRD-PARTY-NOTICES.md", "notices")
                for tfm in packages.TFMS:
                    if fault != "missing-tfm" or tfm != "net481":
                        archive.writestr(f"lib/{tfm}/{name}.dll", b"fixture")
                if fault == "binary":
                    archive.writestr("chrome.exe", b"fixture")
            with ZipFile(str(stem) + ".snupkg", "w") as archive:
                for tfm in packages.TFMS:
                    if fault != "symbols" or tfm != "net481":
                        archive.writestr(f"lib/{tfm}/{name}.pdb", b"fixture")

    def test_accepts_local_candidate_structure(self):
        with tempfile.TemporaryDirectory() as temp, contextlib.redirect_stdout(io.StringIO()):
            directory = Path(temp)
            self.make_packages(directory)
            packages.check(directory)

    def test_rejects_broken_candidates(self):
        for fault in ("dependency", "readme", "missing-tfm", "binary", "symbols"):
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as temp, contextlib.redirect_stdout(io.StringIO()):
                directory = Path(temp)
                self.make_packages(directory, fault)
                with self.assertRaises(AssertionError):
                    packages.check(directory)

    def test_local_candidate_cannot_pass_release_gate(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            self.make_packages(directory)
            with self.assertRaises(AssertionError):
                packages.check(directory, release=True)


if __name__ == "__main__":
    unittest.main()
