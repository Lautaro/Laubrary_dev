// ZUIPalette.cs
using System;
using System.Collections.Generic;
using UnityEngine;

// Kept for serialization backwards compatibility — existing assets have slot: 0 serialized.
// Only Primary is meaningful; all other values were migrated to autoColorRef.
public enum ZUIPaletteSlot { Primary }

[Serializable]
public class ZUIPaletteColor
{
    public string name      = "New Color";
    public Color  color     = Color.white;

    // Named autocolors derived from the base color via proportional HSV mods
    public List<ZUIAutoColor> autoColors = new List<ZUIAutoColor>();
}

/// <summary>
/// A named palette override set. Skins are embedded in ZUIStyleSheetAsset and
/// override palette colors without changing the style structure.
/// </summary>
[Serializable]
public class ZUISkin
{
    public string name = "New Skin";
    public List<ZUIPaletteColor> palette = new List<ZUIPaletteColor>();
    public List<ZUIFontOverride> fontOverrides = new List<ZUIFontOverride>();
}
