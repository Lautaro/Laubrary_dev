// ZUISheet.cs
// Minimal runtime entry point for drawing Zheet (ZUIStyleSheetAsset) styles into
// runtime IMGUI (OnGUI). The style definitions themselves live in the Runtime
// assembly, so a box background can be painted in a player without the editor.

using UnityEngine;

public static class ZUISheet
{
    /// <summary>Draws a box style from a sheet by name into the given rect during OnGUI.
    /// Falls back to the sheet's "Default" box when the name is missing (via FindBox).
    /// Only paints on EventType.Repaint (the underlying DrawBackground handles that).</summary>
    public static void DrawBox(ZUIStyleSheetAsset sheet, string styleName, Rect rect)
    {
        if (sheet == null) return;
        // Wire the ambient sheet so palette / icon-pattern resolution can find it.
        ZUIStyleSheetAsset.Active = sheet;
        var def = sheet.FindBox(styleName);   // sets def.ownerSheet = sheet, falls back to Default
        if (def != null) def.DrawBackground(rect);
    }
}
