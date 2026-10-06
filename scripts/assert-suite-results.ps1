param(
    [Parameter(Mandatory=$true)][string]$Path,
    [Parameter(Mandatory=$true)][ValidateSet('Common', 'Browser', 'Quarantined')][string]$Suite,
    [Parameter(Mandatory=$true)][ValidateSet('Core', 'Legacy')][string]$Project
)
$ErrorActionPreference = 'Stop'
# One manifest for CI execution and release evidence: UC coverage must not
# disappear merely because a fixture was no longer compiled or discovered.
$required = switch ($Suite) {
    'Common' { @('BrowserDock.Tests.UcWorkflowTests', 'BrowserDock.Tests.UcLaunchProfileTests', 'BrowserDock.Tests.UcScriptRegistrationTests') }
    'Browser' {
        if ($Project -eq 'Core') { @('BrowserDock.Tests.WindowsTests', 'BrowserDock.Tests.ReferenceTests', 'BrowserDock.Tests.AttachmentFailureTests') }
        else { @('BrowserDock.FrameworkTests.WindowsLegacyTests') }
        @('BrowserDock.Tests.UcBrowserTests', 'BrowserDock.Tests.UcLaunchProfileBrowserTests')
    }
    'Quarantined' {
        if ($Project -ne 'Core') { throw 'There is no Legacy quarantined suite.' }
        @('BrowserDock.Tests.WindowsTests')
    }
}
& "$PSScriptRoot/assert-test-results.ps1" -Path $Path -RequiredClasses $required
