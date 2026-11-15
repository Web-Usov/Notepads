# Privacy in the Web-Usov fork of Notepads

## Documents and local storage

Notepads reads the files you open and writes them to the locations you choose. Windows keeps the recent-file list and app preferences for this installation. If you enable session snapshots in **Settings → Advanced**, the main window stores recovery copies of unsaved changes in the app’s local data folder.

Installed extension manifests are stored under `LocalState/Extensions`. The installed copy records the local filename or path, or the source URL for a remote installation. For a GitHub repository or release, it also records the resolved revision. The editor evaluates highlighting rules, formats JSON and filters log lines on your device. [How extensions work](docs/extensions.md)

## Diagnostics

The Windows app uses the Microsoft Store Services SDK for diagnostics. It passes event names such as `AppLaunch_Settings` to the SDK. For exceptions, it passes the exception message and stack trace, which can include file paths. Debug builds also write a diagnostic log under `LocalState/Logs`. [Analytics implementation](src/Notepads/Services/AnalyticsService.cs) · [Debug logging](src/Notepads/Program.cs)

## When information leaves the editor

- Choosing **Install from URL…** downloads the manifest or ZIP over HTTPS. For a public GitHub repository or release, the installer requests package metadata from the GitHub API and follows the download URL. **Check for updates** contacts the recorded source again when you choose it.
- Opening Markdown preview can fetch images from addresses embedded in the document. Following a link opens it with the Windows handler for that address.
- The web search command opens the selected URL or sends the selected text to the configured search engine through your browser.
- Copied text is available to Windows clipboard history and clipboard sync according to your system settings.
