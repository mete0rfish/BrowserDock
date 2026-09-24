# Pre-publication repository audit — 2026-09-24

## Status after remediation

Main protection requires five Common contracts checks plus Secret scan: six required checks
in total. Pull requests, administrator enforcement, and force-push/deletion restrictions are
configured. Actions require full SHA pins and read-only default permissions; Dependabot
vulnerability alerts are enabled. The windows-browser and release environments allow main only.

The current private-repository plan does not support required environment reviewers; the API
returned 422. Secret scanning also returned 422 as unavailable for this repository. Enabling
private vulnerability reporting returned 404, so email remains the reporting route.
`PUBLIC_RELEASE_ENABLED=false` explicitly disables package publication. The user put Windows
x64 runner preparation and ARM setup on hold and chose to retain private visibility and the
publication block.

At the user's request, naver.com author/committer addresses in ten remote branches and local
refs were replaced with the GitHub noreply address. File trees were identical at every changed
ref tip. The remote update used atomic push with each ref's exact previous SHA as its lease.
Main protection was temporarily adjusted, restored, and verified as protected afterward.
Local user.email also uses the noreply address. The rewritten main baseline is
`09ba18d566c26c784f252898fdf42fa694b7c4ad`. Rewriting changed commit SHAs and signatures;
existing checkouts need synchronization with the new history. Working files and paused ARM
changes were preserved.

