// ZUIRequireInterface.cs
// For a plain `public Object` field that's really "any asset implementing interface T" (Unity can't serialize
// an interface-typed field directly, and IChunkAnimation/ICombatFx/etc-style interfaces aren't UnityEngine.Object
// themselves) — without this, Unity's default ObjectField accepts ANY asset at all (a texture, a prefab, even a
// .cs script), and a mismatch only surfaces later as a silent null cast. Tag the field and the paired Editor
// drawer (ZUIRequireInterfaceDrawer) constrains both drag-drop and the browse popup to actually-compatible
// assets, via Unity's own interface-aware EditorGUI.ObjectField overload — no custom picker UI needed.
using System;
using UnityEngine;

public class RequireInterfaceAttribute : PropertyAttribute
{
    public readonly Type InterfaceType;
    public RequireInterfaceAttribute(Type interfaceType) => InterfaceType = interfaceType;
}
