# BrowserDock open-source readiness guide

First written: 2026-09-11. Status updated: 2026-09-24. The status table reflects repository files and existing execution records. Follow-up work added the README and usage guide, contribution/security/conduct policies, issue/PR templates, Dependabot, SHA-pinned CI with minimal permissions, shared alpha versioning, symbol packages, and release checks. Follow the [release procedure](releasing.md) for remaining setup. The remote is `mete0rfish/BrowserDock`. On 2026-09-24, the owner confirmed MIT, copyright holder/author mete0rfish, and sungwonyoon326@gmail.com for private security and conduct reports. Repository visibility and NuGet publication have not changed. A limited secret-pattern scan of local Git history does not constitute a comprehensive privacy, permissions, or secrets audit.

The recommended sequence is **experimental source publication → a NuGet preview after Windows verification → a stable release with a verified support matrix**. Source publication need not wait for every feature, but users need accurate features, limitations, and verification status.

## 1. Project identity

Suggested introduction:

> BrowserDock is an experimental .NET library for managing a separately launched Chrome process, independent CDP connections, and replaceable WebDriver sessions on Windows. Its lifecycle design is inspired by SeleniumBase UC Mode.

Describe three main capabilities:

- Disconnect WebDriver while preserving Chrome, then attach a new session.
- Invalidate old leases/elements when the attachment changes and reacquire explicitly.
- Offer modern .NET APIs alongside a C# 7.3/.NET Framework 4.8.1 Legacy API.

Separate inspiration from source provenance. `THIRD-PARTY-NOTICES.md` records an independent implementation without copied or translated GPL C# project code. That statement starts a provenance review; it does not replace a full history audit. Do not describe this as an official SeleniumBase .NET port or use upstream logos to imply affiliation. Detection evasion and CAPTCHA success remain outside support guarantees.

## 2. Current status and priorities

The 2026-09-24 [publication audit and actions](publication-audit.md) applied main protection, required CI, main-only environments, Action SHA enforcement, and an email-history rewrite. Environment reviewers and GitHub secret scanning are unavailable under the current account/repository conditions. A dedicated Windows runner remains pending. Keep `PUBLIC_RELEASE_ENABLED=false`.

| Area | Recorded status | Remaining action |
|---|---|---|
| License | MIT LICENSE, copyright holder/author mete0rfish, package metadata updated | Verify the same LICENSE in release artifacts |
| Provenance/dependencies | Direct dependency notices aligned with csproj and requirements.txt | Review transitive dependencies and actual distribution notices |
| Documentation | English overview, usage, design, verification, and Framework guides; 258-pass Windows record included | Update against the release candidate commit |
| Common tests | 2026-09-24 record: Core net8/net10 passed 43 each; Legacy net481/net8/net10 passed 22 each; 152 total | Recheck CI for the publication commit |
| Actual browser | Windows 11 x64, Chrome/Driver 154.0.8037.57: 258 normal tests passed, zero failures/skips | Stress on the same candidate; identify unverified Stable-1, Python comparisons, privileged tests, and real recipes |
| Packages | Shared 0.2.0 version; author, MIT, repository, README, symbols configured | Inspect actual packages, consumer execution, and public-commit Source Link |
| Contribution/security operations | Policies/templates and private reporting email present | GitHub private vulnerability reporting remains unverified |
| CI | Common, Windows, release, and secret workflows; SHA pins, minimal permissions, Dependabot | Required checks/protection applied; environment approval support and runner isolation remain pending |
| Publication baseline | Local changes and execution records exist | Review public history, organize commits, and pin the release SHA |

Test counts come from the [implementation record](implementation.md). They are not new runs performed during this documentation update and do not constitute release approval.

## 3. Before source publication

### 3.1 Confirmed license and remaining provenance review

The owner confirmed MIT and copyright holder/author `mete0rfish`. The standard text is in [LICENSE](../LICENSE), with metadata in `build/Package.props` and matching README/contribution guidance.

Review the origin and redistribution rights of files actually published, plus direct/transitive dependency notices. Match third-party source, binaries, and notices to distribution versions. Distinguish reference links from copied code.

The SeleniumBase reference runner is a development/comparison Python dependency, not a runtime NuGet dependency. Keep Chrome/ChromeDriver binaries out of source and NuGet packages. Satisfy the applicable terms for every file selected for distribution.

### 3.2 Review publication contents

Inspect current files and all published branches, tags, and history for tokens, certificates, passwords, internal URLs, customer data, browser profiles/cookies, test logs, and downloads. Writing this guide does not perform that audit.

