// ZUIEnvelopePresetLibrary.cs
// Project-level saved envelope presets — the per-project tier alongside ZUI's own hard-coded
// ZUIEnvelopeBuiltInPresets. One shared library per project, found by type, created on demand under
// Assets/ZUI/ (host-project authoring space, never inside the package) — mirrors PyreLayerLibrary exactly.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ZUIEnvelopePresetLibrary : ScriptableObject
{
    public List<ZUIEnvelopePreset> presets = new List<ZUIEnvelopePreset>();

    const string DefaultPath = "Assets/ZUI/ZUIEnvelopePresets.asset";
    static ZUIEnvelopePresetLibrary _cached;

    /// The shared library, loading the existing asset or (optionally) creating a fresh one.
    public static ZUIEnvelopePresetLibrary Load(bool createIfMissing = true)
    {
        if (_cached != null) return _cached;

        foreach (var guid in AssetDatabase.FindAssets("t:ZUIEnvelopePresetLibrary"))
        {
            _cached = AssetDatabase.LoadAssetAtPath<ZUIEnvelopePresetLibrary>(AssetDatabase.GUIDToAssetPath(guid));
            if (_cached != null) return _cached;
        }
        if (!createIfMissing) return null;

        if (!AssetDatabase.IsValidFolder("Assets/ZUI")) AssetDatabase.CreateFolder("Assets", "ZUI");
        _cached = CreateInstance<ZUIEnvelopePresetLibrary>();
        AssetDatabase.CreateAsset(_cached, DefaultPath);
        AssetDatabase.SaveAssets();
        return _cached;
    }

    /// Stores a deep clone of the given (already-normalized [0,1]) points under a name.
    public void Add(string presetName, List<ZUIEnvelopePoint> normalizedPoints)
    {
        var preset = new ZUIEnvelopePreset { name = string.IsNullOrWhiteSpace(presetName) ? "(unnamed)" : presetName };
        foreach (var p in normalizedPoints)
            preset.points.Add(new ZUIEnvelopePoint(p.time, p.value, p.exponent, p.editState));
        presets.Add(preset);
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
    }

    public void RemoveAt(int i)
    {
        if (i < 0 || i >= presets.Count) return;
        presets.RemoveAt(i);
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
    }
}
