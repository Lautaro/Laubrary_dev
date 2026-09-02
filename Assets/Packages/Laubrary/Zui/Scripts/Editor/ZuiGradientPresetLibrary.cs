// ZuiGradientPresetLibrary.cs — T-0205.
// Project-level saved GRADIENTS — the "important colours" library the owner asked to be shared between the
// gradient control Fill uses and every ramp control (RampByQuantity, OverPhase, Procedural noise, Pyre's
// hosted ramps). One shared library per project, found by type, created on demand under Assets/ZUI/
// (host-project authoring space, never inside the package) — mirrors ZUIEnvelopePresetLibrary and
// PyreLayerLibrary exactly, so this is the SAME pattern the project already trusts for a "this project's
// saved shapes" tier, not a new idiom.
//
// A UnityEngine.Gradient caps at 8 colour keys and 8 alpha keys — that ceiling is inherited here on purpose
// (see ZuiGradientPresetPopup's save path): a saved entry is always a byte-faithful Gradient, never a
// lossy approximation of itself. The lossy step, when there is one, happens once, on the way IN from a
// ramp with more than 8 stops (ZuiRampGradientBridge.ToGradient) — never on save/load of an already-fewer-
// than-9-stop gradient, and never silently on a project's existing saved presets.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[System.Serializable]
public class ZuiGradientPresetEntry
{
    public string name;
    public Gradient gradient;
}

public class ZuiGradientPresetLibrary : ScriptableObject
{
    public List<ZuiGradientPresetEntry> presets = new List<ZuiGradientPresetEntry>();

    const string DefaultPath = "Assets/ZUI/ZuiGradientPresets.asset";
    static ZuiGradientPresetLibrary _cached;

    /// The shared library, loading the existing asset or (optionally) creating a fresh one.
    public static ZuiGradientPresetLibrary Load(bool createIfMissing = true)
    {
        if (_cached != null) return _cached;

        foreach (var guid in AssetDatabase.FindAssets("t:ZuiGradientPresetLibrary"))
        {
            _cached = AssetDatabase.LoadAssetAtPath<ZuiGradientPresetLibrary>(AssetDatabase.GUIDToAssetPath(guid));
            if (_cached != null) return _cached;
        }
        if (!createIfMissing) return null;

        if (!AssetDatabase.IsValidFolder("Assets/ZUI")) AssetDatabase.CreateFolder("Assets", "ZUI");
        _cached = CreateInstance<ZuiGradientPresetLibrary>();
        AssetDatabase.CreateAsset(_cached, DefaultPath);
        AssetDatabase.SaveAssets();
        return _cached;
    }

    /// Stores a deep clone of <paramref name="g"/> under a name — never a live reference back into the
    /// caller's own gradient, so editing the caller afterward can never mutate the saved entry.
    public void Add(string presetName, Gradient g)
    {
        if (g == null) return;
        var clone = new Gradient();
        clone.SetKeys(g.colorKeys, g.alphaKeys);
        clone.mode = g.mode;
        presets.Add(new ZuiGradientPresetEntry
        {
            name = string.IsNullOrWhiteSpace(presetName) ? "(unnamed)" : presetName,
            gradient = clone,
        });
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
