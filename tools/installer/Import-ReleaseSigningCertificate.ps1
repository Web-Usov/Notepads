[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Publisher
)

$ErrorActionPreference = 'Stop'
$encoded = $env:NOTEPADS_RELEASE_PFX_BASE64
$password = $env:NOTEPADS_RELEASE_PFX_PASSWORD
# SignTool only needs the certificate store. Keep secrets out of its environment.
Remove-Item Env:NOTEPADS_RELEASE_PFX_BASE64, Env:NOTEPADS_RELEASE_PFX_PASSWORD -ErrorAction SilentlyContinue
if ([string]::IsNullOrWhiteSpace($encoded) -or [string]::IsNullOrEmpty($password)) {
    throw 'Configure NOTEPADS_RELEASE_PFX_BASE64 and NOTEPADS_RELEASE_PFX_PASSWORD in the notepads-release environment. See tools/installer/README.md.'
}

$bytes = $null
$preview = $null
$signer = $null
$store = $null
try {
    $bytes = [Convert]::FromBase64String($encoded)
    $encoded = $null
    # Validate the leaf with a temporary key before persisting it in CurrentUser\My.
    $preview = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $bytes, $password, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    if (!$preview.HasPrivateKey -or $preview.Subject -cne $Publisher) {
        throw "The release PFX must contain the private key for publisher '$Publisher'."
    }
    $now = [DateTime]::UtcNow
    if ($now -lt $preview.NotBefore.ToUniversalTime() -or $now -gt $preview.NotAfter.ToUniversalTime()) {
        throw 'The release signing certificate is outside its validity period.'
    }
    foreach ($extension in $preview.Extensions) {
        if ($extension -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] -and
            '1.3.6.1.5.5.7.3.3' -notin $extension.EnhancedKeyUsages.Value) {
            throw 'The release certificate does not permit code signing.'
        }
        if ($extension -is [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension] -and
            !($extension.KeyUsages -band [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature)) {
            throw 'The release certificate does not permit digital signatures.'
        }
    }
    if (Test-Path "Cert:\CurrentUser\My\$($preview.Thumbprint)") {
        throw 'The release signing certificate is already installed. Use a fresh signing runner.'
    }
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new('My', 'CurrentUser')
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    $signer = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $bytes, $password, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]'UserKeySet,PersistKeySet')
    $store.Add($signer)
    # The caller owns the installed key and deletes it after signing in a finally block.
    $signer
}
finally {
    if ($null -ne $store) { $store.Close() }
    if ($null -ne $preview) { $preview.Dispose() }
    if ($null -ne $bytes) { [Array]::Clear($bytes, 0, $bytes.Length) }
    $password = $null
    $encoded = $null
}
