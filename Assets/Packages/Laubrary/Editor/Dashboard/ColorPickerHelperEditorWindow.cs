using UnityEngine;
using UnityEditor;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector;

public class ColorPickerHelper : OdinEditorWindow
{
    [ShowInInspector, ColorPalette]
    public Color SelectedColor { get; set; } = Color.white;

    [ShowInInspector, PropertyOrder(-1)]
    public string RGBValues => $"RGB: ({SelectedColor.r:F2}, {SelectedColor.g:F2}, {SelectedColor.b:F2})";

    protected override void OnGUI()
    {
        base.OnImGUI(); // Call the base OnGUI to draw the Odin Inspector elements

        // Now draw the custom label with the selected color
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            normal = { textColor = SelectedColor }
        };
        GUILayout.Label("This is a sample text using the selected color.", labelStyle);
    }

    [MenuItem("Tools/Color Picker Helper")]
    private static void ShowWindow()
    {
        var window = GetWindow<ColorPickerHelper>();
        window.titleContent = new GUIContent("Color Picker Helper");
        window.Show();
    }
}