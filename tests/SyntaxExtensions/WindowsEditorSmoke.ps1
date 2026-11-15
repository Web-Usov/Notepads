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

# UWP may expose a flyout as a separate top-level popup owned by the editor process.
# Restrict searches to the document window and those popups, even during startup.
function Find-AppControl($Property, [string]$Value) {
    $control = Find-Control $window $Property $Value
    if ($control) { return $control }
    $popups = $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $ownedByEditor)
    foreach ($popup in $popups) {
        $control = Find-Control $popup $Property $Value
        if ($control) { return $control }
    }
}

function Invoke-Control($Control) {
    $pattern = $Control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Invoke-Extension([string]$Name) {
    Invoke-Control (Wait-For 'the main menu button' { Find-AppControl $byId 'MainMenuButton' })
    $submenu = Wait-For 'the Extension commands submenu' { Find-AppControl $byId 'MenuExtensionCommands' }
    $submenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Invoke-Control (Wait-For $Name { Find-AppControl $byName $Name })
}

function Read-Document {
    $pattern = $editor.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
    $pattern.DocumentRange.GetText(-1)
}

function Assert-Document([string]$Description, [string]$Expected) {
    $null = Wait-For $Description { [string]::Equals((Read-Document), $Expected, [StringComparison]::Ordinal) }
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
    # The application sets the window title to the active file name.
    $window = Wait-For 'the source document window' {
        $windows = $desktop.FindAll([System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($candidate in $windows) {
            if ($candidate.Current.Name -like '*extension-smoke.json*') { return $candidate }
        }
    } 40
    $editor = Wait-For 'the running RichEdit control' { Find-Control $window $byId 'TextEditorCore' }
    $ownedByEditor = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $editor.Current.ProcessId)
    # Some RichEdit providers append a final paragraph marker. Keep the exact raw text
    # for undo/redo, and allow at most one provider marker when reading the known fixture.
    $rawOriginal = Wait-For 'opening the source fixture' {
        $raw = Read-Document
        if (@($original, ($original + "`r"), ($original + "`n"), ($original + "`r`n")) -ccontains $raw) { return $raw }
    }
    Write-Output 'PASS opening the source fixture'
    $providerMarker = $rawOriginal.Substring($original.Length)

    $formatted = "{`n  `"requestId`": `"req-17`",`n  `"level`": `"error`"`n}"
    Invoke-Extension 'Format JSON'
    $rawFormatted = Wait-For 'JSON formatting in the actual editor' {
        $raw = Read-Document
        if ($raw.EndsWith($providerMarker, [StringComparison]::Ordinal)) {
            $content = $raw.Substring(0, $raw.Length - $providerMarker.Length)
            if ($content.Replace("`r`n", "`n").Replace("`r", "`n") -ceq $formatted) { return $raw }
        }
    }
    Write-Output 'PASS JSON formatting in the actual editor'
    $editor.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('^z')
    Assert-Document 'one Ctrl+Z restores the complete document' $rawOriginal
    [System.Windows.Forms.SendKeys]::SendWait('^+z')
    Assert-Document 'one Ctrl+Shift+Z restores the complete transformation' $rawFormatted
    $editor.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('^{HOME}')

    # Choose the severity field line in the real panel and inspect RichEdit's selection.
    Invoke-Extension 'Open Log Lens'
    $filter = Wait-For 'the results filter' { Find-AppControl $byName 'Filter results by request ID or literal text' }
    $filter.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('level')
    $list = Wait-For 'the Log Lens result list' { Find-AppControl $byName 'Log Lens results by source line' }
    $null = Wait-For 'Log Lens to narrow the results to one line' { Find-AppControl $byName '1 matching line.' }
    $row = Wait-For 'the matching Log Lens row' { Find-Control $list $byName '3     "level": "error"' }
    $row.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $null = Wait-For 'navigation to return keyboard focus to RichEdit' { $editor.Current.HasKeyboardFocus }
    Assert-Document 'navigation preserves document text' $rawFormatted
    # Replacing the selection distinguishes the exact source range from a caret on the same line.
    # Read the document range, which is stable across focus changes in the Windows UIA provider.
    [System.Windows.Forms.SendKeys]::SendWait('x')
    Assert-Document 'typing replaces exactly the source line selected by Log Lens' ($rawFormatted.Replace('  "level": "error"', 'x'))
    [System.Windows.Forms.SendKeys]::SendWait('^z')
    Assert-Document 'undo restores the selected source line' $rawFormatted
    Write-Output 'PASS Log Lens filters and navigates to source line 3'
    [System.Windows.Forms.SendKeys]::SendWait('^z')
    Assert-Document 'panel interactions preserved the original Undo history' $rawOriginal
    if ([IO.File]::ReadAllText($inputPath) -cne $original) { throw 'The test commands modified the saved file.' }
    Write-Output 'PASS the saved file is unchanged'
}
finally {
    if ($installed) { Remove-AppxPackage -Package $installed.PackageFullName -ErrorAction Continue }
    if ($addTrust -and (Test-Path -LiteralPath $trustPath)) { Remove-Item -LiteralPath $trustPath }
}
