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
$classes = @($document.SelectNodes('//t:UnitTest/t:TestMethod', $ns) | ForEach-Object { $_.className })
foreach ($class in $RequiredClasses) {
    if (-not ($classes | Where-Object { $_ -eq $class })) { throw "Required fixture $class was not discovered in $Path" }
}
Write-Host "Validated $($results.Count) passing tests in $Path"
