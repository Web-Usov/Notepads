// Fluent-style split editor controls. The layout and commands are a view of
// NotepadsCore's authoritative editor groups (not an extension or a second core).
namespace Notepads.Views.MainPage
{
    using System;
    using Notepads.Core;
    using Notepads.Services;
    using Windows.UI.Core;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Automation;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Controls.Primitives;

    public sealed partial class NotepadsMainPage
    {
        private void InitializeSplitEditorControls()
        {
            var core = NotepadsCore;
            core.SplitLayoutChanged += (sender, args) => UpdateSplitEditorLayout();
            core.TabDropTargetChanged += (sender, target) => ShowTabDropTarget(target);
            core.SplitCreationTargetChanged += (sender, zone) => ShowSplitCreationPreview(zone);
            core.ActiveTextEditorChanged += (sender, editor) => SetupStatusBar(editor);

            var splitText = _resourceLoader.GetString("SplitEditor_MenuTitle");
            var rightText = _resourceLoader.GetString("SplitEditor_Right");
            var downText = _resourceLoader.GetString("SplitEditor_Down");
            var closeText = _resourceLoader.GetString("SplitEditor_Close");
            var newTabText = _resourceLoader.GetString("SplitEditor_NewTab");

            MenuSplitEditor.Text = splitText;
            MenuSplitRightButton.Text = rightText;
            MenuSplitDownButton.Text = downText;
            MenuCloseSplitButton.Text = closeText;

            ToolTipService.SetToolTip(SecondaryNewSetButton, newTabText);
            AutomationProperties.SetName(SecondaryNewSetButton, newTabText);
            AutomationProperties.SetName(VerticalSplitDivider, _resourceLoader.GetString("SplitEditor_ResizeColumns"));
            AutomationProperties.SetName(HorizontalSplitDivider, _resourceLoader.GetString("SplitEditor_ResizeRows"));

            // MenuFlyoutItem.Invoke is synchronous for UI Automation. Reparenting
            // editors while the invoking flyout is still closing invalidates its
            // automation provider (0x800F1000) and can re-enter SetsView layout.
            // Queue document operations after the popup input event completes.
            MenuSplitRightButton.Click += async (sender, args) =>
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low,
                    () => ExecuteSplitSafely(EditorSplitOrientation.Right));
            MenuSplitDownButton.Click += async (sender, args) =>
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low,
                    () => ExecuteSplitSafely(EditorSplitOrientation.Down));
            MenuCloseSplitButton.Click += async (sender, args) =>
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low,
                    () => core.CloseSplitEditor());

            SecondaryNewSetButton.Click += (sender, args) =>
            {
                core.ActivateEditorGroup(1);
                core.OpenNewTextEditor(_defaultNewFileName);
            };

            if (App.IsGameBarWidget)
            {
                // Compact Game Bar widgets do not have enough space for two tab strips.
                MenuSplitEditor.Visibility = Visibility.Collapsed;
            }

            UpdateSplitEditorLayout();
        }

        // Keep errors visible to users and to UI automation. UWP can report
        // opaque HRESULTs for a failed XAML tree mutation during a split.
        private void ExecuteSplitSafely(EditorSplitOrientation orientation)
        {
            try
            {
                NotepadsCore.SplitSelectedEditor(orientation);
                AutomationProperties.SetHelpText(MainMenuButton, string.Empty);
            }
            catch (Exception exception)
            {
                LoggingService.LogError($"[SplitEditor] {exception}");
                AutomationProperties.SetHelpText(MainMenuButton, exception.ToString());
                NotificationCenter.Instance.PostNotification(exception.Message, 5000);
            }
        }

        private void ShowSplitCreationPreview(EditorTabDropZone zone)
        {
            if (zone == EditorTabDropZone.None || NotepadsCore.IsSplitEditorEnabled)
            {
                SplitCreationPreview.Visibility = Visibility.Collapsed;
                return;
            }

            var side = zone == EditorTabDropZone.Left || zone == EditorTabDropZone.Right;
            SplitCreationPreview.Width = side ? Sets.ActualWidth * 0.5 : double.NaN;
            SplitCreationPreview.Height = side ? double.NaN : Sets.ActualHeight * 0.5;
            SplitCreationPreview.HorizontalAlignment = zone == EditorTabDropZone.Left
                ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            SplitCreationPreview.VerticalAlignment = zone == EditorTabDropZone.Top
                ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            SplitCreationPreview.Visibility = Visibility.Visible;
        }

        private void ShowTabDropTarget(int target)
        {
            PrimaryTabDropHighlight.Visibility = target == 0 && NotepadsCore.IsSplitEditorEnabled
                ? Visibility.Visible : Visibility.Collapsed;
            SecondaryTabDropHighlight.Visibility = target == 1 && NotepadsCore.IsSplitEditorEnabled
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateSplitEditorLayout()
        {
            var orientation = NotepadsCore.SplitOrientation;
            bool right = orientation == EditorSplitOrientation.Right;
            bool down = orientation == EditorSplitOrientation.Down;
            bool split = right || down;

            // Reuse Grid definitions so the splitter remains keyboard operable.
            PrimaryEditorColumn.Width = new GridLength(1, GridUnitType.Star);
            PrimaryEditorRow.Height = new GridLength(1, GridUnitType.Star);
            SecondaryEditorColumn.MinWidth = right ? 160 : 0;
            SecondaryEditorRow.MinHeight = down ? 140 : 0;
            SecondaryEditorColumn.Width = right ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            SecondaryEditorRow.Height = down ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            VerticalDividerColumn.Width = right ? new GridLength(6) : new GridLength(0);
            HorizontalDividerRow.Height = down ? new GridLength(6) : new GridLength(0);
            Grid.SetColumn(SecondarySets, right ? 2 : 0);
            Grid.SetRow(SecondarySets, down ? 2 : 0);
            Grid.SetColumn(SecondaryTabDropHighlight, right ? 2 : 0);
            Grid.SetRow(SecondaryTabDropHighlight, down ? 2 : 0);
            ShowTabDropTarget(-1);
            ShowSplitCreationPreview(EditorTabDropZone.None);
            // Window caption controls occupy the upper-right 180 DIPs.
            // When split right, reserve that area in the second tab strip,
            // not the primary strip. A split-down view keeps it in the first.
            TitleBarReservedArea.Width = !App.IsGameBarWidget && !right ? 180 : 0;
            SecondaryTitleBarReservedArea.Width = !App.IsGameBarWidget && right ? 180 : 0;
            SecondarySets.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
            VerticalSplitDivider.Visibility = right ? Visibility.Visible : Visibility.Collapsed;
            HorizontalSplitDivider.Visibility = down ? Visibility.Visible : Visibility.Collapsed;
            MenuCloseSplitButton.IsEnabled = split;
        }
    }
}
