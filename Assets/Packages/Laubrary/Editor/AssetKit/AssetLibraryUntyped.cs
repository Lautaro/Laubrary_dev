using System;
using System.Collections.Generic;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// Non-generic companion to AssetLibrary&lt;T&gt; — for code that only knows the concrete type at runtime
    /// (a picker/field constrained to an interface, resolving across several concrete types it can't name at
    /// compile time). AssetLibrary&lt;T&gt; stays the typed, CRUD-capable entry point for a tool that DOES know
    /// its own T; this is purely read-side enumeration.
    public static class AssetLibraryUntyped
    {
        public static List<Object> Enumerate(Type concreteType, string folder = null)
        {
            var guids = string.IsNullOrEmpty(folder)
                ? AssetDatabase.FindAssets("t:" + concreteType.Name)
                : AssetDatabase.FindAssets("t:" + concreteType.Name, new[] { folder });
            var list = new List<Object>(guids.Length);
            foreach (var g in guids)
            {
                var a = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(g), concreteType);
                if (a != null) list.Add(a);
            }
            return list;
        }

        /// Every non-abstract ScriptableObject type satisfying <paramref name="constraint"/> — an interface, a
        /// base type, or a single concrete type (returned alone). Backed by TypeCache, so it's cheap and
        /// current even for types nothing has explicitly registered anywhere (unlike LauAssetEditors, which
        /// only knows about types that opted into an Open/Create action).
        public static IEnumerable<Type> ConcreteTypesSatisfying(Type constraint)
        {
            if (constraint == null) yield break;
            if (!constraint.IsInterface && !constraint.IsAbstract && typeof(UnityEngine.ScriptableObject).IsAssignableFrom(constraint))
            {
                yield return constraint;
                yield break;
            }
            foreach (var t in TypeCache.GetTypesDerivedFrom(constraint))
                if (!t.IsAbstract && typeof(UnityEngine.ScriptableObject).IsAssignableFrom(t))
                    yield return t;
        }
    }
}
