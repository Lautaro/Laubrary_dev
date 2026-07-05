// ZUIMultiToggle.cs
// MultiToggle — a space-saving group of related toggles behind one "super" toggle:
//
//   [x] SuperToggle
//        [x] Option A
//        [ ] Option B
//        [x] Option C
//
// The super toggle is always visible; the sub-toggles live in a FoldControls body (so the
// group expands by an arrow-click or on hover, chosen from the header's right-click menu).
// When the super toggle is OFF the sub-toggles are shown disabled but KEEP their values, so
// flipping the super back on restores exactly what was there.
//
// Usage:
//   superOn = ZUI.MultiToggle("fx", superOn, "Post FX", subLabels, subValues);
// subValues[] is mutated in place; the new super value is returned.

using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    public static bool MultiToggle(string key, bool superOn, string superLabel,
                                   string[] subLabels, bool[] subValues,
                                   string style = Style.Default)
    {
        if (subLabels == null || subValues == null) return superOn;
        bool newSuper = superOn;

        FoldControls(key,
            alwaysVisible: () =>
            {
                newSuper = Toggle(superOn, superLabel, style);
            },
            expandable: () =>
            {
                // Sub-toggles keep their stored values even while the super is off; they just can't be
                // edited (drawn dimmed and non-interactive) until the super is on again.
                using (new EditorGUI.DisabledScope(!superOn))
                {
                    int n = Mathf.Min(subLabels.Length, subValues.Length);
                    for (int i = 0; i < n; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(18f);
                        subValues[i] = Toggle(subValues[i], subLabels[i], style);
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();
                    }
                }
            },
            defaultMode: FoldMode.Arrow);

        return newSuper;
    }
}
