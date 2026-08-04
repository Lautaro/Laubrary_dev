using UnityEditor;
using UnityEngine;
using Laubrary.ZoetropeLaunimator;
using Laubrary.Launimator;
using Laubrary.Launimator.Editor;

namespace Laubrary.ZoetropeLaunimator.Editor
{
    /// <summary>
    /// Adds an "Open in Laumination Builder" button to a <see cref="LauminaryView"/>'s inspector — the missing editor
    /// half of the runtime Launimator bridge. Without this, a Zoetrope recipe's <c>version</c> field is just a
    /// bare object reference: selecting it lands on LauminaryVersion's default (useless) inspector, since LauminaryVersion
    /// has no custom editor of its own and no back-reference to the Lauminary that owns it. This drawer resolves the
    /// owning Lauminary by folder convention (every version lives under its Lauminary's own folder — see
    /// <see cref="LauminaryRepo"/>) and opens it directly in the Laumination Builder.
    /// </summary>
    [CustomPropertyDrawer(typeof(LauminaryView))]
    public class LauminaryViewDrawer : PropertyDrawer
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

            var version = versionProp.objectReferenceValue as LauminaryVersion;
            using (new EditorGUI.DisabledScope(version == null))
            {
                if (GUI.Button(r, "Open in Laumination Builder"))
                {
                    var lauminary = FindOwningLauminary(version);
                    if (lauminary != null)
                        LauminationBuilderWindow.OpenForEdit(lauminary, idleClipProp.stringValue);
                    else
                        Debug.LogWarning($"LauminaryViewDrawer: couldn't find the Lauminary owning '{AssetDatabase.GetAssetPath(version)}' " +
                                          $"(expected it somewhere under {LauminaryRepo.Root}).");
                }
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineH = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            return (lineH + pad) * FieldCount;
        }

        static Lauminary FindOwningLauminary(LauminaryVersion version)
        {
            if (version == null) return null;
            string versionPath = AssetDatabase.GetAssetPath(version);
            if (string.IsNullOrEmpty(versionPath)) return null;
            foreach (var lauminary in LauminaryRepo.EnumerateLauminaries())
            {
                string folder = LauminaryRepo.FolderOf(lauminary);
                if (!string.IsNullOrEmpty(folder) &&
                    versionPath.StartsWith(folder + "/", System.StringComparison.OrdinalIgnoreCase))
                    return lauminary;
            }
            return null;
        }
    }
}
