# Resolved dependency inventory

Reviewed on 2026-09-24 against the restored Core/Legacy project assets and cached NuGet manifests. This records metadata and distribution boundaries; it is not an audit of upstream source ownership. Versions must be regenerated/reviewed when restoring a release candidate.

| Package | Version | License evidence | Distribution |
|---|---|---|---|
| Microsoft.Bcl.AsyncInterfaces | 10.0.12 | MIT | NuGet dependency; not embedded in BrowserDock packages |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.12 | MIT | NuGet dependency; not embedded in BrowserDock packages |
| Microsoft.Extensions.Logging.Abstractions | 10.0.12 | MIT | NuGet dependency; not embedded in BrowserDock packages |
| Microsoft.NETFramework.ReferenceAssemblies | 1.0.3 | [upstream license URL](https://github.com/microsoft/dotnet/blob/main/LICENSE); no SPDX field | Build-only reference package; not shipped |
| Microsoft.NETFramework.ReferenceAssemblies.net481 | 1.0.3 | [upstream license URL](https://github.com/microsoft/dotnet/blob/main/LICENSE); no SPDX field | Build-only reference package; not shipped |
| Selenium.WebDriver | 4.44.0 | Apache-2.0 | NuGet dependency; not embedded in BrowserDock packages |
| System.Buffers | 4.6.1 | MIT | NuGet dependency; not embedded in BrowserDock packages |
| System.Diagnostics.DiagnosticSource | 10.0.12 | MIT | NuGet dependency; not embedded in BrowserDock packages |
| System.Memory | 4.6.3 | MIT | NuGet dependency; not embedded in BrowserDock packages |
| System.Numerics.Vectors | 4.6.1 | MIT | NuGet dependency; not embedded in BrowserDock packages |
| System.Runtime.CompilerServices.Unsafe | 6.1.2 | MIT | NuGet dependency; not embedded in BrowserDock packages |
| System.Text.Encodings.Web | 8.0.0 | MIT | NuGet dependency; not embedded in BrowserDock packages |
| System.Text.Json | 8.0.6 | MIT | NuGet dependency; not embedded in BrowserDock packages |
| System.Threading.Channels | 8.0.0 | MIT | NuGet dependency; not embedded in BrowserDock packages |
| System.Threading.Tasks.Extensions | 4.6.3 | MIT | NuGet dependency; not embedded in BrowserDock packages |
| System.ValueTuple | 4.5.0 | MIT (package LICENSE.TXT) | NuGet dependency; not embedded in BrowserDock packages |

Both BrowserDock packages contain their own target DLLs, README, MIT LICENSE and THIRD-PARTY-NOTICES. External dependency DLLs are resolved by NuGet, not copied into these packages. Consumers redistributing dependencies must preserve their applicable upstream licenses/notices.

SeleniumBase 4.53.7 and psutil 7.2.2 are separately installed test tools. Their source and browser binaries are not included in the NuGet payload. Source comparison and permission review for any future copied code remains required.

Gitleaks 8.30.1 and Gitleaks Action v3 are CI-only tooling, not library dependencies or NuGet payload. Action usage is pinned to a full commit SHA.

Local package inspection passed for both nupkg/snupkg pairs on 2026-09-24 using `scripts/check-packages.py --release`. Payload checks found only the expected BrowserDock DLLs under lib/, with no dependency DLLs or browser binaries. This is a local packaging check, not release approval or verification of public Source Link resolution.
