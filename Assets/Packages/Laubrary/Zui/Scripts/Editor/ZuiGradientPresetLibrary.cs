// ZuiGradientPresetLibrary.cs — T-0205, unlimited stops since T-0221.
// Project-level saved GRADIENTS — the "important colours" library the owner asked to be shared between the
// gradient control Fill uses and every ramp control (RampByQuantity, OverPhase, Procedural noise, Pyre's
// hosted ramps). One shared library per project, found by type, created on demand under Assets/ZUI/
// (host-project authoring space, never inside the package) — mirrors ZUIEnvelopePresetLibrary and
// PyreLayerLibrary exactly, so this is the SAME pattern the project already trusts for a "this project's
// saved shapes" tier, not a new idiom.
//
// An entry stores a STOP LIST and a blend space, not a UnityEngine.Gradient: the 8-key cap that used to be
// inherited here is what made a 10-stop Pyre ramp impossible to save and reload whole. Entries written before
// that carry their Gradient in `gradient` and are converted on read (`Resolve`), so an existing library asset
// keeps working untouched and is only rewritten when the author saves into it again.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[System.Serializable]
public class ZuiGradientPresetEntry
{
    public string name;

    /// Legacy storage: entries saved before the stop list existed. Read-only from here on — `Resolve` converts it.
    public Gradient gradient;

    /// The saved ramp, at any stop count.
    public List<ZuiGradientStop> stops = new List<ZuiGradientStop>();
    public ZuiGradientSpace space = ZuiGradientSpace.Srgb;

    /// <summary>This entry as a fresh ZuiGradient the caller owns outright — never a live reference into the
    /// library's own data, so applying one and then editing it cannot rewrite the saved entry.</summary>
    public ZuiGradient Resolve()
    {
        var zg = new ZuiGradient();
        if (stops != null && stops.Count > 0)
        {
            var into = zg.Stops;
            into.Clear();
            foreach (var s in stops) into.Add(new ZuiGradientStop(s.pos, s.color));
            zg.MarkStopsChanged();
            zg.BlendMode = (int)space;
        }
        else
        {
            zg.SetGradient(gradient);   // a pre-T-0221 entry: its Gradient keys become stops
        }
        return zg;
    }
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

    /// Stores a deep copy of <paramref name="g"/>'s stops under a name — every stop, whatever the count, and
    /// never a live reference back into the caller's own gradient.
    public void Add(string presetName, ZuiGradient g)
    {
        if (g == null || g.Count == 0) return;
        var entry = new ZuiGradientPresetEntry
        {
            name = string.IsNullOrWhiteSpace(presetName) ? "(unnamed)" : presetName,
            space = (ZuiGradientSpace)g.BlendMode,
        };
        foreach (var s in g.Stops) entry.stops.Add(new ZuiGradientStop(s.pos, s.color));
        presets.Add(entry);
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
