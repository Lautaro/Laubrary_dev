// ZUICore.cs
// Runtime-side core helpers extracted from the editor-only ZUI static so that style
// definitions (ZUIBoxDef / ZUIButtonDef) can resolve corner masks and, in the editor,
// call back into editor-only flash/debug features — all without the Runtime assembly
// referencing ZUI.Editor. In player builds the hooks are never registered (no-op).

using System;
using UnityEngine;

// ── ZUICornerMask — per-call corner rounding override ───────────────────────────
// Top-level enum so call sites can use it without a ZUI. qualifier.
// None = use the def's own roundTL/TR/BL/BR settings (no override).
public enum ZUICornerMask
{
    None,    // no override — use the def's roundTL/TR/BL/BR
    All,     // all four corners
    Left,    // TL + BL
    Right,   // TR + BR
    Top,     // TL + TR
    Bottom,  // BL + BR
    Square,  // all corners square (no rounding)
}

public static class ZUICore
{
    // Translates a ZUICornerMask into (roundTL, roundTR, roundBL, roundBR).
    // When mask is None, falls back to the def's own per-corner flags.
    // Single implementation — the editor ZUI.ResolveCornerMask forwards here.
    public static (bool tl, bool tr, bool bl, bool br) ResolveCornerMask(ZUIButtonDef def, ZUICornerMask mask)
    {
        switch (mask)
        {
            case ZUICornerMask.All:    return (true,  true,  true,  true);
            case ZUICornerMask.Left:   return (true,  false, true,  false);
            case ZUICornerMask.Right:  return (false, true,  false, true);
            case ZUICornerMask.Top:    return (true,  true,  false, false);
            case ZUICornerMask.Bottom: return (false, false, true,  true);
            case ZUICornerMask.Square: return (false, false, false, false);
            default:                   return def.GetResolvedCornerFlags();
        }
    }

    // ── Editor-only draw hooks ─────────────────────────────────────────────────
    // Registered by the editor ZUI static (see ZUIDebug.cs RegisterCoreHooks) so that
    // moved defs can invoke editor flash/debug niceties without a hard reference to the
    // ZUI.Editor assembly. These stay null in player builds, so the call sites are no-ops.
    public static Action<ZUIBoxDef, string, Rect> BoxDebugHook;   // (def, name, rect)
    public static Action<Rect, string, int>       BoxFlashHook;   // (rect, name, cornerRadius)
}
