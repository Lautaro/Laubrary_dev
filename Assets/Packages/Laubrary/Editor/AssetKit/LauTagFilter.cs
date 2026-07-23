using System.Collections.Generic;
using System.Linq;

namespace Laubrary.AssetKit.Editor
{
    /// The matching semantics, copied deliberately from Zounds' ZoundsFilter.GetZoundsByTag (see
    /// lautag-lauasset-plan memory): querying a full "key:value" tag is an exact-name match; querying a bare
    /// key ALSO pulls in every "key:*" tag whose pre-colon portion matches — ':' is a pure naming convention,
    /// not a structural split anywhere in the data. Selected tags OR together.
    public static class LauTagFilter
    {
        /// Every tag id that a selection of `selectedTagIds` actually resolves to, once bare-key expansion is
        /// applied — e.g. selecting the bare tag "CombatUnit" expands to itself plus every "CombatUnit:*" id.
        public static HashSet<int> ExpandSelection(IEnumerable<int> selectedTagIds, LauTagLibrary lib)
        {
            var expanded = new HashSet<int>();
            foreach (var id in selectedTagIds)
            {
                expanded.Add(id);
                var tag = lib.Tags.FirstOrDefault(t => t.id == id);
                if (tag == null || tag.name.Contains(':')) continue;   // only bare keys expand further
                string prefix = tag.name + ":";
                foreach (var t in lib.Tags)
                    if (t.name.StartsWith(prefix)) expanded.Add(t.id);
            }
            return expanded;
        }

        /// True if the asset's own tags intersect the (already-expanded) selection — OR semantics. An empty
        /// selection always matches (no filter active).
        public static bool Matches(List<int> assetTagIds, HashSet<int> expandedSelection)
        {
            if (expandedSelection == null || expandedSelection.Count == 0) return true;
            if (assetTagIds == null || assetTagIds.Count == 0) return false;
            foreach (var id in assetTagIds) if (expandedSelection.Contains(id)) return true;
            return false;
        }
    }
}
