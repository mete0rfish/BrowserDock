"""One fixture host, sequential fresh browsers, separate artifacts per repetition."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import re
import subprocess
import sys
import threading
import urllib.request

from compare import compare

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent


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
    parser.add_argument("--framework", choices=("net8.0", "net10.0"), default="net10.0")
    args = parser.parse_args()
    if sys.platform != "win32" or sys.getwindowsversion().build < 22000:
        parser.error("Use Windows 11 x64 with an interactive desktop")
    if args.repeat < 1:
        parser.error("--repeat must be positive")
    chrome, driver = args.chrome.resolve(strict=True), args.driver.resolve(strict=True)
    output = args.results.resolve()
    output.mkdir(parents=True, exist_ok=False)
    manifest = json.loads((HERE / "scenarios.json").read_text(encoding="utf-8"))
    version_text = subprocess.check_output([str(driver), "--version"], text=True, timeout=10)
    version = re.search(r"\d+\.\d+\.\d+\.\d+", version_text)
    if version is None:
        raise RuntimeError("Driver did not return a four-part version")
    metadata = {"manifest": manifest, "repeat": args.repeat, "framework": args.framework,
                "browserdockCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                "workingTree": subprocess.check_output(["git", "status", "--short"], cwd=ROOT, text=True),
                "driverVersion": version.group(), "python": sys.version,
                "chromeSha256": hashlib.sha256(chrome.read_bytes()).hexdigest(),
                "vendorDriverSha256": hashlib.sha256(driver.read_bytes()).hexdigest(),
                "dependencies": subprocess.check_output([sys.executable, "-m", "pip", "freeze"], text=True)}
    (output / "fixture.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    environment = dict(os.environ, BROWSERDOCK_CHROME=str(chrome), BROWSERDOCK_DRIVER=str(driver),
                       BROWSERDOCK_REFERENCE_DRIVER_VERSION=version.group())
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
                commands = [
                    ([sys.executable, "-m", "pytest", str(HERE / "test_reference.py"), "-v",
                      f"--junitxml={run / 'seleniumbase.xml'}"], "seleniumbase"),
                    (["dotnet", "test", "tests/BrowserDock.Tests", "-f", args.framework, "--no-build", "--no-restore",
                      "--filter", "TestCategory=Reference", "--logger", "trx;LogFileName=browserdock.trx",
                      "--results-directory", str(run)], "browserdock")]
                for command, name in commands:
                    code = invoke(command, environment, run / f"{name}.log")
                    if code:
                        errors.append(f"run-{repeat:02}/{name}: exit {code}")
                errors.extend(f"run-{repeat:02}/{error}" for error in compare(run, manifest))
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
