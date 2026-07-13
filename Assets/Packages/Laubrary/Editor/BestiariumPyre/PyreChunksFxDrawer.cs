using UnityEditor;
using UnityEngine;
using Laubrary.BestiariumPyre;
using Laubrary.Pyre;
using Laubrary.Pyre.Editor;

namespace Laubrary.BestiariumPyre.Editor
{
    /// <summary>
    /// Adds a "Preview in Pyre" button to a <see cref="PyreChunksFx"/>'s inspector — the missing editor half of
    /// the runtime Pyre/Chunks bridge, so a Bestiarium recipe's hit/death/muzzle/impact effect opens straight
    /// into Pyre's live WYSIWYG editor instead of just sitting there as an inert object reference.
    /// </summary>
    [CustomPropertyDrawer(typeof(PyreChunksFx))]
    public class PyreChunksFxDrawer : PropertyDrawer
    {
        const int FieldCount = 5; // blast, blastFps, chunks, sortingOrder, the button

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var blastProp = property.FindPropertyRelative("blast");
            var fpsProp = property.FindPropertyRelative("blastFps");
            var chunksProp = property.FindPropertyRelative("chunks");
            var sortProp = property.FindPropertyRelative("sortingOrder");

            float lineH = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            var r = new Rect(position.x, position.y, position.width, lineH);

            EditorGUI.PropertyField(r, blastProp);
            r.y += lineH + pad;
            EditorGUI.PropertyField(r, fpsProp);
            r.y += lineH + pad;
            EditorGUI.PropertyField(r, chunksProp);
            r.y += lineH + pad;
            EditorGUI.PropertyField(r, sortProp);
            r.y += lineH + pad;

            var blast = blastProp.objectReferenceValue as BlastSpec;
            using (new EditorGUI.DisabledScope(blast == null))
            {
                if (GUI.Button(r, "Preview in Pyre"))
                    PyreWindow.OpenFor(blast);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineH = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            return (lineH + pad) * FieldCount;
        }
    }
}
