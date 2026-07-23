using System.Linq;
using UnityEditor;
using UnityEngine;
using Laubrary.ZoetropeLaunimator;
using Laubrary.Launimator;
using Laubrary.Launimator.Editor;

namespace Laubrary.ZoetropeLaunimator.Editor
{
    /// <summary>
    /// Adds an "Open in Animation Builder" button to a <see cref="ZonedReelView"/>'s inspector — the missing
    /// editor half of the runtime Launimator bridge. Without this, a Zoetrope recipe's <c>version</c> field is
    /// just a bare object reference: selecting it lands on ReelVersion's default (useless) inspector, since
    /// ReelVersion has no custom editor of its own and no back-reference to the Reel that owns it. This drawer
    /// resolves the owning Reel by folder convention (every version lives under its Reel's own folder — see
    /// <see cref="ReelRepo"/>) and opens it directly in the Animation Builder. (Originally built for the now-
    /// retired plain <c>ReelView</c>, which had identical version/idleClip/height fields — retargeted here
    /// rather than duplicated once <c>ReelView</c> was folded into this one.)
    /// </summary>
    [CustomPropertyDrawer(typeof(ZonedReelView))]
    public class ZonedReelViewDrawer : PropertyDrawer
    {
        const int FieldCount = 2; // version, then idleClip+height+button all sharing one row

        // A plain number/short string doesn't need the full inspector row (ui-layout-rules.md "no infinite-
        // width controls") — this drawer is a Rect-based PropertyDrawer, a completely separate code path from
        // ZoetropeWindows.cs's generic per-child walk (that's the whole reason this class exists — see its own
        // doc comment), so DrawScalarField's width cap there never reaches Height/Idle Clip here; needed its
        // own fix. Version is left full-width — an asset name is a legitimate width exception. Idle Clip,
        // Height and the "Open in Animation Builder" button all share one row per user request — three short
        // related controls that don't need three rows. Clip/Height widths are sized via ZUI.FitWidth against
        // their own widest possible text (not a guessed fixed constant) so neither the label nor the value
        // ever clips, whatever clip names or height happen to be authored.
        const float ButtonMinWidth = 150f;
        const float PairGap = 8f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var versionProp = property.FindPropertyRelative("version");
            var idleClipProp = property.FindPropertyRelative("idleClip");
            var heightProp = property.FindPropertyRelative("height");

            float lineH = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            var r = new Rect(position.x, position.y, position.width, lineH);

            EditorGUI.PropertyField(r, versionProp);
            r.y += lineH + pad;

            // Idle Clip is the exact same "pick a name out of this Reel version's animation list" case
            // TryDrawClipPopup (ZoetropeWindows.cs) handles elsewhere — free text here was a real
            // inconsistency: the identical concept (Zoe.hitReaction's hurtClip/deathClip) gets a validated
            // dropdown, this one didn't, purely because this drawer bypasses that generic walk.
            var version = versionProp.objectReferenceValue as ReelVersion;
            string[] names = version != null && version.animations != null
                ? version.animations.Where(a => !string.IsNullOrEmpty(a.name)).Select(a => a.name).ToArray()
                : System.Array.Empty<string>();
            float clipWidth = names.Length > 0
                ? ZUI.FitWidth("Idle Clip", Widest(names), 100f, 260f)
                : ZUI.FitWidth("Idle Clip", idleClipProp.stringValue, 100f, 260f);
            float heightWidth = ZUI.FitWidth("Height", heightProp.floatValue.ToString("0.##"), 60f, 140f);

            var clipRect = new Rect(r.x, r.y, clipWidth, lineH);
            var heightRect = new Rect(clipRect.xMax + PairGap, r.y, heightWidth, lineH);
            var buttonRect = new Rect(heightRect.xMax + PairGap, r.y,
                Mathf.Max(ButtonMinWidth, r.xMax - (heightRect.xMax + PairGap)), lineH);

            // EditorGUIUtility.labelWidth is a GLOBAL ambient value — without pinning it per field, Unity
            // reserves its ambient (often much larger, in a wide window) labelWidth out of these fixed Rects
            // before the value control gets anything, which is exactly why Height "didn't work": the field
            // itself was being squeezed to near-zero width, not actually broken.
            using (ZUI.NarrowLabel("Idle Clip"))
            {
                if (names.Length > 0)
                {
                    int current = System.Array.IndexOf(names, idleClipProp.stringValue);
                    int chosen = EditorGUI.Popup(clipRect, "Idle Clip", Mathf.Max(current, 0), names);
                    idleClipProp.stringValue = names[Mathf.Clamp(chosen, 0, names.Length - 1)];
                }
                else
                {
                    EditorGUI.PropertyField(clipRect, idleClipProp);
                }
            }
            using (ZUI.NarrowLabel("Height"))
                EditorGUI.PropertyField(heightRect, heightProp);

            using (new EditorGUI.DisabledScope(version == null))
            {
                if (GUI.Button(buttonRect, "Open in Animation Builder"))
                {
                    var reel = FindOwningReel(version);
                    if (reel != null)
                        AnimationBuilderWindow.OpenForEdit(reel, idleClipProp.stringValue);
                    else
                        Debug.LogWarning($"ZonedReelViewDrawer: couldn't find the Reel owning '{AssetDatabase.GetAssetPath(version)}' " +
                                          $"(expected it somewhere under {ReelRepo.Root}).");
                }
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineH = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            return (lineH + pad) * FieldCount;
        }

        /// The widest string in <paramref name="options"/> by rendered label width — sizing a control against
        /// this (via <see cref="ZUI.FitWidth"/>) guarantees no clipping no matter which option ends up chosen.
        static string Widest(string[] options)
        {
            if (options == null || options.Length == 0) return "";
            string best = options[0];
            float bestW = EditorStyles.label.CalcSize(new GUIContent(best ?? "")).x;
            for (int i = 1; i < options.Length; i++)
            {
                float w = EditorStyles.label.CalcSize(new GUIContent(options[i] ?? "")).x;
                if (w > bestW) { bestW = w; best = options[i]; }
            }
            return best ?? "";
        }

        static Reel FindOwningReel(ReelVersion version)
        {
            if (version == null) return null;
            string versionPath = AssetDatabase.GetAssetPath(version);
            if (string.IsNullOrEmpty(versionPath)) return null;
            foreach (var reel in ReelRepo.EnumerateReels())
            {
                string folder = ReelRepo.FolderOf(reel);
                if (!string.IsNullOrEmpty(folder) &&
                    versionPath.StartsWith(folder + "/", System.StringComparison.OrdinalIgnoreCase))
                    return reel;
            }
            return null;
        }
    }
}
