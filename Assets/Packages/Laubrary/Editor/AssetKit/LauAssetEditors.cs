using System;
using System.Collections.Generic;
using System.Linq;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// The one registry, keyed by concrete asset type, for the two things any tool needs to plug a LauAsset
    /// type into a shared picker/field without that field's own module needing a hard reference to the type's
    /// authoring tool: "open THIS existing asset in its own editor" and "create a NEW one of this type".
    /// A tool registers both (or just Open, if it has no lightweight inline-create story yet) via
    /// [InitializeOnLoad], the same decoupling shape Chunks used for its own animation-source "Edit" button
    /// before this generalized it — Pyre/Launimator/BackSplash/Mirage register into this directly now instead
    /// of a Chunks-owned registry, since it's no longer Chunks-specific.
    public static class LauAssetEditors
    {
        static readonly Dictionary<Type, Action<Object>> openers = new Dictionary<Type, Action<Object>>();
        static readonly Dictionary<Type, Func<string, string, Object>> creators = new Dictionary<Type, Func<string, string, Object>>();

        /// Called by a tool's own Editor assembly (e.g. via [InitializeOnLoad]) to wire up its "Edit" action.
        public static void RegisterOpen<T>(Action<T> opener) where T : Object
            => openers[typeof(T)] = obj => opener((T)obj);

        /// Called by a tool's own Editor assembly to wire up its "New" action — given a suggested name and
        /// folder, create and return a fresh asset of this type (any side effect, like opening the type's own
        /// authoring tool on the new asset, is the caller's job via Open right after, not this delegate's).
        public static void RegisterCreate<T>(Func<string, string, T> creator) where T : Object
            => creators[typeof(T)] = (name, folder) => creator(name, folder);

        public static bool CanOpen(Object asset) => asset != null && openers.ContainsKey(asset.GetType());

        public static void Open(Object asset)
        {
            if (asset != null && openers.TryGetValue(asset.GetType(), out var fn)) fn(asset);
        }

        public static bool CanCreate(Type concreteType) => concreteType != null && creators.ContainsKey(concreteType);

        public static Object Create(Type concreteType, string suggestedName, string folder)
            => concreteType != null && creators.TryGetValue(concreteType, out var fn) ? fn(suggestedName, folder) : null;

        /// Every concrete type with EITHER an opener or a creator registered, that also satisfies
        /// <paramref name="constraint"/> (an interface or base type) — the set a picker/field constrained to
        /// that contract should browse/offer-to-create across.
        public static IEnumerable<Type> RegisteredTypesFor(Type constraint)
        {
            var all = new HashSet<Type>(openers.Keys);
            all.UnionWith(creators.Keys);
            return all.Where(t => constraint == null || constraint.IsAssignableFrom(t));
        }
    }
}
