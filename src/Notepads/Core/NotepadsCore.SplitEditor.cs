// Core-owned split editor groups. XAML supplies the surfaces; the core owns
// document identity, selected editor, tab transfer and persisted group state.
namespace Notepads.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Notepads.Controls;
    using Notepads.Controls.TextEditor;
    using Windows.ApplicationModel.DataTransfer;
    using Windows.Foundation;
    using Notepads.Services;
    using Windows.UI.Xaml;

    public enum EditorTabDropZone
    {
        None,
        Left,
        Right,
        Top,
        Bottom
    }

    public partial class NotepadsCore
    {
        private SetsView _secondarySets;
        private SetsView _activeSets;
        private EditorSplitOrientation _splitOrientation;
        private bool _isMergingGroups;
        private int _tabDropTargetGroup = -1;
        private EditorTabDropZone _splitCreationZone = EditorTabDropZone.None;
        private FrameworkElement _tabTransferSurface;

        // UI-only feedback. Document ownership and tab moves remain core-owned.
        public event EventHandler<int> TabDropTargetChanged;
        public event EventHandler<EditorTabDropZone> SplitCreationTargetChanged;


        // The editor surface provides stable coordinates for native drop
        // completion, including UWP ListView's DropResult.None edge case.
        public void AttachTabTransferSurface(FrameworkElement surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (_secondarySets == null) throw new InvalidOperationException("Attach the second tab group first.");
            if (_tabTransferSurface != null) throw new InvalidOperationException("Transfer surface already attached.");
            _tabTransferSurface = surface;
            surface.AddHandler(UIElement.DragOverEvent,
                new DragEventHandler(SplitCreationSurface_DragOver), true);
            surface.AddHandler(UIElement.DragLeaveEvent,
                new DragEventHandler(SplitCreationSurface_DragLeave), true);
        }

        private void SplitCreationSurface_DragOver(object sender, DragEventArgs args)
        {
            if (!IsSplitEditorEnabled &&
                TryGetDraggedEditor(args.DataView, out ITextEditor editor))
            {
                SetSplitCreationTarget(GetSplitCreationZone(editor,
                    args.GetPosition(_tabTransferSurface)));
            }
        }

        private void SplitCreationSurface_DragLeave(object sender, DragEventArgs args)
        {
            // An edge preview must not remain armed after leaving the editor.
            if (!IsSplitEditorEnabled) SetSplitCreationTarget(EditorTabDropZone.None);
        }

        private void SetSplitCreationTarget(EditorTabDropZone zone)
        {
            if (_splitCreationZone == zone) return;
            _splitCreationZone = zone;
            SplitCreationTargetChanged?.Invoke(this, zone);
        }

        private EditorTabDropZone GetSplitCreationZoneAtCurrentPointer(ITextEditor editor)
        {
            if (_tabTransferSurface == null) return EditorTabDropZone.None;
            return GetSplitCreationZone(editor,
                ToTransferSurfacePoint(Window.Current.CoreWindow.PointerPosition));
        }

        private EditorTabDropZone GetSplitCreationZone(ITextEditor editor, Point position)
        {
            // Deliberately require two existing documents: one stays in the
            // original group while the dragged tab becomes a new group.
            if (IsSplitEditorEnabled || _tabTransferSurface == null ||
                !ReferenceEquals(FindEditorGroup(editor), _sets) ||
                _sets.Items.Count < 2)
            {
                return EditorTabDropZone.None;
            }

            var origin = _sets.TransformToVisual(_tabTransferSurface).TransformPoint(new Point(0, 0));
            var x = position.X - origin.X;
            var y = position.Y - origin.Y;
            var w = _sets.ActualWidth;
            var h = _sets.ActualHeight;
            // Accept a small overshoot beyond the visible content bounds; OS
            // drag images and DPI-rounded coordinates can land just past them.
            const double overshoot = 32;
            if (x < -overshoot || x > w + overshoot ||
                y < 0 || y > h + overshoot) return EditorTabDropZone.None;

            var threshold = Math.Min(120, Math.Min(w, h) * 0.25);
            // Avoid splitting when the user is merely reordering in the tab strip.
            if (y < 42) return EditorTabDropZone.None;

            var candidates = new[]
            {
                new { Zone = EditorTabDropZone.Left, Distance = x },
                new { Zone = EditorTabDropZone.Right, Distance = w - x },
                new { Zone = EditorTabDropZone.Top, Distance = y - 42 },
                new { Zone = EditorTabDropZone.Bottom, Distance = h - y }
            };
            var closest = candidates.OrderBy(candidate => candidate.Distance).First();
            return closest.Distance <= threshold ? closest.Zone : EditorTabDropZone.None;
        }

        private void SplitEditorByDragging(ITextEditor editor, EditorTabDropZone zone)
        {
            if (editor == null || IsSplitEditorEnabled ||
                !ReferenceEquals(FindEditorGroup(editor), _sets) ||
                _sets.Items.Count < 2 || zone == EditorTabDropZone.None) return;

            var orientation = zone == EditorTabDropZone.Left || zone == EditorTabDropZone.Right
                ? EditorSplitOrientation.Right : EditorSplitOrientation.Down;
            var before = zone == EditorTabDropZone.Left || zone == EditorTabDropZone.Top;
            var otherEditors = _sets.Items.Cast<SetsViewItem>()
                .Select(item => item.Content as ITextEditor)
                .Where(item => item != null && !ReferenceEquals(item, editor)).ToArray();

            SetSplitCreationTarget(EditorTabDropZone.None);
            try
            {
                SetSplitEditorOrientation(orientation);
                if (before)
                {
                    // The new group occupies the left/top position. Keep the
                    // dragged tab in primary; transfer all remaining tabs intact.
                    foreach (var other in otherEditors) MoveEditorToGroup(other, _secondarySets);
                    SwitchTo(editor);
                }
                else
                {
                    MoveEditorToGroup(editor, _secondarySets);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex);
                if (IsSplitEditorEnabled) CloseSplitEditor();
            }
        }

        private int GetTransferTargetGroup(Point point)
        {
            if (!IsSplitEditorEnabled || _tabTransferSurface == null) return -1;
            if (IsPointInsideGroup(_sets, point)) return 0;
            if (IsPointInsideGroup(_secondarySets, point)) return 1;
            return -1;
        }

        private bool IsPointInsideGroup(SetsView group, Point position)
        {
            if (group == null || group.Visibility != Visibility.Visible ||
                group.ActualWidth <= 0 || group.ActualHeight <= 0)
            {
                return false;
            }
            var origin = group.TransformToVisual(_tabTransferSurface).TransformPoint(new Point(0, 0));
            return position.X >= origin.X && position.X < origin.X + group.ActualWidth &&
                   position.Y >= origin.Y && position.Y < origin.Y + group.ActualHeight;
        }

        private Point ToTransferSurfacePoint(Point windowPoint)
        {
            var origin = _tabTransferSurface.TransformToVisual(null).TransformPoint(new Point(0, 0));
            return new Point(windowPoint.X - origin.X, windowPoint.Y - origin.Y);
        }

        private void SetTabDropTarget(int targetGroup)
        {
            if (_tabDropTargetGroup == targetGroup) return;
            _tabDropTargetGroup = targetGroup;
            if (targetGroup >= 0)
            {
                LoggingService.LogInfo($"[TabTransfer] Hover destination group {targetGroup}.");
            }
            TabDropTargetChanged?.Invoke(this, targetGroup);
        }

        private bool TryGetDraggedEditor(DataPackageView view, out ITextEditor editor)
        {
            editor = null;
            if (view?.Properties == null ||
                !view.Properties.TryGetValue(NotepadsInstanceId, out object instanceId) ||
                !(instanceId is string sourceId) ||
                !string.Equals(sourceId, App.InstanceId.ToString(), StringComparison.Ordinal) ||
                !view.Properties.TryGetValue(NotepadsTextEditorGuid, out object tabId) ||
                !(tabId is string editorId) || !Guid.TryParse(editorId, out Guid id))
            {
                return false;
            }

            editor = GetAllTextEditors().FirstOrDefault(item => item.Id == id);
            return editor != null;
        }

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
            sets.DragLeave += Sets_DragLeave;
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
            SetSplitCreationTarget(EditorTabDropZone.None);
            SplitLayoutChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SplitSelectedEditor(EditorSplitOrientation orientation)
        {
            if (orientation != EditorSplitOrientation.Right && orientation != EditorSplitOrientation.Down) return;
            if (_secondarySets == null || GetSelectedTextEditor() == null) return;
            try
            {
                if (IsSplitEditorEnabled)
                {
                    SetSplitEditorOrientation(orientation);
                    return;
                }

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
            catch (Exception ex)
            {
                LoggingService.LogError($"[{nameof(NotepadsCore)}] SplitSelectedEditor({orientation}) failed: {ex}");
                throw;
            }
        }

        // Stable two-group API for tab context menus and keyboard users.
        public int GetEditorGroupIndex(ITextEditor editor)
        {
            var group = FindEditorGroup(editor);
            if (group == null) return -1;
            return ReferenceEquals(group, _secondarySets) ? 1 : 0;
        }

        public void MoveEditorToGroup(ITextEditor editor, int targetGroup)
        {
            if (editor == null || !IsSplitEditorEnabled || _secondarySets == null ||
                (targetGroup != 0 && targetGroup != 1)) return;
            var target = targetGroup == 0 ? _sets : _secondarySets;
            if (FindEditorGroup(editor) == null || ReferenceEquals(FindEditorGroup(editor), target)) return;
            MoveEditorToGroup(editor, target);
        }

        public void MoveEditorToOtherGroup(ITextEditor editor)
        {
            var currentGroup = GetEditorGroupIndex(editor);
            if (currentGroup < 0) return;
            if (!IsSplitEditorEnabled)
            {
                SetSplitEditorOrientation(EditorSplitOrientation.Right);
            }
            MoveEditorToGroup(editor, currentGroup == 0 ? 1 : 0);
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

            if (ReferenceEquals(source, _sets) && ReferenceEquals(target, _secondarySets) &&
                source.Items.Count == 1)
            {
                // Also cover dragging the last primary tab into the second group.
                var previousActive = _activeSets;
                _activeSets = _sets;
                OpenNewTextEditor(_resourceLoader.GetString("TextEditor_DefaultNewFileName"));
                _activeSets = previousActive;
            }

            var oldItem = GetTextEditorSetsViewItem(editor);
            var originalIndex = source.Items.IndexOf(oldItem);
            source.Items.Remove(oldItem);

            // A SetsViewItem is a ContentControl. Removing it from ListView.Items
            // does not release its Content reference. Reusing the editor as the
            // Content of a *second* item would throw XAML 0x800F1000:
            // "Element is already the child of another element."
            // Detach the existing visual owner before assigning the same editor
            // to a new tab container. The document and Undo history stay intact.
            oldItem.Content = null;

            SetsViewItem newItem = null;
            try
            {
                newItem = CreateTextEditorSetsViewItem(editor);
                if (atIndex < 0 || atIndex > target.Items.Count)
                {
                    target.Items.Add(newItem);
                }
                else
                {
                    target.Items.Insert(atIndex, newItem);
                }
                target.SelectedItem = newItem;
            }
            catch
            {
                // A failed transfer must not orphan an unsaved document. Return
                // the same editor to its original group before rethrowing.
                if (newItem != null)
                {
                    target.Items.Remove(newItem);
                    newItem.Content = null;
                    if (newItem.ContextFlyout is TabContextFlyout newFlyout) newFlyout.Dispose();
                }
                oldItem.Content = editor;
                source.Items.Insert(Math.Min(originalIndex, source.Items.Count), oldItem);
                source.SelectedItem = oldItem;
                ActivateEditorGroup(ReferenceEquals(source, _secondarySets) ? 1 : 0);
                throw;
            }
            if (oldItem.ContextFlyout is TabContextFlyout oldFlyout) oldFlyout.Dispose();
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
            SetTabDropTarget(-1);
            SetSplitCreationTarget(EditorTabDropZone.None);
            ActivateEditorGroup(0);
            SplitLayoutChanged?.Invoke(this, EventArgs.Empty);
            FocusOnSelectedTextEditor();
        }

        public IReadOnlyList<ITextEditor> GetEditorsInSameGroup(ITextEditor editor)
        {
            var sets = FindEditorGroup(editor);
            if (sets == null) return Array.Empty<ITextEditor>();
            return sets.Items.Cast<SetsViewItem>()
                .Select(item => item.Content as ITextEditor)
                .Where(item => item != null).ToArray();
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
