# Assign a strictly increasing MSIX version to CI-only preview packages.
# Published release/native-preview branches keep the committed version instead.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][long]$RunNumber,
    [Parameter(Mandatory)][int]$RunAttempt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Reserve four revisions per workflow run, one for each attempted execution.
# The GitHub workflow run number increases across PRs, schedules and dispatches.
$attemptsPerRun = 4
if ($RunNumber -lt 1) { throw 'GITHUB_RUN_NUMBER must be a positive integer.' }
if ($RunAttempt -lt 1 -or $RunAttempt -gt $attemptsPerRun) {
    throw "GITHUB_RUN_ATTEMPT must be between 1 and $attemptsPerRun; refusing to reuse a version."
}
if ($RunNumber -gt [math]::Floor((65535 - $RunAttempt) / $attemptsPerRun)) {
    throw 'CI preview revision would exceed the MSIX 16-bit version limit. Update the version scheme first.'
}

$path = (Resolve-Path -LiteralPath $ManifestPath -ErrorAction Stop).ProviderPath
[xml]$manifest = Get-Content -LiteralPath $path -Raw
$original = [version]$manifest.Package.Identity.Version
if ($original.Major -gt 65535 -or $original.Minor -gt 65535 -or
    $original.Build -lt 0 -or $original.Build -gt 65535 -or
    $original.Revision -lt 0 -or $original.Revision -gt 65535) {
    throw "Expected a valid four-part MSIX version (0..65535 in every component): $original"
}

$revision = [int]($RunNumber * $attemptsPerRun + $RunAttempt)
if ($revision -le $original.Revision) {
    throw "Generated preview revision $revision is not greater than committed revision $($original.Revision). Bump the manifest version scheme."
}

$preview = [version]::new($original.Major, $original.Minor, $original.Build, $revision)
$manifest.Package.Identity.SetAttribute('Version', $preview.ToString())
$manifest.Save($path)

Write-Output "CI preview MSIX version: $preview (base $original, run $RunNumber, attempt $RunAttempt)"
