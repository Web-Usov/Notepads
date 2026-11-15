# Notepads · Web-Usov fork

Notepads is a tabbed plain-text editor for Windows. This fork adds live JSON syntax highlighting and installable language extensions. Open a `.json` file to highlight property names, strings, numbers, literals and punctuation as you type. The Windows x64 build is distributed through [GitHub Releases](https://github.com/Web-Usov/Notepads/releases/latest).

## Install on Windows

1. On an x64 PC running Windows 10 (build 17763 or later) or Windows 11, open the [latest release](https://github.com/Web-Usov/Notepads/releases/latest). Under **Assets**, download `Notepads-<version>-Windows-x64.zip`.
2. Extract the entire ZIP. Open PowerShell in the folder containing `Add-AppDevPackage.ps1` and run the command below. Running it in an open window keeps installer messages visible.

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Add-AppDevPackage.ps1
   ```

3. Follow the prompts. The package uses a self-signed certificate, so Windows may ask for administrator approval to trust it. Search the Start menu for **Notepads-Dev** and open a JSON file.

Each release includes `SHA256SUMS.txt`. To verify the download, run `Get-FileHash .\<downloaded-archive>.zip -Algorithm SHA256` and compare the hash with the entry for that ZIP. The release notes also give the SHA-256 of the public signing certificate.

## Use the editor

- Open text files up to 1,000 KiB in tabs. Press **Ctrl+T** to create a tab and **Ctrl+Tab** to switch between tabs.
- In a Markdown file, press **Alt+P** to toggle the live preview. Press **Alt+D** to compare your edits with the saved version of a file.
- Manage highlighting in **Settings → Advanced → Extensions**. Choose **Install from file…** to import a language manifest. To try new rules, edit the installed copy and choose **Reload extensions**.

[Dark theme](ScreenShots/1.png) · [Markdown preview](ScreenShots/2.png) · [Diff viewer](ScreenShots/3.png) · [Light theme](ScreenShots/4.png)

## Create a language extension

A language extension is a UTF-8 JSON manifest. It associates filename extensions with highlighting rules. The bundled JSON grammar uses the same format as an extension you install yourself.

- [Extension authoring guide](docs/extensions.md) — create a working `.todo` extension, test its tokens and load it in the editor.
- [Bundled JSON manifest](src/Notepads/Assets/Extensions/json.notepads-extension.json) — see how the built-in grammar handles JSON keys and values.
- [Agent skill](.agents/skills/create-notepads-extension/SKILL.md) — use the repository workflow to create and verify a language extension with a coding agent.

## Project

See [Contributing](CONTRIBUTING.md) for source builds, tests, PR installers and release instructions. The [privacy notes](PRIVACY.md) describe local storage and diagnostics. The [code of conduct](CODE_OF_CONDUCT.md) applies to contributions.

This fork builds on [Notepads by Jackie (Jiaqi) Liu and its contributors](https://github.com/0x7c13/Notepads). Yi Zhou and Mahmoud Qurashy contributed the app and file icon artwork. See the [MIT license](LICENSE.txt).
