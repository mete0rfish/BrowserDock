"""One fixture host, sequential fresh browsers, separate artifacts per repetition."""
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
import platform
from pathlib import Path
import queue
import re
import subprocess
import sys
import threading
import urllib.request
import uuid

from compare import compare

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent


def windows_product_version(binary):
    # Read the PE resource directly. Starting PowerShell can exceed the preflight
    # budget on a cold CI runner; no process is needed for file metadata.
    api = ctypes.WinDLL("version", use_last_error=True)
    api.GetFileVersionInfoSizeW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(wintypes.DWORD)]
    api.GetFileVersionInfoSizeW.restype = wintypes.DWORD
    api.GetFileVersionInfoW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID]
    api.GetFileVersionInfoW.restype = wintypes.BOOL
    api.VerQueryValueW.argtypes = [wintypes.LPCVOID, wintypes.LPCWSTR,
                                 ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(wintypes.UINT)]
    api.VerQueryValueW.restype = wintypes.BOOL
    path = str(binary)
    size = api.GetFileVersionInfoSizeW(path, None)
    if not size:
        raise ctypes.WinError(ctypes.get_last_error())
    resource = ctypes.create_string_buffer(size)
    if not api.GetFileVersionInfoW(path, 0, size, resource):
        raise ctypes.WinError(ctypes.get_last_error())
    value = ctypes.c_void_p()
    length = wintypes.UINT()
    translations = []
    if api.VerQueryValueW(resource, r"\VarFileInfo\Translation", ctypes.byref(value), ctypes.byref(length)):
        words = ctypes.cast(value, ctypes.POINTER(wintypes.WORD))
        translations = [(words[i], words[i + 1]) for i in range(0, length.value // 2 - 1, 2)]
    for language, codepage in [*translations, (0x0409, 0x04b0), (0x0409, 0x04e4)]:
        key = rf"\StringFileInfo\{language:04x}{codepage:04x}\ProductVersion"
        if api.VerQueryValueW(resource, key, ctypes.byref(value), ctypes.byref(length)) and length.value:
            return ctypes.wstring_at(value, length.value).rstrip("\0").strip()
    return ""


def read_chrome_version(binary):
    if sys.platform == "win32":
        text = windows_product_version(binary)
    else:
        text = subprocess.check_output([str(binary), "--version"], text=True, timeout=10)
    version = re.search(r"\d+\.\d+\.\d+\.\d+", text)
    if version is None:
        raise RuntimeError("Chrome binary did not return a four-part version")
    return version.group()


def invoke(command, environment, logfile, timeout=600):
    with logfile.open("w", encoding="utf-8") as stream:
        process = subprocess.Popen(command, cwd=ROOT, env=environment, stdout=stream, stderr=subprocess.STDOUT)
        try:
            return process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            import psutil
            try:
                children = psutil.Process(process.pid).children(recursive=True)
            except psutil.NoSuchProcess:
                children = []
            for child in reversed(children):
                try:
                    child.kill()
                except psutil.NoSuchProcess:
                    pass
            process.kill()
            process.wait(timeout=10)
            psutil.wait_procs(children, timeout=10)
            raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--chrome", required=True, type=Path)
    parser.add_argument("--driver", required=True, type=Path)
    parser.add_argument("--results", required=True, type=Path)
    parser.add_argument("--repeat", type=int, default=3)
    parser.add_argument("--framework", choices=("net481", "net8.0", "net10.0"), default="net10.0")
    parser.add_argument("--suite", choices=("baseline", "interactions"), default="baseline")
    parser.add_argument("--project", choices=("core", "legacy"), default="core")
    parser.add_argument("--linux-investigation", action="store_true", help="SB-05 local headless investigation, not Windows acceptance")
    args = parser.parse_args()
    if args.linux_investigation:
        if not sys.platform.startswith("linux") or args.suite != "interactions" or args.framework != "net10.0":
            parser.error("Linux investigation requires --suite interactions --framework net10.0 on Linux")
    elif sys.platform != "win32" or sys.getwindowsversion().build < 22000:
        parser.error("Use Windows 11 x64 with an interactive desktop")
    if args.suite == "baseline" and args.project != "core":
        parser.error("The baseline reference tests currently target Core")
    if args.project == "core" and args.framework == "net481":
        parser.error("Core does not target net481")
    if args.repeat < 1:
        parser.error("--repeat must be positive")
    chrome, driver = args.chrome.resolve(strict=True), args.driver.resolve(strict=True)
    output = args.results.resolve()
    output.mkdir(parents=True, exist_ok=False)
    manifest = json.loads((HERE / "scenarios.json").read_text(encoding="utf-8"))
    manifest["scenarios"] = [s for s in manifest["scenarios"] if s.get("suite", "baseline") == args.suite]
    if not manifest["scenarios"]:
        raise RuntimeError("Selected comparison suite is empty")
    working_tree = subprocess.check_output(["git", "status", "--short"], cwd=ROOT, text=True)
    if args.suite == "interactions" and working_tree:
        raise RuntimeError("Commit the candidate before recording same-commit interaction evidence")
    version_text = subprocess.check_output([str(driver), "--version"], text=True, timeout=10)
    version = re.search(r"\d+\.\d+\.\d+\.\d+", version_text)
    if version is None:
        raise RuntimeError("Driver did not return a four-part version")
    chrome_version = read_chrome_version(chrome)
    if chrome_version != version.group():
        raise RuntimeError("Supply Chrome and ChromeDriver with the same four-part version")
    metadata = {"manifest": manifest, "repeat": args.repeat, "framework": args.framework, "project": args.project,
                "executionEnvironment": "linux-investigation" if args.linux_investigation else "windows-acceptance",
                "fixtureId": str(uuid.uuid4()), "seleniumBaseVersion": manifest["seleniumBaseVersion"], "seleniumBaseCommit": manifest["seleniumBaseCommit"],
                "browserdockCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                "workingTree": working_tree,
                "driverVersion": version.group(), "chromeVersion": chrome_version, "python": sys.version,
                "os": platform.platform(), "architecture": platform.machine(),
                "chromeSha256": hashlib.sha256(chrome.read_bytes()).hexdigest(),
                "vendorDriverSha256": hashlib.sha256(driver.read_bytes()).hexdigest(),
                "dependencies": subprocess.check_output([sys.executable, "-m", "pip", "freeze"], text=True)}
    (output / "fixture.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    (output / "dependencies.lock.txt").write_text(metadata["dependencies"], encoding="utf-8")
    environment = dict(os.environ, BROWSERDOCK_CHROME=str(chrome), BROWSERDOCK_DRIVER=str(driver),
                       BROWSERDOCK_REFERENCE_DRIVER_VERSION=version.group(), BROWSERDOCK_REFERENCE_SUITE=args.suite,
                       BROWSERDOCK_REFERENCE_FACADE=args.project, BROWSERDOCK_REFERENCE_INVESTIGATION="linux" if args.linux_investigation else "",
                       BROWSERDOCK_REFERENCE_MANIFEST=str(HERE / "scenarios.json"), BROWSERDOCK_REFERENCE_METADATA=str(output / "fixture.json"))
    for command, name in ((["dotnet", "restore", "BrowserDock.slnx"], "restore"),
                          (["dotnet", "build", "BrowserDock.slnx", "--no-restore", "-m:1"], "build")):
        if invoke(command, environment, output / f"{name}.log"):
            raise RuntimeError(f"{name} failed; see {output}")
    host = ROOT / "tests/BrowserDock.FixtureHost/bin/Debug/net10.0/BrowserDock.FixtureHost.dll"
    lines = queue.Queue()
    errors = []
    base = None
    with (output / "fixture-stderr.log").open("w", encoding="utf-8") as stderr:
        process = subprocess.Popen(["dotnet", str(host)], cwd=ROOT, stdout=subprocess.PIPE,
                                   stderr=stderr, text=True, env=environment)
        def drain():
            with (output / "fixture-stdout.log").open("w", encoding="utf-8") as log:
                for line in process.stdout:
                    log.write(line)
                    if line.startswith("BROWSERDOCK_FIXTURE_URL="):
                        lines.put(line.strip().split("=", 1)[1])
            lines.put(None)
        reader = threading.Thread(target=drain, daemon=True)
        reader.start()
        try:
            base = lines.get(timeout=20)
            if not base:
                raise RuntimeError("Fixture host exited before readiness")
            environment["BROWSERDOCK_REFERENCE_URL"] = base
            for repeat in range(1, args.repeat + 1):
                run = output / f"run-{repeat:02}"
                run.mkdir()
                environment["BROWSERDOCK_REFERENCE_RESULTS"] = str(run)
                if args.linux_investigation:
                    dotnet_command = ["dotnet", "run", "--project", "tests/BrowserDock.ReferenceInvestigation", "--no-build", "--no-restore"]
                else:
                    project = "BrowserDock.Tests" if args.project == "core" else "BrowserDock.FrameworkTests"
                    category = "Reference" if args.suite == "baseline" else "ReferenceInteraction"
                    dotnet_command = ["dotnet", "test", "tests/" + project, "-f", args.framework, "--no-build", "--no-restore", "--filter", "TestCategory=" + category,
                                      "--logger", "trx;LogFileName=browserdock.trx", "--results-directory", str(run)]
                commands = [
                    ([sys.executable, "-m", "pytest", str(HERE / "test_reference.py"), "-v",
                      f"--junitxml={run / 'seleniumbase.xml'}"], "seleniumbase"),
                    (dotnet_command, "browserdock")]
                for command, name in commands:
                    code = invoke(command, environment, run / f"{name}.log")
                    if code:
                        errors.append(f"run-{repeat:02}/{name}: exit {code}")
                errors.extend(f"run-{repeat:02}/{error}" for error in compare(run, manifest, metadata))
        except Exception as error:
            errors.append(f"Runner failed: {type(error).__name__}: {error}")
        finally:
            if process.poll() is None:
                try:
                    if base:
                        request = urllib.request.Request(base + "/control/stop", data=b"", method="POST")
                        with urllib.request.urlopen(request, timeout=2):
                            pass
                    process.wait(timeout=10)
                except Exception:
                    process.kill()
                    process.wait(timeout=5)
            reader.join(timeout=5)
            process.stdout.close()
    (output / "comparison.json").write_text(json.dumps({"passed": not errors, "errors": errors}, indent=2), encoding="utf-8")
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    print(f"All reference comparisons passed: {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
