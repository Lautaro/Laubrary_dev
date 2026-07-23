// ZUIRequireInterfaceDrawer.cs
// Draws a [RequireInterface(typeof(T))]-tagged Object field using Unity's own interface-aware
// EditorGUI.ObjectField overload (supported since Unity added interface support to ObjectField) — this
// constrains BOTH drag-drop acceptance and the asset-browse popup to objects actually implementing T,
// instead of the default "any UnityEngine.Object at all" behaviour a plain `public Object` field gets.
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(RequireInterfaceAttribute))]
public class ZUIRequireInterfaceDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.ObjectReference)
        {
            EditorGUI.LabelField(position, label.text, "Use [RequireInterface] on an Object field only.");
            return;
        }

        var iface = ((RequireInterfaceAttribute)attribute).InterfaceType;
        EditorGUI.BeginProperty(position, label, property);
        EditorGUI.BeginChangeCheck();
        var picked = EditorGUI.ObjectField(position, label, property.objectReferenceValue, iface, false);
        if (EditorGUI.EndChangeCheck()) property.objectReferenceValue = picked;
        EditorGUI.EndProperty();
    }
}
