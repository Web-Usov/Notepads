namespace Notepads.SyntaxExtensions
{
    // Built-in UI features exposed through API v2 manifests. These handlers do not
    // load external binaries: UWP controls and their dependencies ship with Plainix.
    internal static class EditorFeatures
    {
        internal const string MarkdownPreview = "markdown.preview";
        internal const string ChangesDiff = "changes.diff";
        internal const string MarkdownPreviewExtensionId = "plainix.markdown-preview";
        internal const string ChangesDiffExtensionId = "plainix.changes-diff";

        internal static bool IsOwnedBy(string handler, string extensionId)
        {
            return (handler == MarkdownPreview && extensionId == MarkdownPreviewExtensionId) ||
                   (handler == ChangesDiff && extensionId == ChangesDiffExtensionId);
        }
    }
