"""Require successful Windows acceptance for the exact release commit."""
import json
import os
import re
from urllib.request import Request, urlopen

def validate(run, repository, commit):
    checks = {
        "successful completed run": run.get("status") == "completed" and run.get("conclusion") == "success",
        "same commit": run.get("head_sha") == commit,
        "manual Windows workflow": run.get("event") == "workflow_dispatch" and run.get("path") == ".github/workflows/windows-browser.yml",
        "same repository": (run.get("head_repository") or {}).get("full_name") == repository,
    }
    for description, passed in checks.items():
        if not passed:
            raise ValueError(f"Windows evidence rejected: {description}")


def main():
    run_id = os.environ["WINDOWS_RUN_ID"]
    if not re.fullmatch(r"[0-9]+", run_id):
        raise SystemExit("A numeric Windows acceptance run ID is required")
    repository = os.environ["GITHUB_REPOSITORY"]
    request = Request(
        f"https://api.github.com/repos/{repository}/actions/runs/{run_id}",
        headers={"Authorization": f"Bearer {os.environ['GH_TOKEN']}",
                 "Accept": "application/vnd.github+json"},
    )
    with urlopen(request, timeout=30) as response:
        run = json.load(response)
    validate(run, repository, os.environ["GITHUB_SHA"])
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"artifact=windows-browser-{run_id}-{run['run_attempt']}\n")
    print(f"Verified Windows acceptance run {run_id} for {run['head_sha']}")


if __name__ == "__main__":
    main()
