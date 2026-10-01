param(
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory=$true)][string]$Destination
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'Use PowerShell 7 x64 on Windows to install and verify the win64 browser fixture.'
}
if ((Test-Path -LiteralPath $Destination) -and @(Get-ChildItem -LiteralPath $Destination -Force).Count -gt 0) {
    throw 'Use an empty fixture directory; an earlier browser installation must not be reused implicitly.'
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$root = (Resolve-Path -LiteralPath $Destination).Path
$downloads = @()
foreach ($name in @('chrome', 'chromedriver')) {
    $url = "https://storage.googleapis.com/chrome-for-testing-public/$Version/win64/$name-win64.zip"
    $archive = Join-Path $root "$name-win64.zip"
    Write-Host "Downloading $name $Version (win64)"
    Invoke-WebRequest -Uri $url -OutFile $archive -TimeoutSec 180 -MaximumRetryCount 3 -RetryIntervalSec 2
    $downloads += @{ Url = $url; SHA256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash }
    Expand-Archive -LiteralPath $archive -DestinationPath $root
    Remove-Item -LiteralPath $archive
}
$chrome = Join-Path $root 'chrome-win64/chrome.exe'
$driver = Join-Path $root 'chromedriver-win64/chromedriver.exe'
if (-not (Test-Path -LiteralPath $chrome -PathType Leaf) -or -not (Test-Path -LiteralPath $driver -PathType Leaf)) {
    throw 'Downloaded archives did not contain the expected win64 executables.'
}
$chromeVersion = (Get-Item -LiteralPath $chrome).VersionInfo.ProductVersion
$driverVersion = & $driver --version
if ($LASTEXITCODE -ne 0 -or $chromeVersion -ne $Version -or $driverVersion -notmatch "^ChromeDriver $([regex]::Escape($Version))(?:\s|$)") {
    throw "Browser fixture version mismatch: expected $Version; Chrome=$chromeVersion; driver=$driverVersion"
}
@{
    Version = $Version
    Platform = 'win64'
    Downloads = $downloads
    ChromeSHA256 = (Get-FileHash -LiteralPath $chrome -Algorithm SHA256).Hash
    DriverSHA256 = (Get-FileHash -LiteralPath $driver -Algorithm SHA256).Hash
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'downloads.json')
Write-Host "Verified Chrome and ChromeDriver $Version"
[pscustomobject]@{ Chrome = $chrome; Driver = $driver; Version = $Version }
