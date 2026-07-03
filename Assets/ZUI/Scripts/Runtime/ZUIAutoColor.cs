// ZUIAutoColor.cs
// A named color derived from a base palette color via proportional HSV modifiers.
// Part of the autocolor system — sits alongside ZUIPaletteColor.

using System;
using UnityEngine;

[Serializable]
public class ZUIAutoColor
{
    public string name = "New Color";

    [Range(-1f, 1f)] public float hueMod;   // ±60° hue offset
    [Range(-1f, 1f)] public float satMod;   // proportional saturation shift
    [Range(-1f, 1f)] public float valMod;   // proportional value shift

    /// <summary>Resolves this autocolor against a base color.</summary>
    public Color Resolve(Color baseColor)
    {
        return ZUIColorHSL.ApplyAutoColorMod(baseColor, hueMod, satMod, valMod);
    }
}
