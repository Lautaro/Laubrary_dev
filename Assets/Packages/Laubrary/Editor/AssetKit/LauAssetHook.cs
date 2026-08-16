using System;
using System.Collections.Generic;
using Laubrary.PreviewKit;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// <summary>
    /// Teaches ZUI what a Laubrary asset REFERENCE is, so every generic object field in the toolkit draws one as
    /// a reference chip instead of an object field — a reflected modifier field, a serialized ObjectReference,
    /// anywhere. Installing one delegate here is what makes the rule true library-wide rather than true only
    /// where someone remembered to call <see cref="LauAssetElement"/> by hand.
    ///
    /// <para><b>What counts.</b> A ScriptableObject that either lives in a <c>Laubrary.*</c> namespace, renders
    /// its own preview, or has an editor registered — i.e. a data asset this library authors and browses. Unity's
    /// own asset types keep the plain object field: a Sprite, a Texture, an AudioClip, a font, a prefab are
    /// picked from Unity's project browser, and a chip would be pretending to own them. A SCENE object reference
    /// is never claimed either — there is nothing to browse.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class LauAssetHook
    {
        // Thumbnails for chips built through the hook, which has no window to own a cache for it. Cleared on
        // domain reload so the textures this created cannot outlive the assembly that made them.
        static readonly Dictionary<Object, Texture2D> Thumbs = new Dictionary<Object, Texture2D>();

        static LauAssetHook()
        {
            ZuiAssetHook.Build = TryBuild;
            AssemblyReloadEvents.beforeAssemblyReload += () => LauAssetGridGUI.ClearCache(Thumbs);
        }

        static VisualElement TryBuild(Type objectType, Object current, Action<Object> onChanged, string tooltip)
        {
            if (!IsAssetReference(objectType)) return null;
            return LauAssetElement.Build(current, onChanged, objectType, Thumbs,
                                         "New " + objectType.Name, DefaultFolderFor(objectType),
                                         tooltip ?? ObjectNames.NicifyVariableName(objectType.Name));
        }

        /// <summary>Is a reference to this type one of OURS — something browsed, previewed and created through
        /// AssetKit — rather than a plain Unity asset picked from the project browser?</summary>
        public static bool IsAssetReference(Type t)
        {
            if (t == null || !typeof(ScriptableObject).IsAssignableFrom(t)) return false;
            if (typeof(IVisualPreview).IsAssignableFrom(t)) return true;
            if (LauAssetEditors.HasRegistration(t)) return true;
            return t.Namespace != null && t.Namespace.StartsWith("Laubrary", StringComparison.Ordinal);
        }

        /// Where a brand-new asset of this type goes when the field itself has no opinion: a folder named after
        /// the type, under Assets, so a first-ever asset lands somewhere predictable instead of the project root.
        static string DefaultFolderFor(Type t) => "Assets/" + ObjectNames.NicifyVariableName(t.Name).Replace(" ", "");
    }
}
