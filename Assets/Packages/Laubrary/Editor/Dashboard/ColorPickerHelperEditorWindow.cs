using UnityEngine;
using UnityEditor;

public class ColorPickerHelper : EditorWindow
{
    public Color SelectedColor { get; set; } = Color.white;

    public string RGBValues => $"RGB: ({SelectedColor.r:F2}, {SelectedColor.g:F2}, {SelectedColor.b:F2})";


    [MenuItem("Tools/Color Picker Helper")]
    private static void ShowWindow()
    {
        var window = GetWindow<ColorPickerHelper>();
        window.titleContent = new GUIContent("Color Picker Helper");
        window.Show();
    }
}