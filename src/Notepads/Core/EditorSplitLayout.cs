// Layout metadata is independent of XAML and can be validated in portable tests.
namespace Notepads.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public enum EditorSplitOrientation
    {
        None = 0,
        Right = 1,
        Down = 2
    }

    public sealed class EditorSplitLayout
    {
        public EditorSplitOrientation Orientation { get; set; } = EditorSplitOrientation.None;
        public List<Guid> SecondaryEditorIds { get; set; } = new List<Guid>();
        public Guid SelectedPrimaryEditorId { get; set; }
        public Guid SelectedSecondaryEditorId { get; set; }
        public int ActiveGroup { get; set; }

        // Discard obsolete IDs, duplicates and invalid group selections.
        // An older v1 session without these fields safely restores one group.
        public EditorSplitLayout Normalize(IEnumerable<Guid> openEditorIds)
        {
            var opened = (openEditorIds ?? Enumerable.Empty<Guid>()).Distinct().ToList();
            var known = new HashSet<Guid>(opened);
            var orientation = Orientation == EditorSplitOrientation.Right ||
                              Orientation == EditorSplitOrientation.Down
                ? Orientation : EditorSplitOrientation.None;
            var secondary = (SecondaryEditorIds ?? new List<Guid>())
                .Where(id => known.Contains(id)).Distinct().ToList();
            if (orientation == EditorSplitOrientation.None || secondary.Count == 0 ||
                secondary.Count == opened.Count)
            {
                orientation = EditorSplitOrientation.None;
                secondary.Clear();
            }
            var secondarySet = new HashSet<Guid>(secondary);
            var primary = opened.Where(id => !secondarySet.Contains(id)).ToList();
            return new EditorSplitLayout
            {
                Orientation = orientation,
                SecondaryEditorIds = secondary,
                SelectedPrimaryEditorId = primary.Contains(SelectedPrimaryEditorId)
                    ? SelectedPrimaryEditorId : primary.FirstOrDefault(),
                SelectedSecondaryEditorId = secondary.Contains(SelectedSecondaryEditorId)
                    ? SelectedSecondaryEditorId : secondary.FirstOrDefault(),
                ActiveGroup = orientation != EditorSplitOrientation.None && ActiveGroup == 1 ? 1 : 0
            };
        }
    }
}
