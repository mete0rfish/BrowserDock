# Dependencies and provenance

UcDotNet is an independent implementation based on the behavior contracts in `docs/spec.md`, the W3C WebDriver wire format, CDP, and public Selenium APIs. No source from the GPL C# UndetectedChromeDriver project was copied or translated. Reference links in the design documents describe research provenance; they do not imply incorporation of that code or official affiliation.

- **Selenium.WebDriver 4.44.0** — Apache-2.0, Selenium contributors / Software Freedom Conservancy. Package metadata and upstream license: https://github.com/SeleniumHQ/selenium/blob/selenium-4.44.0/LICENSE . Preserve applicable upstream license and NOTICE material when redistributing dependencies.
- **Microsoft.Extensions.Logging.Abstractions 8.0.2** — dependency notices are supplied by its NuGet package. Upstream: https://github.com/dotnet/runtime . Preserve dependency license material when distributing compiled artifacts.
- **System.Text.Json 8.0.6, System.Threading.Channels 8.0.0, Microsoft.Bcl.AsyncInterfaces 8.0.0** — Framework compatibility dependencies, with transitive supporting assemblies; licenses/notices are supplied by their NuGet packages. Upstream: https://github.com/dotnet/runtime . Preserve applicable dependency license material in binary distributions.
- **Microsoft.NETFramework.ReferenceAssemblies.net481 1.0.3** — build-only reference assemblies (`PrivateAssets=all`); not bundled in the runtime packages and not a runtime installer.
- NUnit, NUnit3TestAdapter and Microsoft.NET.Test.Sdk are development-only test dependencies and are not UcDotNet runtime dependencies.
- Chrome, ChromeDriver and Chrome for Testing are not bundled or downloaded by UcDotNet. Tests require caller-supplied binaries. Their names identify compatibility targets, not endorsement.

The project's own public distribution license remains undecided. This file records provenance and does not grant a project license. Public packaging requires a separate license/notice review. No public package has been published by this implementation task.
