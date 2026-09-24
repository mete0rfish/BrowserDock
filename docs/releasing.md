# Release procedure

The first candidate is `0.2.0-alpha.1`. The source packages share `build/Version.props`;
the classic package consumer reads the same version through `Directory.Build.props`.
No public repository or NuGet package has been created by this change.

## Complete before public source release

1. Preserve the confirmed MIT license and copyright holder/author `mete0rfish` in
   `LICENSE`, `build/Package.props`, README and the usage guide, CONTRIBUTING and THIRD-PARTY-NOTICES.
2. Keep the owner-confirmed private reporting email `sungwonyoon326@gmail.com` current
   in SECURITY and CODE_OF_CONDUCT. Email is the documented reporting route; GitHub
   private vulnerability reporting has not been verified as enabled.
3. Review all published Git refs and working files for secrets, personal information,
   customer content and copied code. Review dependency notices for the actual distribution.
   A pattern scan is only one part of this review. Never publish profiles or raw credentials.
4. Create/configure the intended repository and remote, then commit the reviewed changes.
   GitHub repository creation, visibility, rights ownership and NuGet name availability
   require the owner's account and decisions; local files cannot configure them.

## Repository settings

- Require the Common contracts checks on the default branch. Prevent force pushes and
  deletion; require another review when the maintainer team makes that practical.
- Enable Dependabot alerts, secret scanning and push protection where available.
- Create a `windows-browser` environment. Restrict its branches and require a maintainer
  to approve the reviewed commit before executing on the dedicated interactive Windows runner.
- Use a disposable Windows 11 x64 fixture with no personal profiles, company credentials
  or publishing secrets. Register `windows-11-interactive` only on that fixture, running
  in the interactive desktop. Recreate it between untrusted runs. Public PRs use hosted CI.
- Create a `release` environment with branch/tag restrictions and required approval.
  Restrict workflow editing and review Action SHA updates.

The workflows already use read-only default permissions, pinned Action commits and
checkout without persisted credentials. These files do not themselves enable environment
approvals or branch rules: verify the settings before enabling either privileged workflow.

## Build a local candidate

```sh
dotnet restore BrowserDock.slnx
dotnet build BrowserDock.slnx -c Release --no-restore -m:1
dotnet pack src/BrowserDock -c Release --no-build --no-restore -o .artifacts/packages
dotnet pack src/BrowserDock.Legacy -c Release --no-build --no-restore -o .artifacts/packages
python3 scripts/check-packages.py .artifacts/packages
```

The local check verifies identities, versions, target DLLs, Legacy's dependency on the
matching core version, README/notice files and portable symbol packages. Local candidates
must not be published until all release checks and operational prerequisites are met.
`python3 scripts/check-packages.py .artifacts/packages --release` additionally requires
confirmed author/license/repository metadata, a commit SHA and the matching LICENSE file.

The SDK supplies Source Link. Build releases from the committed public Git remote;
inspect portable PDB Source Link data and resolve it to that commit before approval.
Metadata and the presence of a PDB alone do not prove the source URL works. Verify that
public artifacts contain no local sensitive content or unwanted embedded sources.

## Validate and publish the same candidate

1. Update CHANGELOG, support.md and README and the usage guide to describe the actual tested matrix.
   Bump only `build/Version.props` and any user-facing installation examples.
2. Commit the final source and record its SHA. Run hosted Common contracts for that commit.
3. Run Windows 11 browser acceptance on that exact commit, with `stress=true` and compatible
   Chrome/driver. Inspect fixture metadata, all five TRX files, lifecycle/cleanup results
   and known exclusions. The privileged symlink test and Python SeleniumBase comparison
   are separate runs; the release workflow does not claim to cover them.
4. Run Package candidate and approved release with `publish=false`. It tests common
   runtimes on Windows, packs both packages and symbols, checks package contents, and
   compiles/loads .NET 8/10 and classic C# 7.3 consumers against those packages. The no-argument
   consumer smoke test checks loading, not real browser behavior.
5. Establish ownership of both NuGet package IDs and configure Trusted Publishing for
   this GitHub owner/repository, `.github/workflows/release.yml`, and environment `release`.
   Set repository variable `NUGET_USER` to the NuGet account name. Do not store a permanent
   API key. Follow the current [NuGet instructions](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
   for initial package ownership and trust policy setup.
6. Only after all preceding settings and checks are ready, set repository variable
   `PUBLIC_RELEASE_ENABLED=true`. This is an explicit administrative enablement switch.
7. Run the release workflow on the final commit with `publish=true` and the successful
   Windows run ID. Review that run's candidate artifact before approving its `release`
   environment. The publish job validates the Windows workflow identity, same commit,
   latest run attempt, clean stress fixture and required passing test classes. It validates
   release package metadata, obtains an OIDC credential and pushes the **same artifact
   built and tested in that workflow run**. It does not rebuild after approval.
8. Confirm both NuGet packages and symbols are indexed and install them in a clean
   consumer. Record release notes, commit and workflow links. NuGet publishing of two
   packages is not atomic: if partially successful, resume the same version/artifact;
   never silently replace it. `--skip-duplicate` supports a partial-publish retry.

Source Link resolution, expanded browser compatibility, separate SeleniumBase comparisons,
repository settings and account ownership remain review items; the automation must not be
described as evidence that those checks passed. No release is triggered by a push or PR.
