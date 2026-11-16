# Produce the public Windows App Installer feed from the signed SDK sideload output.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackageFolder,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][uri] $FeedUri,
    [Parameter(Mandatory)][uri] $AssetBaseUri
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($address in $FeedUri, $AssetBaseUri) {
    if (!$address.IsAbsoluteUri -or $address.Scheme -ne 'https' -or
        $address.UserInfo -or $address.Query -or $address.Fragment) {
        throw "Use an absolute HTTPS URI with no credentials, query, or fragment: $address"
    }
}
$assetDirectory = [uri]($AssetBaseUri.AbsoluteUri.TrimEnd('/') + '/')

function Read-ZipXml($Archive, [string] $Name) {
    $entry = $Archive.GetEntry($Name)
    if (!$entry) { throw "Missing package metadata: $Name" }
    $stream = $entry.Open()
    try {
        $xml = [System.Xml.XmlDocument]::new()
        $xml.XmlResolver = $null
        $xml.Load($stream)
        return $xml
    } finally {
        $stream.Dispose()
    }
}

function Add-FeedElement($Parent, [string] $Name, [System.Collections.IDictionary] $Attributes) {
    $node = $Parent.OwnerDocument.CreateElement($Name, $Parent.NamespaceURI)
    foreach ($key in $Attributes.Keys) { $node.SetAttribute($key, [string]$Attributes[$key]) }
    return $Parent.AppendChild($node)
}

$bundles = @(Get-ChildItem -LiteralPath $PackageFolder -Filter '*.msixbundle' -File)
if ($bundles.Count -ne 1) { throw 'Expected a single signed MSIX bundle in PackageFolder.' }
$bundle = $bundles[0]
$certificate = Get-Item -LiteralPath ([System.IO.Path]::ChangeExtension($bundle.FullName, '.cer'))

$archive = [System.IO.Compression.ZipFile]::OpenRead($bundle.FullName)
try {
    $manifest = Read-ZipXml $archive 'AppxMetadata/AppxBundleManifest.xml'
    $identity = $manifest.Bundle.Identity
    $app = @($manifest.Bundle.Packages.Package | Where-Object {
        $_.GetAttribute('Type') -eq 'application' -and $_.GetAttribute('Architecture') -eq 'x64'
    })
    if ($app.Count -ne 1) { throw 'Expected one x64 application in the bundle.' }
    $stream = $archive.GetEntry($app[0].GetAttribute('FileName')).Open()
    try {
        $application = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Read, $true)
        try { $appManifest = Read-ZipXml $application 'AppxManifest.xml' }
        finally { $application.Dispose() }
    } finally {
        $stream.Dispose()
    }
} finally {
    $archive.Dispose()
}

# Use package identities, rather than filenames, to match dependencies and their minimum versions.
$frameworks = @{}
$dependencyDirectory = Join-Path $PackageFolder 'Dependencies/x64'
foreach ($file in Get-ChildItem -LiteralPath $dependencyDirectory -File -Filter '*.appx') {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
    try { $metadata = Read-ZipXml $archive 'AppxManifest.xml' }
    finally { $archive.Dispose() }
    $frameworkIdentity = $metadata.Package.Identity
    if ($frameworkIdentity.GetAttribute('ProcessorArchitecture') -notin @('x64', 'neutral')) { continue }
    $name = $frameworkIdentity.GetAttribute('Name')
    if ($frameworks.ContainsKey($name)) { throw "Ambiguous SDK framework package: $name" }
    $frameworks[$name] = @{ Identity = $frameworkIdentity; File = $file; Manifest = $metadata }
}

$document = [System.Xml.XmlDocument]::new()
$root = $document.CreateElement('AppInstaller', 'http://schemas.microsoft.com/appx/appinstaller/2018')
[void]$document.AppendChild($root)
$root.SetAttribute('Version', $identity.GetAttribute('Version'))
$root.SetAttribute('Uri', $FeedUri.AbsoluteUri)
[void](Add-FeedElement $root 'MainBundle' ([ordered]@{
    Name = $identity.GetAttribute('Name')
    Publisher = $identity.GetAttribute('Publisher')
    Version = $identity.GetAttribute('Version')
    Uri = [uri]::new($assetDirectory, 'Plainix-x64.msixbundle').AbsoluteUri
}))

$files = [ordered]@{ 'Plainix-x64.msixbundle' = $bundle; 'Plainix.cer' = $certificate }
$pending = [System.Collections.Generic.Queue[System.Xml.XmlDocument]]::new()
$pending.Enqueue($appManifest)
$seen = @{}
$dependencies = $null
while ($pending.Count -gt 0) {
    $metadata = $pending.Dequeue()
    foreach ($required in $metadata.SelectNodes("/*[local-name()='Package']/*[local-name()='Dependencies']/*[local-name()='PackageDependency']")) {
        $name = $required.GetAttribute('Name')
        $framework = $frameworks[$name]
        if (!$framework) { throw "Missing SDK framework package: $name" }
        $id = $framework.Identity
        $publisher = $required.GetAttribute('Publisher')
        $minimum = $required.GetAttribute('MinVersion')
        if (($publisher -and $id.GetAttribute('Publisher') -cne $publisher) -or
            ($minimum -and [version]$id.GetAttribute('Version') -lt [version]$minimum)) {
            throw "The provided framework does not satisfy the dependency on $name."
        }
        if ($seen[$name]) { continue }
        $seen[$name] = $true
        $pending.Enqueue($framework.Manifest)
        if (!$dependencies) { $dependencies = Add-FeedElement $root 'Dependencies' @{} }
        [void](Add-FeedElement $dependencies 'Package' ([ordered]@{
            Name = $name
            Publisher = $id.GetAttribute('Publisher')
            Version = $id.GetAttribute('Version')
            ProcessorArchitecture = $id.GetAttribute('ProcessorArchitecture')
            Uri = [uri]::new($assetDirectory, [uri]::EscapeDataString($framework.File.Name)).AbsoluteUri
        }))
        $files.Add($framework.File.Name, $framework.File)
    }
}
$settings = Add-FeedElement $root 'UpdateSettings' @{}
[void](Add-FeedElement $settings 'OnLaunch' @{ HoursBetweenUpdateChecks = '0' })

$directory = [System.IO.Directory]::CreateDirectory($OutputDirectory)
# Never leave unrelated or stale files in the directory a release job will upload.
foreach ($existing in Get-ChildItem -LiteralPath $directory.FullName -Force) {
    if ($existing.PSIsContainer -or
        ($existing.Name -notin @('Plainix.appinstaller', 'Notepads.appinstaller') -and !$files.Contains($existing.Name))) {
        throw "Unexpected file in feed output directory: $($existing.Name)"
    }
}
foreach ($name in $files.Keys) {
    Copy-Item -LiteralPath $files[$name].FullName -Destination (Join-Path $directory.FullName $name) -Force
}
$xmlSettings = [System.Xml.XmlWriterSettings]::new()
$xmlSettings.Indent = $true
$xmlSettings.Encoding = [System.Text.UTF8Encoding]::new($false)
# Each entry point advertises its own URI so previously registered update feeds stay valid.
foreach ($name in 'Plainix.appinstaller', 'Notepads.appinstaller') {
    $root.SetAttribute('Uri', [uri]::new($FeedUri, $name).AbsoluteUri)
    $descriptor = Join-Path $directory.FullName $name
    $writer = [System.Xml.XmlWriter]::Create($descriptor, $xmlSettings)
    try { $document.Save($writer) }
    finally { $writer.Dispose() }
    Write-Output "Created $descriptor with $($seen.Count) framework packages."
}
