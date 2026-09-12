# Dependency-free negative checks for the release gate; runnable in common CI.
$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $path = Join-Path $temporary 'sample.trx'
    $valid = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testName="fixture" outcome="Passed" /></Results><TestDefinitions><UnitTest><TestMethod className="Required" /></UnitTest></TestDefinitions><ResultSummary><Counters total="1" executed="1" passed="1" /></ResultSummary></TestRun>'
    Set-Content -LiteralPath $path -Value $valid
    & "$PSScriptRoot/assert-test-results.ps1" -Path $path -RequiredClasses Required
    foreach ($invalid in @($valid.Replace('outcome="Passed"', 'outcome="NotExecuted"'), $valid.Replace('passed="1"', 'passed="0"'), $valid.Replace('className="Required"', 'className="Missing"'), '<TestRun />')) {
        Set-Content -LiteralPath $path -Value $invalid
        $rejected = $false
        try { & "$PSScriptRoot/assert-test-results.ps1" -Path $path -RequiredClasses Required } catch { $rejected = $true }
        if (-not $rejected) { throw "Audit accepted an invalid result: $invalid" }
    }
} finally { Remove-Item -LiteralPath $temporary -Recurse -Force }
