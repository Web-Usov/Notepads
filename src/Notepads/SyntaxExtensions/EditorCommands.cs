namespace Notepads.SyntaxExtensions
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    // All offsets use UTF-16 indices into Text. The UWP editor supplies one CR per paragraph.
    internal sealed class EditorSnapshot
    {
        public EditorSnapshot(Guid documentId, long revision, string text, int selectionStart, int selectionLength)
        {
            if (text == null || selectionStart < 0 || selectionLength < 0 || selectionStart > text.Length - selectionLength)
                throw new ArgumentOutOfRangeException(nameof(selectionStart));
            DocumentId = documentId;
            Revision = revision;
            Text = text;
            SelectionStart = selectionStart;
            SelectionLength = selectionLength;
        }

        public Guid DocumentId { get; }
        public long Revision { get; }
        public string Text { get; }
        public int SelectionStart { get; }
        public int SelectionLength { get; }

        public bool Matches(Guid documentId, long revision) => documentId == DocumentId && revision == Revision;

        public bool Contains(int start, int length) => start >= 0 && length >= 0 && start <= Text.Length - length;
    }

    internal sealed class EditorCommandResult
    {
        private EditorCommandResult(int start, int length, string text, string message)
        {
            Start = start;
            Length = length;
            Text = text;
            Message = message;
        }

        public int Start { get; }
        public int Length { get; }
        public string Text { get; }
        public string Message { get; }
        public static EditorCommandResult Replace(int start, int length, string text) =>
            new EditorCommandResult(start, length, text ?? throw new ArgumentNullException(nameof(text)), null);
        public static EditorCommandResult Error(int start, int length, string message) =>
            new EditorCommandResult(start, length, null, message ?? throw new ArgumentNullException(nameof(message)));

        public bool Apply(EditorSnapshot snapshot, Guid activeDocument, long revision,
            Action<int, int, string> replaceAsOneUndo, Action<int, int, string> showError)
        {
            if (!snapshot.Matches(activeDocument, revision) || !snapshot.Contains(Start, Length)) return false;
            if (Message != null)
                showError(Start, Length, Message);
            else if (Length != Text.Length || string.CompareOrdinal(snapshot.Text, Start, Text, 0, Length) != 0)
                replaceAsOneUndo(Start, Length, Text);
            return true;
        }
    }

    internal sealed class EditorPanelRow
    {
        public EditorPanelRow(int lineNumber, int start, int length, string text)
        {
            LineNumber = lineNumber;
            Start = start;
            Length = length;
            Text = text;
        }

        public int LineNumber { get; }
        public int Start { get; }
        public int Length { get; }
        public string Text { get; }
        public string Label => LineNumber + "   " + Text;
    }

    internal sealed class ExtensionPanel
    {
        public ExtensionPanel(string id, string title, string handler)
        {
            Id = id;
            Title = title;
            Handler = handler;
        }

        public string Id { get; }
        public string Title { get; }
        public string Handler { get; }

        public IReadOnlyList<EditorPanelRow> Query(EditorSnapshot snapshot, string filter, string severity, CancellationToken token)
        {
            switch (Handler)
            {
                case "log.filter": return LogLens.Query(snapshot, filter, severity, token);
                default: throw new InvalidOperationException("Unknown panel handler: " + Handler);
            }
        }
    }

    internal sealed class ExtensionCommand
    {
        public ExtensionCommand(string id, string title, string handler, List<string> suffixes, ExtensionPanel panel, string prefix, string suffix)
        {
            Id = id;
            Title = title;
            Handler = handler;
            FileExtensions = suffixes.AsReadOnly();
            Panel = panel;
            Prefix = prefix;
            Suffix = suffix;
        }

        public string Id { get; }
        public string Title { get; }
        public string Handler { get; }
        public IReadOnlyList<string> FileExtensions { get; }
        public ExtensionPanel Panel { get; }
        private string Prefix { get; }
        private string Suffix { get; }

        public bool AppliesTo(string extension)
        {
            if (FileExtensions.Count == 0) return true;
            foreach (var suffix in FileExtensions)
                if (string.Equals(suffix, extension, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public EditorCommandResult Execute(EditorSnapshot snapshot)
        {
            switch (Handler)
            {
                case "json.format": return JsonTools.Execute(snapshot, false);
                case "json.minify": return JsonTools.Execute(snapshot, true);
                case "text.wrap":
                    return EditorCommandResult.Replace(snapshot.SelectionStart, snapshot.SelectionLength,
                        Prefix + snapshot.Text.Substring(snapshot.SelectionStart, snapshot.SelectionLength) + Suffix);
                default: throw new InvalidOperationException("Not an editing command: " + Handler);
            }
        }
    }
}
