# Test preview MSIX version monotonicity without requiring Windows packaging tools.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).ProviderPath
$originalManifest = Join-Path $root 'src/Notepads/Package.appxmanifest'
$allocator = Join-Path $root 'tools/installer/Set-PreviewPackageVersion.ps1'
$work = Join-Path ([IO.Path]::GetTempPath()) ("PlainixPreviewVersion-" + [Guid]::NewGuid())
$null = New-Item -ItemType Directory -Path $work
$copy = Join-Path $work 'Package.appxmanifest'

function Check([bool]$value, [string]$name) {
    if (-not $value) { throw "Failed version test: $name" }
}

function Compute([long]$run, [int]$attempt) {
    Copy-Item -LiteralPath $originalManifest -Destination $copy -Force
    $base = [version]([xml](Get-Content -LiteralPath $copy -Raw)).Package.Identity.Version
    & $allocator -ManifestPath $copy -RunNumber $run -RunAttempt $attempt | Out-Null
    $result = [version]([xml](Get-Content -LiteralPath $copy -Raw)).Package.Identity.Version
    Check ($result.Major -eq $base.Major -and $result.Minor -eq $base.Minor -and
        $result.Build -eq $base.Build) 'preserve first three version fields'
    return $result
}

function Expect-Rejected([long]$run, [int]$attempt, [string]$name) {
    Copy-Item -LiteralPath $originalManifest -Destination $copy -Force
    $before = (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash
    $rejected = $false
    try {
        & $allocator -ManifestPath $copy -RunNumber $run -RunAttempt $attempt | Out-Null
    }
    catch { $rejected = $true }
    Check $rejected $name
    Check ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -eq $before) "$name keeps source unmodified"
}

try {
    $base = [version]([xml](Get-Content -LiteralPath $originalManifest -Raw)).Package.Identity.Version
    $first = Compute 66 1
    $retry = Compute 66 2
    $later = Compute 67 1
    Check ($first -gt $base) 'preview upgrades the committed version'
    Check ($retry -gt $first) 'a rerun has a newer version'
    Check ($later -gt $retry) 'the next run upgrades the preceding rerun'
    Check ($first.Revision -eq 265 -and $retry.Revision -eq 266 -and
        $later.Revision -eq 269) 'deterministic preview revisions'

    $limit = Compute 16383 3
    Check ($limit.Revision -eq 65535) 'maximum permitted revision'
    Expect-Rejected 0 1 'zero run number'
    Expect-Rejected 66 0 'zero attempt'
    Expect-Rejected 66 5 'too many rerun attempts'
    Expect-Rejected 16383 4 'revision overflow'
    Expect-Rejected 16384 1 'workflow run overflow'

    Copy-Item -LiteralPath $originalManifest -Destination $copy -Force
    [xml]$highVersion = Get-Content -LiteralPath $copy -Raw
    $highVersion.Package.Identity.SetAttribute('Version', '1.7.0.500')
    $highVersion.Save($copy)
    $rejected = $false
    try { & $allocator -ManifestPath $copy -RunNumber 66 -RunAttempt 1 | Out-Null }
    catch { $rejected = $true }
    Check $rejected 'never downgrade an existing manifest revision'
    Check (([xml](Get-Content -LiteralPath $copy -Raw)).Package.Identity.Version -eq '1.7.0.500') 'failed allocation retains manifest version'

    Write-Output 'PASS CI preview MSIX version increments across runs/retries and rejects unsafe revisions.'
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
