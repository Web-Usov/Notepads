# Run elevated on a disposable Windows x64 account using Windows PowerShell 5.1.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$FeedFolder,
    [Uri]$PublishedFeedUri,
    [switch]$TestUpgradeFromFirstRelease
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
$folder = (Resolve-Path -LiteralPath $FeedFolder).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ("notepads-native-" + [Guid]::NewGuid())
$certsAdded = [Collections.Generic.List[string]]::new()
$server = $null
$subject = 'CN=Web-Usov Notepads Preview'
$appName = 'Notepads-Dev'
if (Get-AppxPackage -Name $appName) { throw 'Use a clean Windows profile with no existing Notepads-Dev installation.' }

function Get-WebFile([Uri]$Address, [string]$Destination) {
    if ($Address.Scheme -ne 'https') { throw "Expected a public HTTPS URL: $Address" }
    Invoke-WebRequest -UseBasicParsing -Uri $Address -OutFile $Destination -TimeoutSec 120
}

function Assert-SameFile([string]$Actual, [string]$Expected) {
    if ((Get-FileHash -LiteralPath $Actual).Hash -ne (Get-FileHash -LiteralPath $Expected).Hash) {
        throw "Downloaded bytes differ from verified build artifact: $Expected"
    }
}

function Trust-Bundle([string]$Bundle, [string]$Cer) {
    $cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2($Cer)
    $signature = Get-AuthenticodeSignature -LiteralPath $Bundle
    if ($cert.Subject -cne $subject -or !$signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint) {
        throw 'The fork package must be signed by the accompanying certificate.'
    }
    $path = "Cert:\LocalMachine\TrustedPeople\$($cert.Thumbprint)"
    if (!(Test-Path -LiteralPath $path)) {
        Import-Certificate -FilePath $Cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        $certsAdded.Add($path)
    }
}

function Install-PreviousVersion {
    # Pin the exact previously published installer. Its leaf certificate differs from the new build.
    $oldZip = Join-Path $work 'old.zip'
    Get-WebFile 'https://github.com/Web-Usov/Notepads/releases/download/v1.6.0.0/Notepads-1.6.0.0-Windows-x64.zip' $oldZip
    if ((Get-FileHash -LiteralPath $oldZip).Hash -ne 'a6c98b141f53a8fd66a26595ac0812e7252edb118732566462e3fcff6032a529') {
        throw 'The v1.6.0.0 installer does not match its published release digest.'
    }
    $old = Join-Path $work 'old'
    Expand-Archive -LiteralPath $oldZip -DestinationPath $old
    $bundle = @(Get-ChildItem -LiteralPath $old -Filter '*.msixbundle')
    if ($bundle.Count -ne 1) { throw 'Expected exactly one earlier release bundle.' }
    Trust-Bundle $bundle[0].FullName ([IO.Path]::ChangeExtension($bundle[0].FullName, '.cer'))
    $deps = @(Get-ChildItem -LiteralPath (Join-Path $old 'Dependencies/x64') -Filter '*.appx')
    Add-AppxPackage -Path $bundle[0].FullName -DependencyPath $deps.FullName
    $prior = Get-AppxPackage -Name $appName
    if (!$prior -or $prior.Version -ne '1.6.0.0' -or $prior.Publisher -cne $subject) { throw 'Earlier release install failed.' }
    $state = Join-Path $env:LOCALAPPDATA "Packages/$($prior.PackageFamilyName)/LocalState"
    [IO.Directory]::CreateDirectory($state) | Out-Null
    $marker = Join-Path $state 'native-installer-update-test.txt'
    [IO.File]::WriteAllText($marker, $prior.PackageFamilyName)
    return $marker
}

