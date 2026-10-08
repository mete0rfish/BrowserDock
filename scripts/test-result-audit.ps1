# Negative checks for both the TRX auditor and the CI/release suite manifests.
$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $path = Join-Path $temporary 'sample.trx'
    function Write-Trx([string[]]$Classes) {
        $results = ''; $definitions = ''; $index = 0
        foreach ($class in $Classes) {
            $index++
            $results += "<UnitTestResult testId='$index' testName='fixture-$index' outcome='Passed' />"
            $definitions += "<UnitTest id='$index'><TestMethod className='$class' /></UnitTest>"
        }
        $xml = "<TestRun xmlns='http://microsoft.com/schemas/VisualStudio/TeamTest/2010'><Results>$results</Results><TestDefinitions>$definitions</TestDefinitions><ResultSummary><Counters total='$index' executed='$index' passed='$index' /></ResultSummary></TestRun>"
        Set-Content -LiteralPath $path -Value $xml
        return $xml
    }
    function Assert-Rejected([scriptblock]$Action) {
        $rejected = $false
        try { & $Action } catch { $rejected = $true }
        if (-not $rejected) { throw 'Audit accepted an invalid result.' }
    }
    $valid = Write-Trx @('Required')
    & "$PSScriptRoot/assert-test-results.ps1" -Path $path -RequiredClasses Required
    foreach ($invalid in @(
        $valid.Replace("outcome='Passed'", "outcome='NotExecuted'"),
        $valid.Replace("passed='1'", "passed='0'"),
        $valid.Replace("executed='1'", "executed='0'"),
        $valid.Replace("className='Required'", "className='Missing'"),
        $valid.Replace("testId='1'", "testId='unknown'"),
        # A discovered but unexecuted Required fixture must not satisfy the gate.
        $valid.Replace("className='Required'", "className='Other'").Replace('</TestDefinitions>', "<UnitTest id='unused'><TestMethod className='Required' /></UnitTest></TestDefinitions>"),
        '<TestRun />', '<invalid'
    )) {
        Set-Content -LiteralPath $path -Value $invalid
        Assert-Rejected { & "$PSScriptRoot/assert-test-results.ps1" -Path $path -RequiredClasses Required }
    }
    $two = Write-Trx @('Required','Other')
    Set-Content -LiteralPath $path -Value $two.Replace("testId='2'", "testId='1'")
    Assert-Rejected { & "$PSScriptRoot/assert-test-results.ps1" -Path $path -RequiredClasses Required }
    Remove-Item $path
    Assert-Rejected { & "$PSScriptRoot/assert-test-results.ps1" -Path $path }
    $common = @('BrowserDock.Tests.UcWorkflowTests','BrowserDock.Tests.UcLaunchProfileTests','BrowserDock.Tests.UcScriptRegistrationTests','BrowserDock.Tests.CommandDeadlineTests','BrowserDock.Tests.TextWaitTests')
    $uc = @('BrowserDock.Tests.UcBrowserTests','BrowserDock.Tests.UcLaunchProfileBrowserTests')
    foreach ($project in 'Core','Legacy') {
        foreach ($suite in 'Common','Browser') {
            $classes = if ($suite -eq 'Common') { $common }
                elseif ($project -eq 'Core') { @('BrowserDock.Tests.WindowsTests','BrowserDock.Tests.ReferenceTests','BrowserDock.Tests.AttachmentFailureTests') + $uc }
                else { @('BrowserDock.FrameworkTests.WindowsLegacyTests') + $uc }
            $null = Write-Trx $classes
            & "$PSScriptRoot/assert-suite-results.ps1" -Path $path -Suite $suite -Project $project
            foreach ($missing in $classes) {
                $null = Write-Trx @($classes | Where-Object { $_ -ne $missing })
                Assert-Rejected { & "$PSScriptRoot/assert-suite-results.ps1" -Path $path -Suite $suite -Project $project }
            }
        }
    }
    $null = Write-Trx @('BrowserDock.Tests.WindowsTests')
    & "$PSScriptRoot/assert-suite-results.ps1" -Path $path -Suite Quarantined -Project Core
    Assert-Rejected { & "$PSScriptRoot/assert-suite-results.ps1" -Path $path -Suite Quarantined -Project Legacy }
} finally { Remove-Item -LiteralPath $temporary -Recurse -Force }
