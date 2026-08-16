// ZuiAssetHook — the seam that lets a REFERENCE to a project asset render as a reference chip everywhere,
// including in the two places ZUI draws object fields generically and therefore knows nothing about what it
// is drawing: ZuiReflect (a reflected field whose type is only known at runtime) and ZuiSerialized (a
// SerializedProperty of type ObjectReference).
//
// Those two are where a wrong control choice is SYSTEMIC — every modifier, every serialized object, every
// tool at once — so they are exactly where the fix belongs, per the standing "fix the wrapper, not the call
// sites" rule. But ZUI cannot reference the AssetKit editor assembly (AssetKit references ZUI, not the other
// way round), so the module that knows what a Laubrary asset IS installs itself here at load, and ZUI simply
// asks. Nothing installed, or a type the installer does not claim, falls back to a plain ObjectField — which
// is still the right control for a Sprite, a Texture or a font.
using System;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public static class ZuiAssetHook
    {
        /// <summary>Return a control for a reference to <paramref name="objectType"/>, or null to decline (the
        /// caller then draws a plain ObjectField).</summary>
        public delegate VisualElement Factory(Type objectType, UnityEngine.Object current,
                                              Action<UnityEngine.Object> onChanged, string tooltip);

        /// Installed once, by whichever module owns asset references (Laubrary's AssetKit does it from an
        /// [InitializeOnLoad]). Left null in a project that has no such module.
        public static Factory Build;

        /// The control for this reference, or null when nothing claims it.
        public static VisualElement TryBuild(Type objectType, UnityEngine.Object current,
                                            Action<UnityEngine.Object> onChanged, string tooltip) =>
            Build?.Invoke(objectType, current, onChanged, tooltip);
    }
}
