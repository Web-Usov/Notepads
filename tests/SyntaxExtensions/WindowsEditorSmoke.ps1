# Run in Windows PowerShell 5.1 (STA) on a disposable, interactive Windows desktop
# with a clean user profile. Pass the SDK sideload directory produced by the installer job.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageFolder)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (![Environment]::UserInteractive -or (Get-Process -Id $PID).SessionId -eq 0) {
    throw 'The editor smoke test requires an interactive desktop. Session 0 cannot drive UWP controls.'
}
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Start Windows PowerShell with -STA for the UI Automation smoke test.'
}
if (Get-AppxPackage -Name 'Notepads-Dev') {
    throw 'Use a clean runner profile. The preview app must not already be installed.'
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$subtree = [System.Windows.Automation.TreeScope]::Descendants
$byId = [System.Windows.Automation.AutomationElement]::AutomationIdProperty
$byName = [System.Windows.Automation.AutomationElement]::NameProperty

function Wait-For([string]$Description, [scriptblock]$Probe, [int]$Seconds = 20) {
    $until = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        try { $value = & $Probe; if ($value) { return $value } }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $until)
    throw "Timed out waiting for $Description."
}

function Find-Control($Root, $Property, [string]$Value) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($Property, $Value)
    $Root.FindFirst($subtree, $condition)
}

function Invoke-Control($Control) {
    $pattern = $Control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Invoke-Extension([string]$Name) {
    Invoke-Control (Wait-For 'the main menu button' { Find-Control $desktop $byId 'MainMenuButton' })
    $submenu = Wait-For 'the Extension commands submenu' { Find-Control $desktop $byId 'MenuExtensionCommands' }
    $submenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Invoke-Control (Wait-For $Name { Find-Control $desktop $byName $Name })
}

# RichEdit's UI Automation provider includes a final paragraph marker in some Windows builds.
# The fixture has no trailing newline. Normalize only for the text assertions below.
function Read-Document {
    $pattern = $editor.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
    $pattern.DocumentRange.GetText(-1).TrimEnd([char[]]"`r`n").Replace("`r`n", "`n").Replace("`r", "`n")
}

function Assert-Document([string]$Description, [string]$Expected) {
    $null = Wait-For $Description { (Read-Document) -ceq $Expected }
    Write-Output "PASS $Description"
}

$folder = (Resolve-Path -LiteralPath $PackageFolder).Path
$bundles = @(Get-ChildItem -LiteralPath $folder -Filter '*.msixbundle')
if ($bundles.Count -ne 1) { throw 'Expected exactly one MSIX bundle.' }
$bundle = $bundles[0]
$certPath = [IO.Path]::ChangeExtension($bundle.FullName, '.cer')
$cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2($certPath)
$signature = Get-AuthenticodeSignature -LiteralPath $bundle.FullName
if ($cert.Subject -ne 'CN=Web-Usov Notepads Preview' -or
    !$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint) {
    throw 'Bundle and certificate must have the fork preview signing identity.'
}
$trustPath = "Cert:\LocalMachine\TrustedPeople\$($cert.Thumbprint)"
$addTrust = !(Test-Path -LiteralPath $trustPath)
$installed = $null
try {
    if ($addTrust) { Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null }
    $dependencies = @(Get-ChildItem -LiteralPath (Join-Path $folder 'Dependencies\x64') -Filter '*.appx')
    if (!$dependencies.Count) { throw 'x64 package dependencies are missing.' }
    Add-AppxPackage -Path $bundle.FullName -DependencyPath $dependencies.FullName
    $installed = Get-AppxPackage -Name 'Notepads-Dev'
    if (!$installed -or $installed.Publisher -ne $cert.Subject) { throw 'Unexpected installed application.' }

    # Opening a file through the app's own alias exercises its normal command-line entry point.
    # Use LocalState so the app has access to the test file even when broad filesystem access is disabled.
    $data = Join-Path $env:LOCALAPPDATA "Packages\$($installed.PackageFamilyName)\LocalState"
    $null = [IO.Directory]::CreateDirectory($data)
    $inputPath = Join-Path $data 'extension-smoke.json'
    $original = '{"requestId":"req-17","level":"error"}'
    [IO.File]::WriteAllText($inputPath, $original, [Text.UTF8Encoding]::new($false))
    $alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\Notepads-Dev.exe'
    $null = Wait-For 'registration of the application execution alias' { Test-Path -LiteralPath $alias }
    Start-Process -FilePath $alias -ArgumentList ('"' + $inputPath + '"')
    $editor = Wait-For 'the running RichEdit control' { Find-Control $desktop $byId 'TextEditorCore' } 40
    Assert-Document 'opening the source fixture' $original

    $formatted = "{`n  `"requestId`": `"req-17`",`n  `"level`": `"error`"`n}"
    Invoke-Extension 'Format JSON'
    Assert-Document 'JSON formatting in the actual editor' $formatted
    $editor.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('^z')
    Assert-Document 'one Ctrl+Z restores the complete document' $original
    [System.Windows.Forms.SendKeys]::SendWait('^+z')
    Assert-Document 'one Ctrl+Shift+Z restores the complete transformation' $formatted
    $editor.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('^{HOME}')

    # Choose the severity field line in the real panel and inspect RichEdit's selection.
    Invoke-Extension 'Open Log Lens'
    $filter = Wait-For 'the results filter' { Find-Control $desktop $byName 'Filter results by request ID or literal text' }
    $filter.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('level')
    $list = Wait-For 'the Log Lens result list' { Find-Control $desktop $byName 'Log Lens results by source line' }
    $row = Wait-For 'the matching Log Lens row' { Find-Control $list $byName '3     "level": "error"' }
    $row.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $null = Wait-For 'navigation to the original line in RichEdit' {
        $ranges = $editor.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern).GetSelection()
        $ranges.Count -eq 1 -and $ranges[0].GetText(-1) -ceq '  "level": "error"'
    }
    Write-Output 'PASS Log Lens filters and navigates to source line 3'
    Assert-Document 'navigation preserves document text' $formatted
    if ([IO.File]::ReadAllText($inputPath) -cne $original) { throw 'The test commands modified the saved file.' }
    Write-Output 'PASS the saved file is unchanged'
}
finally {
    if ($installed) { Remove-AppxPackage -Package $installed.PackageFullName -ErrorAction Continue }
    if ($addTrust -and (Test-Path -LiteralPath $trustPath)) { Remove-Item -LiteralPath $trustPath }
}
