param(
    [Parameter(Mandatory=$true)][string]$Chrome,
    [Parameter(Mandatory=$true)][string]$Driver,
    [switch]$Stress,
    [string]$Results = ".artifacts/windows/$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))"
)
$ErrorActionPreference = "Stop"
if (-not $IsWindows -or [Environment]::OSVersion.Version.Build -lt 22000 -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne "X64") {
    throw "Use PowerShell 7 on Windows 11 x64 with an interactive desktop."
}
$frameworkRelease = Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release
if ($frameworkRelease -lt 533320) { throw "Install the .NET Framework 4.8.1 runtime before running Framework tests." }
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    $env:BROWSERDOCK_CHROME = (Resolve-Path $Chrome).Path
    $env:BROWSERDOCK_DRIVER = (Resolve-Path $Driver).Path
    $env:BROWSERDOCK_STRESS = if ($Stress) { "1" } else { "0" }
    if ((Test-Path $Results) -and @(Get-ChildItem -LiteralPath $Results -Force).Count -gt 0) { throw 'Use a new results directory to preserve earlier failures.' }
    New-Item -ItemType Directory -Path $Results -Force | Out-Null
    # Symlink rejection needs separate Windows privileges. Stress is opt-in;
    # filter it out entirely so any remaining skipped test is a failed run.
    $filter = 'FullyQualifiedName!~RejectsReparsePointOwnedPaths'
    if (-not $Stress) { $filter += '&TestCategory!=Stress' }
    $metadata = @{
        DateUtc = [DateTime]::UtcNow.ToString("o")
        OS = [Environment]::OSVersion.ToString()
        ChromeVersion = (Get-Item $env:BROWSERDOCK_CHROME).VersionInfo.ProductVersion
        DriverVersion = (& $env:BROWSERDOCK_DRIVER --version)
        ChromeHash = (Get-FileHash $env:BROWSERDOCK_CHROME -Algorithm SHA256).Hash
        DriverHash = (Get-FileHash $env:BROWSERDOCK_DRIVER -Algorithm SHA256).Hash
        Stress = [bool]$Stress
        Filter = $filter
        Commit = (& git rev-parse HEAD)
        WorkingTree = (& git status --short | Out-String)
        FrameworkRelease = $frameworkRelease
        Dotnet = (& dotnet --info | Out-String)
    }
    $metadata | ConvertTo-Json | Set-Content (Join-Path $Results "fixture.json")
    & dotnet restore BrowserDock.slnx
    if ($LASTEXITCODE -ne 0) { throw "Restore failed." }
    & dotnet build BrowserDock.slnx --no-restore -m:1
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
    foreach ($framework in @("net8.0", "net10.0")) {
        $env:BROWSERDOCK_REFERENCE_RESULTS = Join-Path (Resolve-Path $Results).Path "reference-$framework"
        & dotnet test tests/BrowserDock.Tests/BrowserDock.Tests.csproj -f $framework --no-build --no-restore --filter $filter --logger "trx;LogFileName=$framework.trx" --results-directory $Results
        if ($LASTEXITCODE -ne 0) { throw "Tests failed for $framework. Review TRX and fixture metadata." }
        & ./scripts/assert-test-results.ps1 -Path (Join-Path $Results "$framework.trx") -RequiredClasses 'BrowserDock.Tests.WindowsTests','BrowserDock.Tests.ReferenceTests','BrowserDock.Tests.AttachmentFailureTests'
    }
    foreach ($framework in @("net481", "net8.0", "net10.0")) {
        & dotnet test tests/BrowserDock.FrameworkTests/BrowserDock.FrameworkTests.csproj -f $framework --no-build --no-restore --filter $filter --logger "trx;LogFileName=legacy-$framework.trx" --results-directory $Results
        if ($LASTEXITCODE -ne 0) { throw "Legacy tests failed for $framework. Review TRX and fixture metadata." }
        & ./scripts/assert-test-results.ps1 -Path (Join-Path $Results "legacy-$framework.trx") -RequiredClasses 'BrowserDock.FrameworkTests.WindowsLegacyTests'
    }
    Write-Host 'All selected tests executed and passed. Reference Python comparisons and real patch recipes require their separate runs.'
} finally { Pop-Location }