Revoke or rotate exposed credentials before deciding how to clean history. Adding `.gitignore` does not remove old commits. History rewriting affects collaborators and requires an explicit scope and procedure. See [GitHub's secret-handling guidance](https://docs.github.com/en/actions/reference/security/secure-use).

Include code, tests, and documentation in the source publication baseline. Prefer verification summaries and CI artifact links over committing all TRX files. Inspect public artifacts for personal paths and page contents.

### 3.3 Minimum documentation

| File | Required content |
|---|---|
| `README.md` | English overview, experimental status, OS/TFM/browser requirements, minimal example, limitations, verification, links |
| `docs/usage.md` | Detailed English usage and API rules, consistent with the overview |
| `LICENSE` | Standard license and copyright holder |
| `THIRD-PARTY-NOTICES.md` | Actual inclusion/distribution/reference relationships and required notices |
| `CONTRIBUTING.md` | Development setup, common tests, Windows conditions, PR scope, review and licensing rules |
| `SECURITY.md` | Supported versions, private reporting route, minimum report details |
| `CODE_OF_CONDUCT.md` | Conduct rules and an actively monitored reporting route |
| `CHANGELOG.md` | User-visible changes, compatibility, known issues |
| `.github/ISSUE_TEMPLATE/*` | Bug/feature forms collecting OS/browser/driver/TFM/patch mode/reproduction |
| `.github/pull_request_template.md` | Change purpose, validation, documentation/API impact |

A separate documentation site or complex CLA system is not necessary initially. Contributors must have permission to submit material under the project license. Discuss large changes before implementation and do not promise support SLAs without staffing.

## 4. Repository operations

Require CI and restrict force pushes on the default branch. Require approval when there is more than one maintainer; avoid rules that make a sole maintainer's own PR impossible to merge. Verify effective rulesets and permissions on GitHub.

Run normal PR tests on hosted runners. Use the manual Windows workflow as the starting point for browser tests. A public-repository self-hosted runner executes arbitrary code: use a rebuildable environment isolated from personal machines, company credentials, and NuGet publishing permissions. Manual runs also execute the selected ref, so use the exact reviewed commit. See [GitHub self-hosted runner guidance](https://docs.github.com/en/actions/concepts/runners/self-hosted-runners).

Use `contents: read` by default, elevate only individual jobs, pin Actions to reviewed full SHAs, and configure Dependabot for NuGet/pip/Actions plus security reporting. Avoid checking out external PR code with secrets or write permissions under `pull_request_target`. See [GitHub Actions security guidance](https://docs.github.com/en/actions/reference/security/secure-use).

Bug forms should request local fixture reproductions, not cookies, login details, or user profiles. Track unreproduced detection-site observations separately from lifecycle defects.

## 5. Before a NuGet preview

### 5.1 Define verified support

Distinguish recorded normal Windows results from unverified coverage. Before the first preview, verify **start → navigate → disconnect → reconnect → stop on every runtime claimed as supported**. A net10 pass cannot stand in for net481 execution.

At minimum, check Chrome PID/profile preservation, a new session, stale-reference rejection, real DOM behavior, and driver/Chrome/port/profile cleanup. Broad Stable/Stable-1 compatibility or leak-stability claims require the relevant [test-plan](test-plan.md) matrix and stress evidence. Real binary patching without recipes remains unsupported/unverified.

Use .NET 10 as the long-term development focus and the Framework 4.8.1 facade for existing applications. The recorded .NET 8 support end date is **2026-11-10**; decide the subsequent maintenance policy at publication time. Review compatibility and versioning before removing a TFM. See [Microsoft's .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

### 5.2 Package completeness

Keep `BrowserDock` and `BrowserDock.Legacy`, with early README guidance on which package to choose. Verify package ID availability and ownership separately on NuGet.

Check `PackageId`, `Authors`, `PackageLicenseExpression`, project/repository URLs and type, description/tags, README, Source Link, and symbols. Verify SDK-provided Source Link resolves actual PDBs to a public source commit. Use publishable URLs for package README links. See [Microsoft's NuGet library guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/nuget).

Pack both candidates and install them in consumers without repository ProjectReferences. Check:

- Matching Core/Legacy versions and intended dependencies.
- Correct DLL/dependency selection on net481/net8/net10.
- Actual Framework binding redirects and C# 7.3 example execution.
- Included licenses, README, notices, and symbols; no fixtures, profiles, temporary source files, or browser binaries.

### 5.3 Version and release flow

The source release version configured in `build/Version.props` is **`0.2.0`**. Reconcile with any existing publication history. Both packages share a release version. Record compatibility changes even during 0.x and define stable 1.0 contracts.

Follow: pin commit → common/Windows verification → pack → consumer tests → freeze artifacts → maintainer release approval → NuGet publication. Publish the same verified artifacts after approval. Normal PR jobs must not have publication permissions.

Prefer GitHub Actions **Trusted Publishing/OIDC** for NuGet authentication. Scope policies to the repository, workflow, and environment where needed, using short-lived credentials. Verify initial ownership and policies in the actual NuGet account. See [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).

## 6. Completion criteria

| Stage | Criteria | Published outcome |
|---|---|---|
| A. Source | Rights/license/notices, history review, minimum guides, baseline commit | Experimental repository and build/contribution guidance |
| B. Preview | Core lifecycle/cleanup on declared Windows/runtime combinations, consumer tests, release flow | NuGet alpha with tested support and limitations |
| C. Stable | Full declared matrix, stress/failure recovery, API/error/support policy | 1.0 release with changes and migration guidance |
| D. Maintenance | Dependency/browser change detection, issue triage, verified regular releases | Updated support and verification records |

## 7. Remaining sequence

License, author, reporting contacts, and the identified documentation inconsistencies have been addressed. Continue with:

1. **History and provenance:** check published history for secrets, personal data, redistribution rights, and dependency notices.
2. **GitHub operations:** confirm required checks, branch protection, environment approval, and runner isolation.
3. **Publication/release baseline:** organize changes and run common CI and Windows Stress on the chosen SHA; identify remaining unverified coverage.
4. **Packages:** inspect both packages/symbols, separate consumers, and Source Link.
5. **Publication:** verify NuGet ownership/Trusted Publishing, publish verified artifacts, and test clean installation. Apply source and package publication criteria separately.
