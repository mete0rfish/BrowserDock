"""Require successful Windows acceptance for the exact release commit."""
import json
import os
import re
from urllib.request import Request, urlopen

def validate(run, repository, commit):
    checks = {
        "successful completed run": run.get("status") == "completed" and run.get("conclusion") == "success",
        # pull_request_target head_sha identifies the trusted base workflow, not
        # the explicit merge commit checked out for this acceptance run. The
        # trusted workflow's run name binds the requested commit and stress label;
        # publish separately verifies fixture.Commit and every required TRX.
        "same requested commit and stress label": run.get("display_title") == f"Windows 11 acceptance {commit} ci:windows11:stress",
        "label-triggered trusted Windows workflow": run.get("event") == "pull_request_target" and run.get("path") == ".github/workflows/windows-browser.yml",
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
    commit = os.environ["RELEASE_COMMIT"]
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise SystemExit("A merged release commit is required")
    validate(run, repository, commit)
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"artifact=windows-browser-{run_id}-{run['run_attempt']}\n")
    print(f"Verified Windows acceptance run {run_id} for {commit}")


if __name__ == "__main__":
    main()
