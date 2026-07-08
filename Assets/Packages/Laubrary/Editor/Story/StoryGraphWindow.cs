using UnityEditor;
using Laubrary.Story;
using Laubrary.Loom.Editor;

namespace Laubrary.Story.Editor
{
    // Story authors its Screenplay in the shared Loom graph window. All the canvas/node/edge/viz logic is generic
    // in Laubrary.Loom.Editor — this only names the window and its menu + wires the Screenplay asset in.
    public class StoryGraphWindow : GraphWindowBase
    {
        protected override string WindowTitle => "Story Graph";

        public static void Open(Screenplay sp)
        {
            var w = GetWindow<StoryGraphWindow>();
            w.OpenAsset(sp);
        }

        [MenuItem("Laubrary/Story Graph")]
        public static void OpenEmpty()
        {
            var w = GetWindow<StoryGraphWindow>();
            if (Selection.activeObject is Screenplay sp) w.OpenAsset(sp);
            else w.Show();
        }
    }
}
