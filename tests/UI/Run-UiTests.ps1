# Run on a disposable interactive Windows desktop with PowerShell 5.1 (STA).
# Installs the exact CI-built MSIX; grants trust only to its public test certificate;
# invokes NUnit/FlaUI tests and always removes the app and temporary trust.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageFolder,
    [string]$ResultsDirectory = (Join-Path $env:RUNNER_TEMP 'plainix-ui-results')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (![Environment]::UserInteractive -or (Get-Process -Id $PID).SessionId -eq 0) {
    throw 'UI E2E tests require an interactive Windows desktop; Session 0 is unsupported.'
}
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Run Windows PowerShell with -STA (powershell.exe -NoProfile -STA -File ...).'
}
if (Get-AppxPackage -Name 'Notepads-Dev') {
    throw 'A clean Windows test profile is required: Notepads-Dev is already installed.'
}

$folder = (Resolve-Path -LiteralPath $PackageFolder).Path
$bundles = @(Get-ChildItem -LiteralPath $folder -File -Filter '*.msixbundle')
if ($bundles.Count -ne 1) { throw "Expected one MSIX bundle in $folder." }
$bundle = $bundles[0]
$certificateFile = [IO.Path]::ChangeExtension($bundle.FullName, '.cer')
if (!(Test-Path -LiteralPath $certificateFile)) { throw "Test signer certificate missing: $certificateFile" }
$cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2($certificateFile)
$signature = Get-AuthenticodeSignature -LiteralPath $bundle.FullName
if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint -or
    $signature.Status -eq 'HashMismatch') {
    throw 'The downloaded MSIX signature does not match the accompanying public certificate.'
}
if ($cert.Subject -ne 'CN=Web-Usov Notepads Preview') {
    throw "Unexpected test package publisher: $($cert.Subject)"
}

$trustPath = "Cert:\LocalMachine\TrustedPeople\$($cert.Thumbprint)"
$importedTrust = !(Test-Path -LiteralPath $trustPath)
$installed = $null
$previousPreference = $env:PLAINIX_UI_ALIAS
try {
    New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
    if ($importedTrust) {
        Import-Certificate -FilePath $certificateFile -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    }
    $dependencies = @(Get-ChildItem -LiteralPath (Join-Path $folder 'Dependencies\x64') -File -Filter '*.appx')
    if (!$dependencies.Count) { throw 'The test MSIX dependencies are missing.' }
    Add-AppxPackage -Path $bundle.FullName -DependencyPath $dependencies.FullName
    $installed = Get-AppxPackage -Name 'Notepads-Dev'
    if (!$installed -or $installed.Publisher -ne $cert.Subject) {
        throw 'Installed application identity does not match the signed test artifact.'
    }

    $local = Join-Path $env:LOCALAPPDATA "Packages\$($installed.PackageFamilyName)\LocalState"
    New-Item -ItemType Directory -Force -Path $local | Out-Null
    $env:PLAINIX_UI_DATA = $local
    $env:PLAINIX_UI_ALIAS = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\plainix.exe'
    $env:PLAINIX_UI_ARTIFACTS = Join-Path $ResultsDirectory 'diagnostics'
    $env:PLAINIX_UI_FEATURE_EXTENSIONS = if (
        (Test-Path 'plugins/markdown-preview/markdown-preview.notepads-extension.json') -and
        (Test-Path 'plugins/changes-diff/changes-diff.notepads-extension.json')
    ) { '1' } else { '0' }
    if (!(Test-Path -LiteralPath $env:PLAINIX_UI_ALIAS)) {
        throw 'Installed Plainix execution alias was not registered.'
    }

    Write-Host "Running installed Plainix UI tests (package $($installed.Version)); feature extensions=$env:PLAINIX_UI_FEATURE_EXTENSIONS"
    $project = Join-Path (Resolve-Path '.').Path 'tests/UI/Plainix.UI.Tests.csproj'
    dotnet test $project --configuration Release --results-directory $ResultsDirectory --logger 'trx;LogFileName=plainix-ui.trx' --verbosity normal
    if ($LASTEXITCODE -ne 0) { throw "UI E2E tests failed with exit code $LASTEXITCODE." }
}
finally {
    if ($installed) {
        foreach ($process in @(Get-Process -Name Notepads -ErrorAction SilentlyContinue)) {
            try {
                if ($process.Path -ieq (Join-Path $installed.InstallLocation 'Notepads.exe')) {
                    Stop-Process -Id $process.Id -Force -ErrorAction Stop
                }
            }
            catch [System.InvalidOperationException] { }
        }
        Remove-AppxPackage -Package $installed.PackageFullName -ErrorAction Continue
    }
    if ($importedTrust -and (Test-Path -LiteralPath $trustPath)) {
        Remove-Item -LiteralPath $trustPath -ErrorAction Continue
    }
    $env:PLAINIX_UI_ALIAS = $previousPreference
    Remove-Item Env:PLAINIX_UI_DATA, Env:PLAINIX_UI_ARTIFACTS, Env:PLAINIX_UI_FEATURE_EXTENSIONS -ErrorAction SilentlyContinue
}
