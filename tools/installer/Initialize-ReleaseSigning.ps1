# Run with GitHub CLI signed in as a maintainer on your Windows PC.
[CmdletBinding()]
param(
    [string]$PfxPath = (Join-Path $env:LOCALAPPDATA 'Plainix\Signing\Plainix.pfx')
)

$ErrorActionPreference = 'Stop'
$repo = 'Web-Usov/Notepads'
$environment = 'notepads-release'
$endpoint = "repos/$repo/environments/$environment"
$subject = 'CN=Web-Usov Notepads Preview'
$OutputEncoding = [Text.UTF8Encoding]::new($false)

function Invoke-GitHub([string[]]$Arguments) {
    $result = & gh @Arguments
    if ($LASTEXITCODE) { throw "GitHub CLI failed. Check your access to $repo and its release environment." }
    $result
}

$names = @(Invoke-GitHub @('secret', 'list', '--repo', $repo, '--env', $environment, '--json', 'name', '--jq', '.[].name'))
$needPfx = 'NOTEPADS_RELEASE_PFX_BASE64' -notin $names
$needPassword = 'NOTEPADS_RELEASE_PFX_PASSWORD' -notin $names
if (!$needPfx -and !$needPassword) {
    Write-Output 'Both signing secrets already exist. You can retry the release.'
    return
}

# Apply the documented branch restriction to a newly auto-created environment.
# Leave any pre-existing reviewer, timer, or protected-branch rules to its owner.
$config = (Invoke-GitHub @('api', $endpoint)) | ConvertFrom-Json
if (!$config.deployment_branch_policy) {
    if ($config.protection_rules) { throw "Configure deployment branches for $environment in GitHub Settings, then rerun." }
    '{"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}' |
        gh api --method PUT $endpoint --input - --silent
    if ($LASTEXITCODE) { throw 'Could not restrict the release environment.' }
    $config = (Invoke-GitHub @('api', $endpoint)) | ConvertFrom-Json
}
if ($config.deployment_branch_policy.custom_branch_policies) {
    $patterns = @(Invoke-GitHub @('api', '--paginate', "$endpoint/deployment-branch-policies", '--jq', '.branch_policies[] | [.type,.name] | @tsv'))
    if (!$patterns.Count) {
        Invoke-GitHub @('api', '--method', 'POST', "$endpoint/deployment-branch-policies", '-f', 'name=release/v*', '-f', 'type=branch', '--silent')
    } elseif (@($patterns | Where-Object { !$_.StartsWith("branch`trelease/v", [StringComparison]::Ordinal) }).Count) {
        throw "Review the existing branch and tag rules for $environment before granting access to the release key."
    }
}

$PfxPath = [IO.Path]::GetFullPath($PfxPath)
$existing = Test-Path -LiteralPath $PfxPath -PathType Leaf
if (!$existing -and $PSBoundParameters.ContainsKey('PfxPath')) {
    throw 'Supply an existing backup with -PfxPath. For a new key, omit -PfxPath to save it in your local application data.'
}
if (!$existing -and (!$needPfx -or !$needPassword)) {
    throw 'One signing secret already exists. Supply its original PFX backup with -PfxPath to complete the pair.'
}

$password = Read-Host 'Password for the encrypted signing PFX (keep it in your password manager)' -AsSecureString
try {
    if (!$password.Length) { throw 'The PFX requires a password.' }
    if (!$existing) {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($PfxPath)) | Out-Null
        $cert = New-SelfSignedCertificate -Type Custom -Subject $subject -CertStoreLocation Cert:\CurrentUser\My `
            -KeyUsage DigitalSignature -KeyExportPolicy Exportable -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(5) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $password -ChainOption EndEntityCertOnly `
            -CryptoAlgorithmOption AES256_SHA256 -NoClobber | Out-Null
    }
    $signer = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $PfxPath, $password, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    try {
        if (!$signer.HasPrivateKey -or $signer.Subject -cne $subject -or (Get-Date) -lt $signer.NotBefore -or (Get-Date) -ge $signer.NotAfter) {
            throw 'The backup must contain a current signing key for the existing Plainix publisher.'
        }
        foreach ($extension in $signer.Extensions) {
            if ($extension -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] -and
                '1.3.6.1.5.5.7.3.3' -notin $extension.EnhancedKeyUsages.Value) { throw 'The certificate does not permit code signing.' }
            if ($extension -is [Security.Cryptography.X509Certificates.X509KeyUsageExtension] -and
                !($extension.KeyUsages -band [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature)) {
                throw 'The certificate does not permit digital signatures.'
            }
        }
    } finally { $signer.Dispose() }
    if ($needPfx) {
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($PfxPath)) | gh secret set NOTEPADS_RELEASE_PFX_BASE64 --repo $repo --env $environment
        if ($LASTEXITCODE) { throw 'Could not upload the signing PFX.' }
    }
    if ($needPassword) {
        [Net.NetworkCredential]::new('', $password).Password | gh secret set NOTEPADS_RELEASE_PFX_PASSWORD --repo $repo --env $environment
        if ($LASTEXITCODE) { throw 'Could not upload the PFX password. Retry with the same PFX and password.' }
    }
} finally { $password.Dispose() }
Write-Output "Configured release signing. Keep a backup of $PfxPath and its password for future releases."
