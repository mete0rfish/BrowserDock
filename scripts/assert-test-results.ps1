param(
    [Parameter(Mandatory=$true)][string]$Path,
    [string[]]$RequiredClasses = @()
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing TRX: $Path" }
[xml]$document = Get-Content -LiteralPath $Path -Raw
$ns = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
$ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
$results = @($document.SelectNodes('//t:UnitTestResult', $ns))
$counters = $document.SelectSingleNode('//t:ResultSummary/t:Counters', $ns)
if ($results.Count -eq 0 -or $null -eq $counters) { throw "TRX contains no test results: $Path" }
if ([int]$counters.total -ne $results.Count -or [int]$counters.passed -ne $results.Count -or [int]$counters.executed -ne $results.Count) {
    throw "Not every selected test executed and passed in $Path. Counters: $($counters.OuterXml)"
}
foreach ($result in $results) {
    if ($result.outcome -ne 'Passed') { throw "Unexpected $($result.outcome): $($result.testName) in $Path" }
}
$definitions = @{}
foreach ($test in $document.SelectNodes('//t:UnitTest', $ns)) {
    $id = [string]$test.id
    if (-not $id -or $definitions.ContainsKey($id)) { throw "Missing or duplicate test definition ID in $Path" }
    $definitions[$id] = [string]$test.TestMethod.className
}
$classes = @()
$executed = @{}
foreach ($result in $results) {
    $id = [string]$result.testId
    if (-not $definitions.ContainsKey($id) -or $executed.ContainsKey($id)) { throw "Missing definition or duplicate result for test ID '$id' in $Path" }
    $executed[$id] = $true
    $classes += $definitions[$id]
}
foreach ($class in $RequiredClasses) {
    if ($class -notin $classes) { throw "Required fixture $class did not execute in $Path" }
}
Write-Host "Validated $($results.Count) passing tests in $Path"
