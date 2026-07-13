using UnityEditor;
using UnityEngine;
using Laubrary.BestiariumZoetrope;
using Laubrary.Launimator;
using Laubrary.Launimator.Editor;

namespace Laubrary.BestiariumZoetrope.Editor
{
    /// <summary>
    /// Adds an "Open in Animation Builder" button to a <see cref="ReelView"/>'s inspector — the missing editor
    /// half of the runtime Launimator bridge. Without this, a Bestiarium recipe's <c>version</c> field is just a
    /// bare object reference: selecting it lands on ReelVersion's default (useless) inspector, since ReelVersion
    /// has no custom editor of its own and no back-reference to the Reel that owns it. This drawer resolves the
    /// owning Reel by folder convention (every version lives under its Reel's own folder — see
    /// <see cref="ReelRepo"/>) and opens it directly in the Animation Builder.
    /// </summary>
    [CustomPropertyDrawer(typeof(ReelView))]
    public class ReelViewDrawer : PropertyDrawer
    {
        const int FieldCount = 4; // version, idleClip, height, the button

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
            EditorGUI.PropertyField(r, idleClipProp);
            r.y += lineH + pad;
            EditorGUI.PropertyField(r, heightProp);
            r.y += lineH + pad;

            var version = versionProp.objectReferenceValue as ReelVersion;
            using (new EditorGUI.DisabledScope(version == null))
            {
                if (GUI.Button(r, "Open in Animation Builder"))
                {
                    var reel = FindOwningReel(version);
                    if (reel != null)
                        AnimationBuilderWindow.OpenForEdit(reel, idleClipProp.stringValue);
                    else
                        Debug.LogWarning($"ReelViewDrawer: couldn't find the Reel owning '{AssetDatabase.GetAssetPath(version)}' " +
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
