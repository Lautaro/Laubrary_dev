// SwatchPalette.cs
// A shared, runtime-usable set of named colour SWATCHES. A ZuiSwatchRef points at an entry by name;
// change the entry and every ref following it changes. This is the CONTENT-colour counterpart to the
// editor theme palette (ZUIStyleSheetAsset's palette): same ZUIPaletteColor + autocolor machinery,
// but a standalone asset so effect/game colours never depend on which editor skin is loaded.
//
// Change propagation is a cheap VERSION stamp: any edit bumps Version, and readers (ZuiSwatchRef /
// ZuiSwatchBinding) re-resolve lazily only when the version they last saw is stale — so a runtime
// swatch change is free (bump an int) and steady-state resolution is an array index.

using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SwatchPalette", menuName = "Laubrary/ZUI/Swatch Palette")]
public class SwatchPalette : ScriptableObject
{
    [Tooltip("The named swatches. A ZuiSwatchRef resolves against these by name; each entry can carry " +
             "autocolors (HSV-derived variants that track its base colour).")]
    public List<ZUIPaletteColor> swatches = new List<ZUIPaletteColor>();

    // Runtime change counter — NOT serialised, so bumping it never dirties the asset (no churn) and a
    // domain reload resets it to 0, which merely makes every reader re-resolve once (harmless).
    [System.NonSerialized] int _version;

    /// <summary>Bumped on every change; readers cache against it and re-resolve only when it moves.</summary>
    public int Version => _version;

    /// <summary>Call after mutating a swatch at runtime (colour/name/autocolor) so bound readers update.</summary>
    public void Bump() => _version++;

    void OnValidate() => _version++;   // editor edits (inspector / swatch editor) propagate for free

    /// <summary>Index of the swatch with this name, or -1. Linear (palettes are small); callers cache it.</summary>
    public int IndexOf(string swatchName)
    {
        if (string.IsNullOrEmpty(swatchName) || swatches == null) return -1;
        for (int i = 0; i < swatches.Count; i++)
            if (swatches[i] != null && swatches[i].name == swatchName) return i;
        return -1;
    }

    /// <summary>The entry at <paramref name="index"/>, or null if out of range.</summary>
    public ZUIPaletteColor At(int index)
        => (swatches != null && index >= 0 && index < swatches.Count) ? swatches[index] : null;

    /// <summary>The base colour at <paramref name="index"/>, or <paramref name="fallback"/> if out of range.</summary>
    public Color ColorAt(int index, Color fallback)
    {
        var s = At(index);
        return s != null ? s.color : fallback;
    }

    /// <summary>Resolve a swatch by index, applying a named autocolor variant if given (empty = base
    /// colour). Returns <paramref name="fallback"/> when the index is out of range.</summary>
    public Color Resolve(int index, string autoColorRef, Color fallback)
    {
        var s = At(index);
        if (s == null) return fallback;
        if (!string.IsNullOrEmpty(autoColorRef) && s.autoColors != null)
        {
            var ac = s.autoColors.Find(a => a != null && a.name == autoColorRef);
            if (ac != null) return ac.Resolve(s.color);
        }
        return s.color;
    }

    /// <summary>Convenience: mutate a swatch's base colour at runtime and bump the version.</summary>
    public void SetColor(int index, Color color)
    {
        var s = At(index);
        if (s == null) return;
        s.color = color;
        _version++;
    }
}
