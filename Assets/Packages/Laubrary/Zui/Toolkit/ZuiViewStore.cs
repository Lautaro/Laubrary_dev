// ZuiViewStore — the committed store of ZUI "views". A view is VIEW-STATE ONLY: which sections are
// folded, which gears are open, which opt-in controls are shown — never any authored value. One
// ZuiViewEntry = one keyed on/off; one ZuiViewPreset = a whole named arrangement of a tool's view
// state; the asset holds every preset the team has authored, so views are SHARED (committed to the
// asset), while only the per-user "last used" pointer lives in EditorPrefs (see ZuiViewBar).
//
// The keys are the stable, consumer-supplied strings ZuiBox.CaptureView/ApplyView round-trip
// ("<boxKey>/fold", "<boxKey>/gear", "<boxKey>/<controlKey>") — never a display label — so a view
// survives a relabel.
//
// NO [CreateAssetMenu] — by the Laubrary "no unrequested menu items" rule. The asset is born the first
// time a tool's ZuiViewBar Save-as runs with no store yet: the HOST creates it (at a path of its
// choosing) and hands it back; the bar never touches AssetDatabase itself. This type lives in the
// editor-only Zui toolkit asmdef, so although the data is plain-serializable it is only ever used as
// editor tooling, never shipped in a build.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zui
{
    /// One captured view-state bit: a stable ZuiBox key and its on/off. Never a display label.
    [System.Serializable]
    public class ZuiViewEntry
    {
        public string key;
        public bool val;
    }

    /// A named arrangement of a whole window's view state — a flat list of keyed on/off bits.
    [System.Serializable]
    public class ZuiViewPreset
    {
        public string name;
        public List<ZuiViewEntry> entries = new List<ZuiViewEntry>();
    }

    public class ZuiViewStore : ScriptableObject
    {
        public List<ZuiViewPreset> presets = new List<ZuiViewPreset>();
    }
}
