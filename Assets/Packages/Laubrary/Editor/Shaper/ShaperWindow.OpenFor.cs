// ShaperWindow.OpenFor.cs — a standalone entry point so an outside caller (T-0161's ShaperClipEditorLink)
// can open the Shaper window bound to a specific document, mirroring PyreWindow.OpenFor(Pyre). Kept in its
// own partial-class file rather than added to ShaperWindow.cs itself, the shell other Shaper work is
// actively editing — same reasoning PROGRAMME_RULES.md gives for keeping concurrent additions to a shared
// file in a clearly separated region, taken one step further here since a whole new file needs none of
// ShaperWindow.cs's own lines to touch at all.
namespace Laubrary.Shaper.Editor
{
    public partial class ShaperWindow
    {
        /// Opens (or focuses) the Shaper window bound to `doc`. Performs the SAME reset the document picker's
        /// own callback does (ShaperWindow.cs's BuildDocumentBar) — selectedLayer/currentFrame/playing all
        /// reset to their document-just-loaded defaults — so switching document through this entry point
        /// behaves identically to switching it by hand in the window. No-ops on a null doc rather than
        /// opening an empty/unbound window.
        public static void OpenFor(ShaperDocument doc)
        {
            if (doc == null) return;
            var w = GetWindow<ShaperWindow>("Shaper");
            w.minSize = new UnityEngine.Vector2(820f, 520f);
            w.document = doc;
            w.selectedLayer = 0;
            w.currentFrame = 0;
            w.playing = false;
            w.Rebuild();
        }
    }
}
