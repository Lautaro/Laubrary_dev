// ZuiSwatchRef.cs
// A colour that is EITHER a plain inline colour OR a reference to a named swatch in a SwatchPalette.
// The content-colour counterpart to ZUIColorRef (which resolves against the editor theme sheet): this
// one holds a DIRECT reference to the palette asset, so it resolves the same in the editor and in a
// build with no ambient state. Change the swatch → Resolve() returns the new colour on the next read.
//
// Resolution is cheap: it caches the resolved INDEX (name lookup once) and only re-finds it when the
// palette's Version has moved — so steady-state is an array index, and a runtime swatch change costs
// nothing to propagate. No palette / no key → the inline colour (so "just use a plain colour" is the
// default and costs nothing).

using System;
using UnityEngine;

[Serializable]
public struct ZuiSwatchRef
{
    public SwatchPalette palette;      // null → inline colour
    public string        key;          // swatch name; "" → inline colour
    public string        autoColorRef; // named autocolor variant within the swatch; "" → base colour
    public Color         inline;       // the plain (non-swatch) colour, and the fallback

    // Runtime resolve cache — never serialised.
    [NonSerialized] int  _cachedVersion;
    [NonSerialized] int  _cachedIndex;
    [NonSerialized] bool _cached;

    public ZuiSwatchRef(Color inline)
    {
        this.inline = inline;
        palette = null; key = ""; autoColorRef = "";
        _cachedVersion = 0; _cachedIndex = -1; _cached = false;
    }

    public ZuiSwatchRef(SwatchPalette palette, string key, Color inline, string autoColorRef = "")
    {
        this.palette = palette; this.key = key ?? ""; this.autoColorRef = autoColorRef ?? ""; this.inline = inline;
        _cachedVersion = 0; _cachedIndex = -1; _cached = false;
    }

    /// <summary>True when this colour is backed by a swatch (a palette + a key).</summary>
    public bool IsSwatch => palette != null && !string.IsNullOrEmpty(key);

    /// <summary>The resolved colour: the swatch (with optional autocolor) if backed by one and found,
    /// otherwise the inline colour. Cheap on repeat calls — re-finds the swatch index only when the
    /// palette version has changed.</summary>
    public Color Resolve()
    {
        if (palette == null || string.IsNullOrEmpty(key)) return inline;
        if (!_cached || _cachedVersion != palette.Version)
        {
            _cachedIndex   = palette.IndexOf(key);
            _cachedVersion = palette.Version;
            _cached        = true;
        }
        return _cachedIndex < 0 ? inline : palette.Resolve(_cachedIndex, autoColorRef, inline);
    }

    /// <summary>Implicitly usable where a Color is expected.</summary>
    public static implicit operator Color(ZuiSwatchRef r) => r.Resolve();

    public override string ToString()
        => IsSwatch ? (string.IsNullOrEmpty(autoColorRef) ? $"[{key}]" : $"[{key}:{autoColorRef}]")
                    : inline.ToString();
}
