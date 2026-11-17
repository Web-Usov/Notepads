# Installed Windows UI testing

Plainix is a packaged C# UWP/XAML editor. Browser-only test runners cannot test
its controls. All UI E2E tests operate on the **actual installed MSIX** in an
interactive Windows user session with FlaUI UIA3 + NUnit.

## Test layers

| Layer | Entry point | Purpose |
| --- | --- | --- |
| .NET 8 portable extension tests | tests/SyntaxExtensions/SyntaxExtensions.Tests.csproj | Manifests, grammar resolution, editor commands and plugins without Windows |
| Installed editor smoke | tests/SyntaxExtensions/WindowsEditorSmoke.ps1 | Real editor, JSON, Log Lens, install-from-URL, Undo/Redo, English/Russian |
| Installed NUnit UI E2E | tests/UI/Plainix.UI.Tests.csproj | Editor, Find, Save, settings/theme, plugin switches, layout assertions |
| Native installer smoke | tests/Installer/NativeInstallerSmoke.ps1 | App Installer descriptor, clean installation and upgrade |
| Publication guard tests | tests/Installer/PublishGuardsTests.sh | Release tag/commit identity and monotonic release feed |

The existing PowerShell smoke remains active alongside the new NUnit/FlaUI
tests; it is not replaced until equivalent coverage is verified.

## CI

The \`Windows preview installer\` workflow builds the **same** signed preview
MSIX for all installed tests. Its jobs run on hosted \`windows-2022\`:

1. \`installer\` builds the MSIX bundle and checks its signed contents.
2. \`editor-smoke\` installs the app and exercises the established workflow.
3. \`ui-tests\` installs that very artifact with its public preview test
   certificate, runs NUnit/FlaUI tests in an STA process and uninstalls it.
4. \`native-install\` exercises the Windows App Installer feed and upgrade.
5. \`publish\` depends on **all** required tests plus release guard tests.
   A failed UI job prevents new public releases.

The UI jobs run for PRs, release branches, manual dispatches and the nightly
scheduled workflow. Jobs use separate ephemeral Windows machines and never share
an interactive desktop. Tests within a UI job are non-parallel because focus,
keyboard input and the UWP profile are global resources.

A failed test is a **failure**, not a warning. NUnit writes
\`plainix-ui.trx\`; \`ui-test-results\` artifacts include diagnostics and a
screenshot for failed tests. The passing layout test records a visual snapshot
for nightly review. The structural layout assertions compare stable editor,
menu and window bounds, not OS-dependent pixel colors. Pixel-perfect golden
images are intentionally not treated as stable assertions until a fixed
resolution, display scale and approved baseline are available.

## Local reproduction

Prerequisites: Windows 10/11, .NET 8 SDK, PowerShell 5.1, and a disposable
user profile with Developer Mode and AppX deployment available. Download a
\`Plainix-Windows-x64\` artifact from the PR's \`Windows preview installer\`
workflow and extract it. **Do not run this in an existing Plainix profile**:
the runner refuses an already-installed Notepads-Dev package.

From the repo root in an elevated Windows PowerShell session:

\`\`\`powershell
powershell.exe -NoProfile -STA -File tests/UI/Run-UiTests.ps1 -PackageFolder "C:\path\to\Plainix-Windows-x64" -ResultsDirectory "$env:TEMP\plainix-ui-results"
\`\`\`

The wrapper validates the MSIX public signature against the matching
\`.cer\`, imports the preview certificate into LocalMachine/TrustedPeople,
installs the package dependencies, executes \`dotnet test\`, then cleans up
the app and temporary trust even when tests fail. The wrapper only supports
the preview identity \`CN=Web-Usov Notepads Preview\`; when migrating to an
actual SignPath identity, update the trust model and rerun release smoke
without making PR workflows depend on signing secrets.

## Adding a UI scenario

- Add an independent \`[Test]\` to \`EditorUiTests.cs\`, using \`UiTestHost\` to
  launch an isolated fixture from the installed package's LocalState.
- Prefer stable \`AutomationProperties.AutomationId\` in XAML rather than
  display text. Restore modified settings in \`finally\`.
- Use bounded waits; do not sleep for several seconds and hope the window loaded.
- Capture UI Automation details and screenshots on failure. Do not silently
  skip a test or use \`continue-on-error\` for a required check.
- Keep tests serialized **within one desktop**, but jobs may run in parallel
  on separate GitHub-hosted Windows machines.
- When extending plugin UI coverage, exercise the bundled JSON toggle now;
  Markdown Preview and Changes Diff switches are checked once their feature
  manifests from #13 are present. Missing expected manifests must fail CI.

The fixture explicitly checks for an interactive desktop. Session 0 or
non-STA execution cannot operate UWP UI controls; a successful headless
console unit-test build is not evidence of a passing UI test.
