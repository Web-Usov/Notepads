# Notepads · Web-Usov fork

Notepads is a tabbed plain-text editor for Windows. This fork supports installable extensions and live JSON highlighting. Download a Windows x64 build from [GitHub Releases](https://github.com/Web-Usov/Notepads/releases/latest).

This guide covers the 1.7.0.2 source tree, including JSON Tools, Log Lens, Russian plugin labels and ZIP or URL installation. The [1.6.0.0 release](https://github.com/Web-Usov/Notepads/releases/tag/v1.6.0.0) supports JSON highlighting and local grammar imports. To try 1.7 before a release installer is available, use the [source or PR build instructions](CONTRIBUTING.md).

## Install on Windows

Download **Notepads.appinstaller** from the release you want to install. Windows App Installer downloads the signed editor and its x64 frameworks over HTTPS. The preview has its own version-specific download. [Notepads 1.7.0.2 native installer preview](https://github.com/Web-Usov/Notepads/releases/tag/v1.7.0.2-preview)

1. On first install with this signer, download **Notepads.cer** from the same release. Compare its SHA-256 with the release notes. Open the certificate and choose **Install Certificate → Local Machine → Place all certificates in the following store → Trusted People**. This one-time step for each certificate requires Windows administrator approval.
2. Download and double-click **Notepads.appinstaller**, then choose **Install** or **Update**. Open **Notepads-Dev** from Start. Existing fork installations keep their package identity and local data.
3. A stable feed checks for updates when the app starts. A preview points to its own version. When moving to a stable build with a different signing certificate, trust its public certificate once and open its descriptor.

Windows 10 build 17763 or later and Windows 11 x64 are supported. Windows App Installer must be installed. On Windows 10 versions before 2004, enable sideloading of trusted apps in Windows settings. The [v1.6.0.0 release](https://github.com/Web-Usov/Notepads/releases/tag/v1.6.0.0) and offline installations use the complete ZIP. Extract it, open PowerShell in its folder and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Add-AppDevPackage.ps1
```

## Use the editor

To use Russian, open **Settings → Advanced → Language Preferences**, select **русский** and restart Notepads. Select **System Default** in the same list to use the Windows language preference again.

- Open text files up to 1,000 KiB in tabs. Press **Ctrl+T** to create a tab and **Ctrl+Tab** to switch between tabs.
- In a Markdown file, press **Alt+P** to toggle the live preview. Press **Alt+D** to compare your edits with the saved version of a file.
- Open a `.json` file for live highlighting. In **Main menu → Extension commands**, choose **Format JSON** or **Minify JSON** for the current selection or the whole document.
- Select a request ID in a log, then choose **Main menu → Extension commands → Open Log Lens**. Filter by severity and select a result to jump to its original line.
- Install and manage extensions under **Settings → Advanced → Extensions**. Choose a local JSON manifest or ZIP, or enter an HTTPS link to a manifest, ZIP or public GitHub repository or release. See [installation and updates](docs/extensions.md#install-update-and-distribute).

[Dark theme](ScreenShots/1.png) · [Markdown preview](ScreenShots/2.png) · [Diff viewer](ScreenShots/3.png) · [Light theme](ScreenShots/4.png)

## Create an extension

A UTF-8 JSON manifest can declare highlighting rules, commands and read-only panels. The [plugins directory](plugins/) contains the bundled JSON Tools and Log Lens plugins and a separately installable Markdown example.

- [Extension authoring guide](docs/extensions.md) — build a `.todo` grammar, add an editor command, or package an extension for GitHub.
- [Wrap in bold](plugins/examples/markdown-wrap/wrap.notepads-extension.json) — a working command that adds Markdown delimiters to the current selection.
- [Agent skill](.agents/skills/create-notepads-extension/SKILL.md) — use the repository workflow to create and verify a manifest with a coding agent.

## Project

See [Contributing](CONTRIBUTING.md) for source builds, tests, PR installers and release instructions. Coding agents can start with the [repository instructions](AGENTS.md). The [privacy notes](PRIVACY.md) describe local storage and diagnostics. The [code of conduct](CODE_OF_CONDUCT.md) applies to contributions.

This fork builds on [Notepads by Jackie (Jiaqi) Liu and its contributors](https://github.com/0x7c13/Notepads). Yi Zhou and Mahmoud Qurashy contributed the app and file icon artwork. See the [MIT license](LICENSE.txt).