The original history is backed up locally at `.artifacts/publication-audit/before-email-rewrite.bundle`.
The same directory contains `email-rewrite-plan.json` and `email-rewrite-applied.json`.
These backups retain the old address and must not be published. Rewriting live branches does
not erase GitHub PR references, cached views, old clones, or separate wiki history. Follow
[GitHub's history-removal guidance](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository)
for retained references; Support eligibility is not guaranteed.

All five checks passed in the rewritten main's [Common contracts run](https://github.com/mete0rfish/BrowserDock/actions/runs/35965425139).
At PR #20 head `b50658fb64d7a46ea4d90c21608969babdf52c65`, all five
[Common contracts jobs](https://github.com/mete0rfish/BrowserDock/actions/runs/35966913501) and
[Secret scan](https://github.com/mete0rfish/BrowserDock/actions/runs/35966913502) passed.
These links record those commits; later documentation changes need their own CI results.
The following initial-audit tables and counts are historical observations before remediation.

## Initial scope and method

- After `git fetch origin --tags`, inspected 30 local refs, 36 reachable commits, 253 unique
  file blobs, and 92 working files.
- Initial HEAD/main: `0c71d756b0f5265a8ad1e922ea80ab429a3b19f0`. Local changes included
  license/documentation updates and the paused ARM experiment.
- Checked patterns for private keys, GitHub/Slack/Google/AWS keys, credentials in URLs,
  password/token assignments, email addresses, and personal absolute paths. Inspected
  filenames and binary indicators too.
- Location-only results, without matched values, are in local `.artifacts/publication-audit/scan.json`.
  Commit identities are in `commit-identities.txt` beside it. `.artifacts/` is ignored by Git.
- Scope covered objects reachable from fetched branches/tags and local refs. Deleted remote refs,
  the separate wiki, remote LFS objects, GitHub artifacts/logs/attachments, unreachable objects,
  and server-side data were outside that initial scan.

## Initial file and history findings

| Area | Initial result | Follow-up |
|---|---|---|
| Known secret patterns | No matches | Rescan the publication commit; pattern checks do not prove absence of secrets |
| Binary/sensitive filenames | No NUL-containing blobs or exe/dll/archive/key/profile/cookie/TRX candidates | Inspect actual packages and CI artifacts separately |
| Email | Owner-approved reporting address in current documents | Review the separate naver.com commit identity; subsequently rewritten as described above |
| Personal paths | Three personal SSD paths in the untracked ARM guide | Replaced with `/Volumes/ExternalSSD/` examples; ARM implementation remains on hold |
| Provenance/distribution | Reviewed source/script URLs, copyright/copy markers, notices, and pinned reference-runner version | These records cannot prove all source rights; inspect distributed files and transitive notices |

The initial identity list contained GitHub addresses and a separate naver.com address.
The raw list remains in the ignored local evidence file. No history was changed during
that initial read-only audit; the subsequently authorized rewrite is described above.

## Initial GitHub settings

Repository inspected through the API: `mete0rfish/BrowserDock`.

| Area | Initial observation | Required action at that time |
|---|---|---|
| Visibility | PRIVATE, default branch main | Decide visibility after source-publication criteria |
| Remote license | No licenseInfo | Review and submit local MIT/documentation changes |
| Main protection | protected=false | Restrict force pushes/deletion and require CI |
| Rulesets | None | Configure branch protection or an active ruleset |
| Environments | None | Create windows-browser/release, ref restrictions, and approvals |
| Self-hosted runners | Zero | Prepare isolated interactive Windows 11 x64; isolation could not be verified |
| Actions defaults | read, PR approval permission false | Retain |
| Actions policy | all, sha_pinning_required=false | Existing YAML pins SHAs; consider repository enforcement |
| Security features | security_and_analysis=null | Check availability/activation; null alone does not mean disabled |
| Private vulnerability reports | Earlier API returned 404 | Activation unverified; use SECURITY email |
| Actions variables | None | Missing PUBLIC_RELEASE_ENABLED prevents publication; do not enable prematurely |

The [pre-rewrite Common contracts run](https://github.com/mete0rfish/BrowserDock/actions/runs/35948349911)
passed on its baseline. Its five job names were:

- `common (ubuntu-latest, net8.0)`
- `common (ubuntu-latest, net10.0)`
- `common (windows-latest, net8.0)`
- `common (windows-latest, net10.0)`
- `framework481`

That historical CI run does not validate subsequent local changes. Secret scan was added
as a sixth required check during follow-up remediation.

## Initial workflow review

- Normal PRs run common tests on hosted runners. No `pull_request_target` trigger exists.
- The browser workflow is manual, uses a dedicated self-hosted Windows X64 label, and references
  windows-browser. Neither its environment nor runner existed at the initial audit.
- All Action references in the three workflows then present used full SHAs, and checkout did not persist credentials.
- Only the publish job receives OIDC write permission and uses the release environment.
  Publication requires explicit input and PUBLIC_RELEASE_ENABLED=true.
- Release checks require same-commit Stress evidence, a clean checkout, required TRX results,
  and package metadata. Static inspection is not a successful release execution.
- Use a dedicated browser runner without personal accounts/profiles or publication credentials,
  rebuildable after untrusted execution. No runner was available to verify isolation.

## Follow-up findings

- Rewritten main `09ba18d` passed all five common CI checks.
- A fresh fetch of 23 PR refs covered 67 reachable commits. Merged PR heads #1–#12 and #18
  retain the old identity in their history. Open PR #13–#17 head/merge refs do not contain it.
- The separate wiki master contains two commits, neither with that email.
- Details are in local `.artifacts/publication-audit/remaining-ref-audit.json`; the unsent
  Support draft is `github-support-draft.md` beside it. Normal pushes cannot remove GitHub-managed
  PR references/caches; Support must determine whether removal is available.
- Added a SHA-pinned Gitleaks workflow for the private repository. It runs after pushes/PRs
  and is not server-side push protection or environment approval. The publication gate remains false.
  The recorded PR scan covered its two changed commits, not all historical commits.
- Compared restored NuGet manifests and included license texts for direct/transitive dependencies.
  The [inventory](dependency-inventory.md) records versions, evidence, and distribution boundaries;
  THIRD-PARTY-NOTICES was updated. In a separate checkout excluding ARM changes, both nupkg/snupkg
  pairs built and passed `check-packages.py --release`. Inspection verified only the expected
  own DLLs for three TFMs, plus LICENSE/README/notices/symbols/author/MIT metadata. This local
  check of then-uncommitted documentation does not replace final Source Link or Windows consumer execution.

## Deferred or external work

1. Windows x64 runner preparation/isolation: paused by the user. ARM work remains paused too.
2. Environment reviewers/GitHub secret scanning: unavailable under current private-repository conditions;
   retain publication blocking and alternative CI checks.
3. Old email in PR refs/caches: Support draft prepared but not sent; complete deletion is not claimed.
4. Final candidate verification, including Windows Stress and package consumers, is a separate stage.
   Reviewing provenance notices does not guarantee ownership of all upstream code.
