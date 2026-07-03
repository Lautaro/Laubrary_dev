// ZUIColorRef.cs
// A color value with optional palette + autocolor reference.

using System;
using UnityEngine;

[Serializable]
public struct ZUIColorRef
{
    public Color          color;
    public string         paletteRef;      // palette entry name; "" = use inline color
    public ZUIPaletteSlot slot;            // vestigial — kept for serialization compat, always Primary
    public string         autoColorRef;    // autocolor name within the palette entry; "" = use base color

    public ZUIColorRef(Color color, string paletteRef = "", ZUIPaletteSlot slot = ZUIPaletteSlot.Primary, string autoColorRef = "")
    {
        this.color        = color;
        this.paletteRef   = paletteRef ?? "";
        this.slot         = slot;
        this.autoColorRef = autoColorRef ?? "";
    }

    /// <summary>Resolves the color against the given sheet's palette: autocolor if set,
    /// otherwise base palette color, otherwise the inline color.
    /// <para>Pass the sheet that owns the def this ref lives on. Passing null falls back
    /// to the inline color (never reads ambient state).</para></summary>
    public Color Resolve(ZUIStyleSheetAsset sheet)
    {
        if (sheet != null && !string.IsNullOrEmpty(paletteRef))
        {
            var p = sheet.FindPaletteColor(paletteRef);
            if (p != null)
            {
                string acRef = autoColorRef;
                if (!string.IsNullOrEmpty(acRef) && p.autoColors != null)
                {
                    var ac = p.autoColors.Find(a => a.name == acRef);
                    if (ac != null) return ac.Resolve(p.color);
                }
                return p.color;
            }
        }
        return color;
    }

    /// <summary>True when this color is backed by a palette reference.</summary>
    public bool IsPaletteRef => !string.IsNullOrEmpty(paletteRef);

    /// <summary>True when this references a named autocolor (not just the base).</summary>
    public bool IsAutoColorRef => IsPaletteRef && !string.IsNullOrEmpty(autoColorRef);

    /// <summary>Creates a ZUIColorRef from legacy separate fields (slot is vestigial, kept for deserialization).</summary>
    public static ZUIColorRef FromLegacy(Color color, string colorRef, ZUIPaletteSlot colorSlot)
        => new ZUIColorRef(color, colorRef ?? "", colorSlot);

    public override string ToString()
        => IsAutoColorRef ? $"[{paletteRef}:{autoColorRef}]"
         : IsPaletteRef   ? $"[{paletteRef}]"
         : color.ToString();
}
