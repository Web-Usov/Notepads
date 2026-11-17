// Core-owned split editor groups. XAML supplies the surfaces; the core owns
// document identity, selected editor, tab transfer and persisted group state.
namespace Notepads.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Notepads.Controls;
    using Notepads.Controls.TextEditor;
    using Windows.UI.Xaml;

    public partial class NotepadsCore
    {
        private SetsView _secondarySets;
        private SetsView _activeSets;
        private EditorSplitOrientation _splitOrientation;
        private bool _isMergingGroups;

        public event EventHandler SplitLayoutChanged;
        public event EventHandler<ITextEditor> ActiveTextEditorChanged;

        public EditorSplitOrientation SplitOrientation => _splitOrientation;
        public bool IsSplitEditorEnabled => _splitOrientation != EditorSplitOrientation.None;

        public void AttachSecondarySetsView(SetsView secondarySets)
        {
            if (secondarySets == null) throw new ArgumentNullException(nameof(secondarySets));
            if (_secondarySets != null) throw new InvalidOperationException("Secondary editor group is already attached.");
            if (ReferenceEquals(_sets, secondarySets)) throw new ArgumentException("Editor groups must be distinct.");
            _secondarySets = secondarySets;
            BindSetsView(_secondarySets);
        }

        private void BindSetsView(SetsView sets)
        {
            sets.SelectionChanged += SetsView_OnSelectionChanged;
            sets.Items.VectorChanged += SetsView_OnItemsChanged;
            sets.SetClosing += SetsView_OnSetClosing;
            sets.SetTapped += (sender, args) =>
            {
                ActivateEditorGroup(ReferenceEquals(sets, _secondarySets) ? 1 : 0);
                FocusOnTextEditor(args.Item as ITextEditor);
            };
            sets.SetDraggedOutside += Sets_SetDraggedOutside;
            sets.DragOver += Sets_DragOver;
            sets.Drop += Sets_Drop;
            sets.DragItemsStarting += Sets_DragItemsStarting;
            sets.DragItemsCompleted += Sets_DragItemsCompleted;
        }

        private IEnumerable<SetsView> EnumerateEditorGroups()
        {
            yield return _sets;
            if (_secondarySets != null) yield return _secondarySets;
        }

        private SetsView FindEditorGroup(ITextEditor editor)
        {
            if (editor == null) return null;
            foreach (var sets in EnumerateEditorGroups())
            {
                if (sets.Items == null) continue;
                foreach (SetsViewItem item in sets.Items)
                {
                    if (ReferenceEquals(item.Content, editor)) return sets;
                }
            }
            return null;
        }

        public void ActivateEditorGroup(int group)
        {
            var sets = group == 1 && IsSplitEditorEnabled ? _secondarySets : _sets;
            if (sets == null) return;
            var previous = _selectedTextEditor;
            _activeSets = sets;
            _selectedTextEditor = (sets.SelectedItem as SetsViewItem)?.Content as ITextEditor;
            if (_selectedTextEditor != null && !ReferenceEquals(previous, _selectedTextEditor))
            {
                ActiveTextEditorChanged?.Invoke(this, _selectedTextEditor);
            }
        }

        private void TextEditor_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ITextEditor editor)
            {
                var sets = FindEditorGroup(editor);
                if (sets != null && ReferenceEquals((sets.SelectedItem as SetsViewItem)?.Content, editor))
                {
                    ActivateEditorGroup(ReferenceEquals(sets, _secondarySets) ? 1 : 0);
                }
            }
        }

        public void SetSplitEditorOrientation(EditorSplitOrientation orientation)
        {
            if (orientation == EditorSplitOrientation.None)
            {
                CloseSplitEditor();
                return;
            }
            if (_secondarySets == null || (orientation != EditorSplitOrientation.Right &&
                                           orientation != EditorSplitOrientation.Down)) return;
            if (_splitOrientation == orientation) return;
            _splitOrientation = orientation;
            SplitLayoutChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SplitSelectedEditor(EditorSplitOrientation orientation)
        {
            if (_secondarySets == null || GetSelectedTextEditor() == null) return;
            if (IsSplitEditorEnabled)
            {
                SetSplitEditorOrientation(orientation);
                return;
            }
            if (orientation == EditorSplitOrientation.None) return;

            var editor = GetSelectedTextEditor();
            // Keep the primary pane usable when splitting the only document.
            if (_sets.Items.Count == 1)
            {
                _activeSets = _sets;
                OpenNewTextEditor(_resourceLoader.GetString("TextEditor_DefaultNewFileName"));
            }
            SetSplitEditorOrientation(orientation);
            MoveEditorToGroup(editor, _secondarySets);
        }

        public void MoveEditorToOtherGroup(ITextEditor editor)
        {
            if (editor == null || _secondarySets == null) return;
            var source = FindEditorGroup(editor);
            if (source == null) return;
            if (!IsSplitEditorEnabled) SetSplitEditorOrientation(EditorSplitOrientation.Right);

            if (ReferenceEquals(source, _sets) && _sets.Items.Count == 1)
            {
                var previousActive = _activeSets;
                _activeSets = _sets;
                OpenNewTextEditor(_resourceLoader.GetString("TextEditor_DefaultNewFileName"));
                _activeSets = previousActive;
            }
            MoveEditorToGroup(editor, ReferenceEquals(source, _sets) ? _secondarySets : _sets);
        }

        // Transfer the same editor (and its undo history). A fresh tab container
        // avoids reparenting a SetsViewItem across different ListViews.
        private void MoveEditorToGroup(ITextEditor editor, SetsView target, int atIndex = -1)
        {
            var source = FindEditorGroup(editor);
            if (source == null || target == null) return;
            if (ReferenceEquals(source, target))
            {
                SwitchTo(editor);
                return;
            }

            var oldItem = GetTextEditorSetsViewItem(editor);
            source.Items.Remove(oldItem);
            if (oldItem.ContextFlyout is TabContextFlyout oldFlyout) oldFlyout.Dispose();

            var newItem = CreateTextEditorSetsViewItem(editor);
            if (atIndex < 0 || atIndex > target.Items.Count)
            {
                target.Items.Add(newItem);
            }
            else
            {
                target.Items.Insert(atIndex, newItem);
            }
            target.SelectedItem = newItem;
            ActivateEditorGroup(ReferenceEquals(target, _secondarySets) ? 1 : 0);

            if (!_isMergingGroups && IsSplitEditorEnabled && _secondarySets.Items.Count == 0)
            {
                CloseSplitEditor();
            }
        }

        public void CloseSplitEditor()
        {
            if (_secondarySets == null || !IsSplitEditorEnabled) return;
            _isMergingGroups = true;
            try
            {
                var editors = _secondarySets.Items.Cast<SetsViewItem>()
                    .Select(item => item.Content as ITextEditor)
                    .Where(editor => editor != null).ToArray();
                foreach (var editor in editors) MoveEditorToGroup(editor, _sets);
            }
            finally
            {
                _isMergingGroups = false;
            }
            _splitOrientation = EditorSplitOrientation.None;
            ActivateEditorGroup(0);
            SplitLayoutChanged?.Invoke(this, EventArgs.Empty);
            FocusOnSelectedTextEditor();
        }

        public EditorSplitLayout GetSplitEditorLayout()
        {
            var layout = new EditorSplitLayout
            {
                Orientation = _splitOrientation,
                SelectedPrimaryEditorId = ((_sets.SelectedItem as SetsViewItem)?.Content as ITextEditor)?.Id ?? Guid.Empty,
                ActiveGroup = ReferenceEquals(_activeSets, _secondarySets) ? 1 : 0
            };
            if (IsSplitEditorEnabled && _secondarySets != null)
            {
                layout.SecondaryEditorIds = _secondarySets.Items.Cast<SetsViewItem>()
                    .Select(item => item.Content as ITextEditor)
                    .Where(editor => editor != null).Select(editor => editor.Id).ToList();
                layout.SelectedSecondaryEditorId =
                    ((_secondarySets.SelectedItem as SetsViewItem)?.Content as ITextEditor)?.Id ?? Guid.Empty;
            }
            return layout;
        }

        public void RestoreSplitEditorLayout(EditorSplitLayout saved)
        {
            if (_secondarySets == null || saved == null) return;
            var layout = saved.Normalize(GetAllTextEditors().Select(editor => editor.Id));
            if (layout.Orientation == EditorSplitOrientation.None) return;

            SetSplitEditorOrientation(layout.Orientation);
            _isMergingGroups = true;
            try
            {
                foreach (var id in layout.SecondaryEditorIds)
                {
                    var editor = GetAllTextEditors().FirstOrDefault(item => item.Id == id);
                    if (editor != null) MoveEditorToGroup(editor, _secondarySets);
                }
            }
            finally
            {
                _isMergingGroups = false;
            }

            var primary = GetAllTextEditors().FirstOrDefault(editor => editor.Id == layout.SelectedPrimaryEditorId);
            var secondary = GetAllTextEditors().FirstOrDefault(editor => editor.Id == layout.SelectedSecondaryEditorId);
            var primaryItem = GetTextEditorSetsViewItem(primary);
            var secondaryItem = GetTextEditorSetsViewItem(secondary);
            if (primaryItem != null && ReferenceEquals(FindEditorGroup(primary), _sets))
                _sets.SelectedItem = primaryItem;
            if (secondaryItem != null && ReferenceEquals(FindEditorGroup(secondary), _secondarySets))
                _secondarySets.SelectedItem = secondaryItem;
            ActivateEditorGroup(layout.ActiveGroup);
        }
    }
}
