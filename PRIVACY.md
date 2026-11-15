# Privacy in the Web-Usov fork of Notepads

## Documents and local storage

Notepads reads the files you open and writes them to the locations you choose. Windows keeps the recent-file list and app preferences for this installation. If you enable session snapshots in **Settings → Advanced**, the main window stores recovery copies of unsaved changes in the app’s local data folder.

Language extensions are JSON files. Installing a manifest copies it to `LocalState/Extensions`. The app evaluates its highlighting rules against the open document on your device. [How extensions work](docs/extensions.md)

## Diagnostics

The Windows app uses the Microsoft Store Services SDK for diagnostics. It passes event names such as `AppLaunch_Settings` to the SDK. For exceptions, it passes the exception message and stack trace, which can include file paths. Debug builds also write a diagnostic log under `LocalState/Logs`. [Analytics implementation](src/Notepads/Services/AnalyticsService.cs) · [Debug logging](src/Notepads/Program.cs)

## When information leaves the editor

- Opening Markdown preview can fetch images from addresses embedded in the document. Following a link opens it with the Windows handler for that address.
- The web search command opens the selected URL or sends the selected text to the configured search engine through your browser.
- Copied text is available to Windows clipboard history and clipboard sync according to your system settings.
