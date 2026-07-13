// ZUIPaddedArea.cs
// Insets a content area from its container's edge on all four sides — e.g. a window pane whose controls
// shouldn't start flush against the window border. Distinct from ZUI.HorizontalSpace()/VerticalSpace(), which
// space controls apart from EACH OTHER; this is the gap between a whole block of content and the OUTER
// boundary it's drawn inside. Sheet-driven (ZUIStyleSheetAsset.contentPadding) so it's one user-adjustable
// value via the Style Editor, not a hardcoded pixel number scattered across every window that wants it.
//
// Usage:
//   using (ZUI.PaddedArea())
//   {
//       // ... the pane's normal content, e.g. EditorGUILayout.BeginVertical()/DrawPreview()/etc ...
//   }

using System;
using UnityEngine;

public class ZUIPaddedAreaScope : IDisposable
{
    readonly float pad;

    internal ZUIPaddedAreaScope(float pad)
    {
        this.pad = pad;
        GUILayout.BeginHorizontal();
        GUILayout.Space(pad);
        GUILayout.BeginVertical();
        GUILayout.Space(pad);
    }

    public void Dispose()
    {
        GUILayout.Space(pad);
        GUILayout.EndVertical();
        GUILayout.Space(pad);
        GUILayout.EndHorizontal();
    }
}

public static partial class ZUI
{
    const float k_FallbackContentPadding = 8f;

    /// <summary>The active sheet's content padding (see ZUIStyleSheetAsset.contentPadding), or a fallback
    /// when no sheet is active.</summary>
    public static float ContentPadding => ActiveSheet?.contentPadding ?? k_FallbackContentPadding;

    /// <summary>Insets whatever's drawn inside the scope by `padding` points on all four sides (defaults to
    /// the active sheet's ContentPadding — a user-adjustable value via the Style Editor, not a fixed number).
    /// </summary>
    public static ZUIPaddedAreaScope PaddedArea(float? padding = null) => new ZUIPaddedAreaScope(padding ?? ContentPadding);
}