[IO.Directory]::CreateDirectory($work) | Out-Null
try {
    $buildDescriptor = Join-Path $folder 'Notepads.appinstaller'
    $entry = Join-Path $work 'Notepads.appinstaller'
    [xml]$document = Get-Content -Raw -LiteralPath $buildDescriptor
    $root = $document.DocumentElement
    $main = $root.SelectSingleNode("*[local-name()='MainBundle']")
    $packages = @($root.SelectNodes("*[local-name()='Dependencies']/*[local-name()='Package']"))
    if (!$main -or $main.GetAttribute('Name') -cne $appName -or
        $main.GetAttribute('Publisher') -cne $subject -or $packages.Count -eq 0) { throw 'Unexpected feed identity or dependencies.' }
    $feedUri = [Uri]$root.GetAttribute('Uri')
    if ($feedUri.Scheme -ne 'https') { throw 'The distributed feed must advertise a stable HTTPS URL.' }

    if ($PublishedFeedUri) {
        if ($PublishedFeedUri -ne $feedUri) { throw "Unexpected stable update URI: $feedUri" }
        Get-WebFile $PublishedFeedUri $entry
        Assert-SameFile $entry $buildDescriptor
        # Exercise the exact descriptor a browser downloads, including the public bundle and dependency addresses.
        foreach ($package in @($main) + $packages) {
            $uri = [Uri]$package.GetAttribute('Uri')
            $name = [IO.Path]::GetFileName($uri.AbsolutePath)
            $download = Join-Path $work $name
            Get-WebFile $uri $download
            Assert-SameFile $download (Join-Path $folder $name)
        }
        $cer = Join-Path $work 'Notepads.cer'
        Get-WebFile ([Uri]::new([Uri]$main.GetAttribute('Uri'), 'Notepads.cer')) $cer
        Assert-SameFile $cer (Join-Path $folder 'Notepads.cer')
        Write-Output 'PASS public HTTPS descriptor, bundle, dependencies and certificate are byte-identical to the signed build'
    } else {
        # Serve private PR assets with real HTTP semantics. Change only the origin/path of each URI.
        $testFeed = Join-Path $work 'feed'
        [IO.Directory]::CreateDirectory($testFeed) | Out-Null
        $portFile = Join-Path $work 'port'
        $helper = Join-Path $PSScriptRoot 'feed_server.py'
        $server = Start-Process -FilePath (Get-Command python.exe).Source -ArgumentList (
            '"{0}" "{1}" "{2}"' -f $helper, $testFeed, $portFile) -PassThru -WindowStyle Hidden
        $stop = [DateTime]::UtcNow.AddSeconds(15)
        while (!(Test-Path -LiteralPath $portFile) -and !$server.HasExited -and [DateTime]::UtcNow -lt $stop) {
            Start-Sleep -Milliseconds 100
        }
        $port = [int](Get-Content -Raw -LiteralPath $portFile)
        $feedUri = [Uri]"http://127.0.0.1:$port/Notepads.appinstaller"
        $root.SetAttribute('Uri', $feedUri.AbsoluteUri)
        foreach ($package in @($main) + $packages) {
            $remote = [Uri]$package.GetAttribute('Uri')
            if ($remote.Scheme -ne 'https') { throw "Unexpected source URI in distributed descriptor: $remote" }
            $name = [IO.Path]::GetFileName($remote.AbsolutePath)
            Copy-Item -LiteralPath (Join-Path $folder $name) -Destination $testFeed
            $package.SetAttribute('Uri', [Uri]::new($feedUri, $name).AbsoluteUri)
        }
        $document.Save((Join-Path $testFeed 'Notepads.appinstaller'))
        Invoke-WebRequest -UseBasicParsing -Uri $feedUri -OutFile $entry
    }

    Trust-Bundle (Join-Path $folder 'Notepads-x64.msixbundle') (Join-Path $folder 'Notepads.cer')
    $marker = if ($TestUpgradeFromFirstRelease) { Install-PreviousVersion }
    # This switch submits the descriptor to the Windows deployment engine rather than installing the bundle directly.
    Add-AppxPackage -AppInstallerFile -Path $entry
    $installed = Get-AppxPackage -Name $appName
    if (!$installed -or $installed.Publisher -cne $subject -or [version]$installed.Version -ne [version]$main.GetAttribute('Version')) {
        throw 'The native installer did not register the expected app identity/version.'
    }
    foreach ($package in $packages) {
        $framework = Get-AppxPackage -Name $package.GetAttribute('Name') -PackageTypeFilter Framework |
            Where-Object { $_.Publisher -ceq $package.GetAttribute('Publisher') -and [version]$_.Version -ge [version]$package.GetAttribute('Version') }
        if (!$framework) { throw "Missing dependency after native deployment: $($package.GetAttribute('Name'))" }
    }
    if ($marker -and [IO.File]::ReadAllText($marker) -cne $installed.PackageFamilyName) {
        throw 'The update did not preserve the earlier package family and LocalState.'
    }
    if ($marker) { Write-Output "PASS upgrade from 1.6.0.0 to $($installed.Version) preserves the package family and user data" }
    if (Get-Command Get-AppxPackageAutoUpdateSettings -ErrorAction SilentlyContinue) {
        $settings = Get-AppxPackageAutoUpdateSettings -PackageFamilyName $installed.PackageFamilyName
        $actual = [Uri]$settings.AppInstallerUri
    } else {
        $manager = [Windows.Management.Deployment.PackageManager,Windows.Management.Deployment,ContentType=WindowsRuntime]::new()
        $package = $manager.FindPackageForUser('', $installed.PackageFullName)
        $actual = $package.GetAppInstallerInfo().Uri
    }
    if ($actual -ne $feedUri) { throw "Expected update association $feedUri, got $actual" }
    Write-Output "PASS Windows installed $($installed.PackageFullName) via the descriptor and registered $actual"
} finally {
    $ours = Get-AppxPackage -Name $appName | Where-Object Publisher -CEQ $subject
    if ($ours) { Remove-AppxPackage -Package $ours.PackageFullName -ErrorAction Continue }
    foreach ($path in $certsAdded) { Remove-Item -LiteralPath $path -ErrorAction Continue }
    if ($server -and !$server.HasExited) { Stop-Process -Id $server.Id; $server.WaitForExit() }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction Continue
}
