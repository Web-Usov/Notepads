# Notepads · Web-Usov fork

Notepads is a tabbed plain-text editor for Windows. This fork supports installable extensions and live JSON highlighting. Download a Windows x64 build from [GitHub Releases](https://github.com/Web-Usov/Notepads/releases/latest).

This guide covers the 1.7.0.1 source tree, including JSON Tools, Log Lens, Russian plugin labels and ZIP or URL installation. The [1.6.0.0 release](https://github.com/Web-Usov/Notepads/releases/tag/v1.6.0.0) supports JSON highlighting and local grammar imports. To try 1.7 before a release installer is available, use the [source or PR build instructions](CONTRIBUTING.md).

## Install on Windows

1. On an x64 PC running Windows 10 (build 17763 or later) or Windows 11, open the [latest release](https://github.com/Web-Usov/Notepads/releases/latest). Under **Assets**, download `Notepads-<version>-Windows-x64.zip`.
2. Extract the entire ZIP. Open PowerShell in the folder containing `Add-AppDevPackage.ps1` and run the command below. Running it in an open window keeps installer messages visible.

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Add-AppDevPackage.ps1
   ```

3. Follow the prompts. The package uses a self-signed certificate, so Windows may ask for administrator approval to trust it. Search the Start menu for **Notepads-Dev** and open a JSON file.

Each release includes `SHA256SUMS.txt`. To verify the download, run `Get-FileHash .\<downloaded-archive>.zip -Algorithm SHA256` and compare the hash with the entry for that ZIP. The release notes also give the SHA-256 of the public signing certificate.

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
