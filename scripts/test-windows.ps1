param(
    [Parameter(Mandatory=$true)][string]$Chrome,
    [Parameter(Mandatory=$true)][string]$Driver,
    [switch]$Stress,
    [string]$Results = ".artifacts/windows"
)
$ErrorActionPreference = "Stop"
if (-not $IsWindows -or [Environment]::OSVersion.Version.Build -lt 22000 -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne "X64") {
    throw "Use PowerShell 7 on Windows 11 x64 with an interactive desktop."
}
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    $env:UCDOTNET_CHROME = (Resolve-Path $Chrome).Path
    $env:UCDOTNET_DRIVER = (Resolve-Path $Driver).Path
    $env:UCDOTNET_STRESS = if ($Stress) { "1" } else { "0" }
    New-Item -ItemType Directory -Path $Results -Force | Out-Null
    $metadata = @{
        DateUtc = [DateTime]::UtcNow.ToString("o")
        OS = [Environment]::OSVersion.ToString()
        ChromeVersion = (Get-Item $env:UCDOTNET_CHROME).VersionInfo.ProductVersion
        DriverVersion = (& $env:UCDOTNET_DRIVER --version)
        ChromeHash = (Get-FileHash $env:UCDOTNET_CHROME -Algorithm SHA256).Hash
        DriverHash = (Get-FileHash $env:UCDOTNET_DRIVER -Algorithm SHA256).Hash
        Stress = [bool]$Stress
        Dotnet = (& dotnet --info | Out-String)
    }
    $metadata | ConvertTo-Json | Set-Content (Join-Path $Results "fixture.json")
    & dotnet restore UcDotNet.slnx
    if ($LASTEXITCODE -ne 0) { throw "Restore failed." }
    & dotnet build UcDotNet.slnx --no-restore -m:1
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
    foreach ($framework in @("net8.0", "net10.0")) {
        & dotnet test tests/UcDotNet.Tests/UcDotNet.Tests.csproj -f $framework --no-build --no-restore --logger "trx;LogFileName=$framework.trx" --results-directory $Results
        if ($LASTEXITCODE -ne 0) { throw "Tests failed for $framework. Review TRX and fixture metadata." }
    }
    Write-Host "Review skipped tests in TRX. Passing common tests does not establish every Windows acceptance criterion."
} finally { Pop-Location }
