// ZUIEnvelopePresetPopup.cs
// The picker popup for BOTH envelope preset tiers — ZUI's own hard-coded ZUIEnvelopeBuiltInPresets and the
// current project's ZUIEnvelopePresetLibrary — each row showing a live shape thumbnail (the same
// ZUI.DrawCurveThumbnail every folded curve field already uses) so picking a shape is visual, not
// name-guessing. A "Save current" row at the top adds the CALLER's live points to the project library,
// normalized to [0,1] first (see ZUIEnvelopePreset.cs for why).
//
// Opened the same way as PyreLayerLibraryPopup: PopupWindow.Show(activatorRect, new ZUIEnvelopePresetPopup(...)).

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ZUIEnvelopePresetPopup : PopupWindowContent
{
    readonly List<ZUIEnvelopePoint> target;
    readonly float yMin, yMax, width;
    readonly ZUIEnvelopePresetLibrary lib;
    string newName = "";
    Vector2 scroll;

    public ZUIEnvelopePresetPopup(List<ZUIEnvelopePoint> target, float yMin, float yMax, float width = 260f)
    {
        this.target = target;
        this.yMin = yMin; this.yMax = yMax;
        this.width = Mathf.Max(220f, width);
        lib = ZUIEnvelopePresetLibrary.Load();
    }

    public override Vector2 GetWindowSize()
    {
        int rows = ZUIEnvelopeBuiltInPresets.All.Length + (lib != null ? lib.presets.Count : 0);
        return new Vector2(width, 96f + Mathf.Clamp(rows, 1, 10) * 40f);
    }

    public override void OnGUI(Rect rect)
    {
        EditorGUILayout.LabelField("Save current shape as…", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        newName = EditorGUILayout.TextField(newName);
        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newName)))
        {
            if (GUILayout.Button("Save", GUILayout.Width(50f)))
            {
                lib.Add(newName, Normalized());
                newName = "";
                GUI.FocusControl(null);
                editorWindow.Repaint();
            }
        }
        EditorGUILayout.EndHorizontal();

        scroll = EditorGUILayout.BeginScrollView(scroll);

        GUILayout.Label("Built-in", EditorStyles.miniBoldLabel);
        foreach (var preset in ZUIEnvelopeBuiltInPresets.All)
            Row(preset.name, preset.points, null);

        GUILayout.Space(6f);
        GUILayout.Label("This project", EditorStyles.miniBoldLabel);
        if (lib == null || lib.presets.Count == 0)
            EditorGUILayout.HelpBox("No saved presets yet — name one above and hit Save.", MessageType.Info);
        else
        {
            int del = -1;
            for (int i = 0; i < lib.presets.Count; i++)
            {
                int captured = i;
                Row(lib.presets[i].name, lib.presets[i].points, () => del = captured);
            }
            if (del >= 0) { lib.RemoveAt(del); editorWindow.Repaint(); }
        }

        EditorGUILayout.EndScrollView();
    }

    void Row(string presetName, List<ZUIEnvelopePoint> points, System.Action onDelete)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

        Rect thumb = GUILayoutUtility.GetRect(48f, 32f, GUILayout.Width(48f), GUILayout.Height(32f));
        ZUI.DrawCurveThumbnail(thumb, points, 0f, 1f);

        if (GUILayout.Button(presetName, EditorStyles.miniButton, GUILayout.ExpandWidth(true), GUILayout.Height(32f)))
        {
            Apply(points);
            editorWindow.Close();
        }
        if (onDelete != null &&
            GUILayout.Button(new GUIContent("x", "Delete this project preset."), GUILayout.Width(20f), GUILayout.Height(32f)))
            onDelete();

        EditorGUILayout.EndHorizontal();
    }

    // Remaps the preset's normalized [0,1] points onto THIS field's own yMin..yMax before writing them in.
    void Apply(List<ZUIEnvelopePoint> presetPoints)
    {
        target.Clear();
        foreach (var p in presetPoints)
            target.Add(new ZUIEnvelopePoint(p.time, Mathf.Lerp(yMin, yMax, p.value), p.exponent, p.editState));
        GUI.changed = true;
    }

    List<ZUIEnvelopePoint> Normalized()
    {
        var result = new List<ZUIEnvelopePoint>();
        float range = yMax - yMin;
        foreach (var p in target)
            result.Add(new ZUIEnvelopePoint(p.time, range > 0.0001f ? Mathf.InverseLerp(yMin, yMax, p.value) : 0f, p.exponent, p.editState));
        return result;
    }
}
