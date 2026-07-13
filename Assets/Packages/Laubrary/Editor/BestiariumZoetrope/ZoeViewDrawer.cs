using UnityEditor;
using UnityEngine;
using Laubrary.BestiariumZoetrope;
using Laubrary.Zoetrope;
using Laubrary.Zoetrope.Editor;

namespace Laubrary.BestiariumZoetrope.Editor
{
    /// <summary>
    /// Adds an "Open in Animation Builder" button to a <see cref="ZoeView"/>'s inspector — the missing editor
    /// half of the runtime Zoetrope bridge. Without this, a Bestiarium recipe's <c>version</c> field is just a
    /// bare object reference: selecting it lands on ZoeVersion's default (useless) inspector, since ZoeVersion
    /// has no custom editor of its own and no back-reference to the Zoe that owns it. This drawer resolves the
    /// owning Zoe by folder convention (every version lives under its Zoe's own folder — see
    /// <see cref="ZoeRepo"/>) and opens it directly in the Animation Builder.
    /// </summary>
    [CustomPropertyDrawer(typeof(ZoeView))]
    public class ZoeViewDrawer : PropertyDrawer
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

            var version = versionProp.objectReferenceValue as ZoeVersion;
            using (new EditorGUI.DisabledScope(version == null))
            {
                if (GUI.Button(r, "Open in Animation Builder"))
                {
                    var zoe = FindOwningZoe(version);
                    if (zoe != null)
                        AnimationBuilderWindow.OpenForEdit(zoe, idleClipProp.stringValue);
                    else
                        Debug.LogWarning($"ZoeViewDrawer: couldn't find the Zoe owning '{AssetDatabase.GetAssetPath(version)}' " +
                                          $"(expected it somewhere under {ZoeRepo.Root}).");
                }
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineH = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            return (lineH + pad) * FieldCount;
        }

        static Zoe FindOwningZoe(ZoeVersion version)
        {
            if (version == null) return null;
            string versionPath = AssetDatabase.GetAssetPath(version);
            if (string.IsNullOrEmpty(versionPath)) return null;
            foreach (var zoe in ZoeRepo.EnumerateZoes())
            {
                string folder = ZoeRepo.FolderOf(zoe);
                if (!string.IsNullOrEmpty(folder) &&
                    versionPath.StartsWith(folder + "/", System.StringComparison.OrdinalIgnoreCase))
                    return zoe;
            }
            return null;
        }
    }
}
