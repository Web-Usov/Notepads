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

    # Exercise the user-facing installer with the exact v2 example shipped in this checkout.
    # Pin the URL to HEAD rather than master or GITHUB_SHA (a PR job can use a synthetic merge SHA).
    $root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
    $manifestPath = 'docs/examples/wrap.notepads-extension.json'
    $fixture = [IO.File]::ReadAllText((Join-Path $root $manifestPath)) | ConvertFrom-Json
    $sha = git -C $root rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[a-f0-9]{40}$') { throw 'Could not resolve the fixture commit.' }
    $source = "https://raw.githubusercontent.com/Web-Usov/Notepads/$sha/$manifestPath"
    $storedFile = Join-Path $data "Extensions\ext-$($fixture.id).notepads-extension.json"
    if (Test-Path -LiteralPath $storedFile) { throw 'The example extension is already installed.' }
    Write-Output "Verifying the v2 package installer with $source"

    Invoke-Control (Wait-For 'the main menu button' { Find-AppControl $byId 'MainMenuButton' })
    Invoke-Control (Wait-For 'Settings in the main menu' { Find-AppControl $byId 'MenuSettingsButton' })
    $navigation = Wait-For 'the settings navigation view' { Find-AppControl $byId 'SettingsNavigationView' }
    $advanced = Wait-For 'the Advanced settings item' { Find-Control $navigation $byName 'Advanced' }
    $advanced.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $urlButton = Wait-For 'Install from URL in Advanced settings' {
        Find-AppControl $byName ('Install from URL' + [char]0x2026)
    }
    Invoke-Control $urlButton
    $urlInput = Wait-For 'the extension source URL input' {
        $box = Find-AppControl $byName 'Manifest, ZIP or GitHub repository URL'
        if ($box -and $box.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit) { return $box }
        # Some UWP versions expose the header as a separate peer. Inspect its labeled
        # dialog's edit field rather than matching the hidden document editor.
        $show = Find-AppControl $byName 'Show details'
        if (!$show) { return }
        $peer = $show
        while ($peer) {
            $peer = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($peer)
            if (!$peer -or $peer.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) { break }
            $condition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
            $edits = $peer.FindAll($subtree, $condition)
            if ($edits.Count -eq 1) { return $edits[0] }
        }
    }
    $urlInput.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($source)
    $previewButton = Wait-For 'the enabled preview action' {
        $button = Find-AppControl $byName 'Show details'
        if ($button -and $button.Current.IsEnabled) { return $button }
    }
    Invoke-Control $previewButton
    # The details TextBlock exposes its entire text as its accessible name. Line endings
    # in a .resw are normalized by XML; match the actual fields across LF/CRLF forms.
    $previewText = ('{0}\s+ID:\s*{1}\s+Version:\s*{2}\s+Source:\s*{3}' -f
        [regex]::Escape($fixture.displayName), [regex]::Escape($fixture.id),
        [regex]::Escape($fixture.version), [regex]::Escape($source))
    $null = Wait-For 'the preview displaying the expected name, ID, version and source' {
        $status = Find-AppControl $byId 'ExtensionStatus'
        if ($status -and $status.Current.Name -like 'Extension operation failed:*') { throw $status.Current.Name }
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
        $roots = @($window) + @($desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $ownedByEditor))
        foreach ($owner in $roots) {
            foreach ($label in $owner.FindAll($subtree, $condition)) {
                if ($label.Current.Name -match $previewText) { return $true }
            }
        }
    } 45
    if (Test-Path -LiteralPath $storedFile) { throw 'The package was installed before confirmation.' }
    Write-Output "PASS confirmation preview for $($fixture.id) $($fixture.version)"

    Invoke-Control (Wait-For 'the Install confirmation button' { Find-AppControl $byName 'Install' })
    $null = Wait-For 'the installed package manifest' { Test-Path -LiteralPath $storedFile }
    $stored = [IO.File]::ReadAllText($storedFile) | ConvertFrom-Json
    if ($stored.apiVersion -ne 2 -or $stored.id -cne $fixture.id -or $stored.version -cne $fixture.version -or
        $stored._notepadsInstall.remote -ne $true -or $stored._notepadsInstall.source -cne $source) {
        throw 'The installed extension or its recorded update source does not match the confirmed package.'
    }
    $toggle = Wait-For 'the newly registered extension' {
        Find-AppControl $byName ($fixture.displayName + '  ' + $fixture.version)
    }
    $state = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState
    if ($state -ne [System.Windows.Automation.ToggleState]::On) { throw 'The imported extension is not enabled.' }
    Write-Output 'PASS installed v2 extension, registered enabled state and persisted source receipt'
}
finally {
    if ($installed) { Remove-AppxPackage -Package $installed.PackageFullName -ErrorAction Continue }
    if ($addTrust -and (Test-Path -LiteralPath $trustPath)) { Remove-Item -LiteralPath $trustPath }
}
