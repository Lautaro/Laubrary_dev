using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    /// The authoring window for a chunk recipe. The AssetKit shell (browse, new, duplicate, rename, delete,
    /// tags) is inherited and stays; the per-recipe body is being rebuilt around the capability stack and is
    /// deliberately empty until it lands, rather than left showing dials for a data model that no longer
    /// exists — a window that edits fields nothing reads is worse than one that says it is not ready.
    public class ChunkWindow : ZuiAssetWindow<ChunkSpec>
    {
        [MenuItem("Laubrary/Chunks")]
        public static void Open() => GetWindow<ChunkWindow>("Chunks");

        /// Same entry-point shape as PyreWindow.OpenFor / MirageWindow.OpenFor — lets a LauAssetField's Edit
        /// button jump straight into this recipe's own editor.
        public static void OpenFor(ChunkSpec spec)
        {
            var w = GetWindow<ChunkWindow>("Chunks");
            if (spec != null) w.SetAsset(spec);
        }

        protected override string TypeLabel => "Chunk";
        protected override string NewAssetName => "Chunks";
        protected override string DefaultFolder => "Assets/Chunks";

        protected override void BuildAsset(VisualElement root, ChunkSpec c)
        {
            if (c == null) return;
            var section = Z.Section("Recipe", "The capabilities this recipe is composed of.");
            section.Add(Z.Text($"{c.capabilities.Count} capabilities · {c.ClockLength:0.00}s",
                               ZuiText.Subtle, "What this recipe currently holds and how long it runs for."));
            root.Add(section);
        }
    }
}
