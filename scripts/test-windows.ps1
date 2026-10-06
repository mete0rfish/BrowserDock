param(
    [Parameter(Mandatory=$true)][string]$Chrome,
    [Parameter(Mandatory=$true)][string]$Driver,
    [switch]$Stress,
    [switch]$QuarantinedOnly,
    [ValidateSet('Windows11', 'GitHubHosted')][string]$FixtureKind = 'Windows11',
    [string]$Results = ".artifacts/windows/$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))"
)
$ErrorActionPreference = "Stop"
if ($QuarantinedOnly -and $Stress) { throw 'QuarantinedOnly and Stress select different suites; run them separately.' }
if (-not $IsWindows -or [Environment]::OSVersion.Version.Build -lt 22000 -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne "X64") {
    throw "Use PowerShell 7 x64 on Windows build 22000 or later (Windows 11 or the hosted Windows Server fixture)."
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
    # Symlink rejection needs separate Windows privileges. Quarantined tests
    # run only in the dedicated manual suite (#41); skipped tests still fail audit.
    $filter = 'FullyQualifiedName!~RejectsReparsePointOwnedPaths'
    if ($QuarantinedOnly) { $filter += '&TestCategory=Quarantined&TestCategory!=Stress' }
    else {
        $filter += '&TestCategory!=Quarantined'
        if (-not $Stress) { $filter += '&TestCategory!=Stress' }
    }
    $metadata = @{
        DateUtc = [DateTime]::UtcNow.ToString("o")
        FixtureKind = $FixtureKind
        OSName = (Get-CimInstance Win32_OperatingSystem).Caption
        OS = [Environment]::OSVersion.ToString()
        UserInteractive = [Environment]::UserInteractive
        RunnerImage = $env:ImageOS
        RunnerImageVersion = $env:ImageVersion
        ChromeVersion = (Get-Item $env:BROWSERDOCK_CHROME).VersionInfo.ProductVersion
        DriverVersion = (& $env:BROWSERDOCK_DRIVER --version)
        ChromeHash = (Get-FileHash $env:BROWSERDOCK_CHROME -Algorithm SHA256).Hash
        DriverHash = (Get-FileHash $env:BROWSERDOCK_DRIVER -Algorithm SHA256).Hash
        Stress = [bool]$Stress
        QuarantinedOnly = [bool]$QuarantinedOnly
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
        $suite = if ($QuarantinedOnly) { 'Quarantined' } else { 'Browser' }
        & ./scripts/assert-suite-results.ps1 -Path (Join-Path $Results "$framework.trx") -Suite $suite -Project Core
    }
    # The quarantined case exists only in Core. Do not report empty Legacy runs as passes.
    if (-not $QuarantinedOnly) {
        foreach ($framework in @("net481", "net8.0", "net10.0")) {
            & dotnet test tests/BrowserDock.FrameworkTests/BrowserDock.FrameworkTests.csproj -f $framework --no-build --no-restore --filter $filter --logger "trx;LogFileName=legacy-$framework.trx" --results-directory $Results
            if ($LASTEXITCODE -ne 0) { throw "Legacy tests failed for $framework. Review TRX and fixture metadata." }
            & ./scripts/assert-suite-results.ps1 -Path (Join-Path $Results "legacy-$framework.trx") -Suite Browser -Project Legacy
        }
    }
    Write-Host 'All selected tests executed and passed. Reference Python comparisons and real patch recipes require their separate runs.'
    if (-not $QuarantinedOnly) { Write-Host 'Quarantined coverage is excluded; run Windows quarantined browser tests separately. See #41.' }
} finally { Pop-Location }
