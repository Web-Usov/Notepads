// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2019-2024, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Notepads.Core
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;
    using Notepads.Controls.TextEditor;
    using Notepads.Models;
    using Notepads.Utilities;
    using Windows.Storage;
    using Windows.UI.Xaml.Input;

    /// <summary>
    /// INotepadsCore handles Tabs and TextEditor life cycle
    /// </summary>
    public interface INotepadsCore
    {
        event EventHandler<ITextEditor> TextEditorLoaded;
        event EventHandler<ITextEditor> TextEditorUnloaded;
        event EventHandler<ITextEditor> TextEditorEditorModificationStateChanged;
        event EventHandler<ITextEditor> TextEditorFileModificationStateChanged;
        event EventHandler<ITextEditor> TextEditorSaved;
        event EventHandler<ITextEditor> TextEditorClosing;
        event EventHandler<ITextEditor> TextEditorRenamed;
        event EventHandler<ITextEditor> TextEditorSelectionChanged;
        event EventHandler<ITextEditor> TextEditorFontZoomFactorChanged;
        event EventHandler<ITextEditor> TextEditorEncodingChanged;
        event EventHandler<ITextEditor> TextEditorLineEndingChanged;
        event EventHandler<ITextEditor> TextEditorModeChanged;
        event EventHandler<ITextEditor> TextEditorMovedToAnotherAppInstance;
        event EventHandler<IReadOnlyList<IStorageItem>> StorageItemsDropped;
        event KeyEventHandler TextEditorKeyDown;
        event EventHandler SplitLayoutChanged;
        event EventHandler<ITextEditor> ActiveTextEditorChanged;
        event EventHandler<int> TabDropTargetChanged;
        event EventHandler<string> TabTransferDiagnosticChanged;

        EditorSplitOrientation SplitOrientation { get; }
        bool IsSplitEditorEnabled { get; }

        void ActivateEditorGroup(int group);
        void SetSplitEditorOrientation(EditorSplitOrientation orientation);
        void SplitSelectedEditor(EditorSplitOrientation orientation);
        void MoveEditorToOtherGroup(ITextEditor editor);
        int GetEditorGroupIndex(ITextEditor editor);
        void MoveEditorToGroup(ITextEditor editor, int targetGroup);
        void CloseSplitEditor();
        IReadOnlyList<ITextEditor> GetEditorsInSameGroup(ITextEditor editor);
        EditorSplitLayout GetSplitEditorLayout();
        void RestoreSplitEditorLayout(EditorSplitLayout layout);

        Task<ITextEditor> CreateTextEditorAsync(
            Guid id,
            StorageFile file,
            Encoding encoding = null,
            bool ignoreFileSizeLimit = false);

        ITextEditor CreateTextEditor(
            Guid id,
            TextFile textFile,
            StorageFile editingFile,
            string fileNamePlaceHolder,
            bool isModified = false);

        void OpenNewTextEditor(string fileNamePlaceholder);

        void OpenTextEditor(ITextEditor editor, int atIndex = -1);

        void OpenTextEditors(ITextEditor[] editors, Guid? selectedEditorId = null);

        Task SaveContentToFileAndUpdateEditorStateAsync(ITextEditor textEditor, StorageFile file);

        void DeleteTextEditor(ITextEditor textEditor);

        int GetNumberOfOpenedTextEditors();

        bool TryGetSharingContent(ITextEditor textEditor, out string title, out string content);

        bool HaveUnsavedTextEditor();

        bool HaveNonemptyTextEditor();

        void ChangeLineEnding(ITextEditor textEditor, LineEnding lineEnding);

        void SwitchTo(bool next);

        void SwitchTo(int index);

        void SwitchTo(ITextEditor textEditor);

        ITextEditor GetSelectedTextEditor();

        ITextEditor GetTextEditor(StorageFile file);

        ITextEditor GetTextEditor(string editingFilePath);

        ITextEditor[] GetAllTextEditors();

        void FocusOnTextEditor(ITextEditor textEditor);

        void FocusOnSelectedTextEditor();

        void CloseTextEditor(ITextEditor textEditor);

        double GetTabScrollViewerHorizontalOffset();

        void SetTabScrollViewerHorizontalOffset(double offset);
    }
}
