// Fluent-style split editor controls. The layout and commands are a view of
// NotepadsCore's authoritative editor groups (not an extension or a second core).
namespace Notepads.Views.MainPage
{
    using Notepads.Core;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Automation;
    using Windows.UI.Xaml.Controls;

    public sealed partial class NotepadsMainPage
    {
        private void InitializeSplitEditorControls()
        {
            var core = NotepadsCore;
            core.SplitLayoutChanged += (sender, args) => UpdateSplitEditorLayout();
            core.ActiveTextEditorChanged += (sender, editor) => SetupStatusBar(editor);

            var splitText = _resourceLoader.GetString("SplitEditor_MenuTitle");
            var rightText = _resourceLoader.GetString("SplitEditor_Right");
            var downText = _resourceLoader.GetString("SplitEditor_Down");
            var closeText = _resourceLoader.GetString("SplitEditor_Close");
            var newTabText = _resourceLoader.GetString("SplitEditor_NewTab");

            MenuSplitEditor.Text = splitText;
            MenuSplitRightButton.Text = ToolbarSplitRightButton.Text = rightText;
            MenuSplitDownButton.Text = ToolbarSplitDownButton.Text = downText;
            MenuCloseSplitButton.Text = ToolbarCloseSplitButton.Text = closeText;

            ToolTipService.SetToolTip(SplitEditorToolbarButton, splitText);
            ToolTipService.SetToolTip(SecondaryCloseSplitButton, closeText);
            ToolTipService.SetToolTip(SecondaryNewSetButton, newTabText);
            AutomationProperties.SetName(SplitEditorToolbarButton, splitText);
            AutomationProperties.SetName(SecondaryCloseSplitButton, closeText);
            AutomationProperties.SetName(SecondaryNewSetButton, newTabText);
            AutomationProperties.SetName(VerticalSplitDivider, _resourceLoader.GetString("SplitEditor_ResizeColumns"));
            AutomationProperties.SetName(HorizontalSplitDivider, _resourceLoader.GetString("SplitEditor_ResizeRows"));

            MenuSplitRightButton.Click += (sender, args) => core.SplitSelectedEditor(EditorSplitOrientation.Right);
            ToolbarSplitRightButton.Click += (sender, args) => core.SplitSelectedEditor(EditorSplitOrientation.Right);
            MenuSplitDownButton.Click += (sender, args) => core.SplitSelectedEditor(EditorSplitOrientation.Down);
            ToolbarSplitDownButton.Click += (sender, args) => core.SplitSelectedEditor(EditorSplitOrientation.Down);
            MenuCloseSplitButton.Click += (sender, args) => core.CloseSplitEditor();
            ToolbarCloseSplitButton.Click += (sender, args) => core.CloseSplitEditor();
            SecondaryCloseSplitButton.Click += (sender, args) => core.CloseSplitEditor();

            SecondaryNewSetButton.Click += (sender, args) =>
            {
                core.ActivateEditorGroup(1);
                core.OpenNewTextEditor(_defaultNewFileName);
            };

            if (App.IsGameBarWidget)
            {
                // Compact Game Bar widgets do not have enough space for two tab strips.
                SplitEditorToolbarButton.Visibility = Visibility.Collapsed;
                MenuSplitEditor.Visibility = Visibility.Collapsed;
            }

            UpdateSplitEditorLayout();
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
            SecondarySets.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
            VerticalSplitDivider.Visibility = right ? Visibility.Visible : Visibility.Collapsed;
            HorizontalSplitDivider.Visibility = down ? Visibility.Visible : Visibility.Collapsed;
            MenuCloseSplitButton.IsEnabled = ToolbarCloseSplitButton.IsEnabled = split;
        }
    }
}
