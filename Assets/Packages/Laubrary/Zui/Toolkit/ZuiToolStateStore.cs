// ZuiToolStateStore — the committed store of a tool's CURRENT editor arrangement.
//
// Sibling to ZuiViewStore, and the division between them is the point:
//   ZuiViewStore     NAMED presets a user explicitly saves and picks ("Compact", "Everything").
//   ZuiToolStateStore  the ONE live arrangement a tool is in right now, saved automatically so it is
//                      still there next session. Nobody names it and nobody picks it.
//
// It is GLOBAL PER TOOL, not per asset: one backdrop and one set of folds for TextSplash, whichever
// splash is open. Per-asset state was considered and deliberately dropped — it needs a GUID-keyed
// sidecar, and the arrangement people actually want restored is "how I like this tool", not "how I
// last looked at this one asset".
//
// It is COMMITTED (an asset under the host project's Assets/), so an arrangement travels with the
// project and with the team rather than living in one machine's EditorPrefs. That is a deliberate
// trade: it means the asset shows up in a diff when someone folds a box. The alternative — a
// gitignored UserSettings JSON — is invisible to git but also invisible to everyone else, and cannot
// serialize a Sprite reference without a GUID codec. Committed wins on both counts here.
//
// The VALUES are deliberately generic (flags / numbers / vectors / colours / object refs) rather than
// typed per feature, so this toolkit never has to reference BackSplash, Pyre or any consumer to store
// their state — a tool composes its own keys ("textsplash.backdrop.zoom") and owns their meaning.
//
// NO [CreateAssetMenu], by the Laubrary "no unrequested menu items" rule — the asset is created on
// demand by ZuiToolStateProvider. This type lives in the editor-only Zui toolkit asmdef, so although
// the data is plain-serializable it is only ever editor tooling and is never shipped in a build.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zui
{
    [System.Serializable] public class ZuiFloatEntry  { public string key; public float val; }
    [System.Serializable] public class ZuiVec2Entry   { public string key; public Vector2 val; }
    [System.Serializable] public class ZuiColorEntry  { public string key; public Color val; }
    [System.Serializable] public class ZuiObjectEntry { public string key; public Object val; }

    /// One tool's whole arrangement. `toolId` is the tool's own stable slug ("textsplash"), never a
    /// display name — a window retitle must not orphan a saved arrangement.
    [System.Serializable]
    public class ZuiToolState
    {
        public string toolId;
        public List<ZuiViewEntry> flags = new List<ZuiViewEntry>();
        public List<ZuiFloatEntry> numbers = new List<ZuiFloatEntry>();
        public List<ZuiVec2Entry> vectors = new List<ZuiVec2Entry>();
        public List<ZuiColorEntry> colors = new List<ZuiColorEntry>();
        public List<ZuiObjectEntry> objects = new List<ZuiObjectEntry>();

        // Every getter takes the caller's own default and returns it when the key is absent. That is what
        // makes adopting this free: a tool that has never been saved behaves exactly as it did before,
        // and a NEW dial added later reads its declared default until the user first touches it.
        public bool GetBool(string key, bool fallback)
        {
            var e = flags.Find(x => x.key == key);
            return e != null ? e.val : fallback;
        }

        public void SetBool(string key, bool v)
        {
            var e = flags.Find(x => x.key == key);
            if (e != null) e.val = v; else flags.Add(new ZuiViewEntry { key = key, val = v });
        }

        public float GetFloat(string key, float fallback)
        {
            var e = numbers.Find(x => x.key == key);
            return e != null ? e.val : fallback;
        }

        public void SetFloat(string key, float v)
        {
            var e = numbers.Find(x => x.key == key);
            if (e != null) e.val = v; else numbers.Add(new ZuiFloatEntry { key = key, val = v });
        }

        public Vector2 GetVector(string key, Vector2 fallback)
        {
            var e = vectors.Find(x => x.key == key);
            return e != null ? e.val : fallback;
        }

        public void SetVector(string key, Vector2 v)
        {
            var e = vectors.Find(x => x.key == key);
            if (e != null) e.val = v; else vectors.Add(new ZuiVec2Entry { key = key, val = v });
        }

        public Color GetColor(string key, Color fallback)
        {
            var e = colors.Find(x => x.key == key);
            return e != null ? e.val : fallback;
        }

        public void SetColor(string key, Color v)
        {
            var e = colors.Find(x => x.key == key);
            if (e != null) e.val = v; else colors.Add(new ZuiColorEntry { key = key, val = v });
        }

        /// Object references are REAL serialized references, not GUID strings — the whole reason this store
        /// is a committed asset rather than a JSON file. A reference to a deleted asset comes back null,
        /// which every caller already has to handle for "no image chosen".
        public Object GetObject(string key)
        {
            var e = objects.Find(x => x.key == key);
            return e != null ? e.val : null;
        }

        /// Distinguishes "never saved" from "saved as none" — without it, restoring would CLEAR an image the
        /// user had set before this store existed.
        public bool HasObject(string key) => objects.Exists(x => x.key == key);

        public void SetObject(string key, Object v)
        {
            var e = objects.Find(x => x.key == key);
            if (e != null) e.val = v; else objects.Add(new ZuiObjectEntry { key = key, val = v });
        }
    }

    public class ZuiToolStateStore : ScriptableObject
    {
        public List<ZuiToolState> tools = new List<ZuiToolState>();

        /// The entry for a tool, created on first use. Never returns null for a non-empty id, so callers
        /// read and write without a null dance.
        public ZuiToolState For(string toolId)
        {
            if (string.IsNullOrEmpty(toolId)) return null;
            var t = tools.Find(x => x != null && x.toolId == toolId);
            if (t == null) { t = new ZuiToolState { toolId = toolId }; tools.Add(t); }
            return t;
        }
    }
}
