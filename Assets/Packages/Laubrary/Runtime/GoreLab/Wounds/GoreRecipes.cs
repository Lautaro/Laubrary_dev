using System;
using System.Collections.Generic;
using System.Reflection;

namespace Laubrary.GoreLab
{
    /// <summary>The list of damage types: the standard set for a new rig, and discovery of every recipe class in the project (including ones a game adds).</summary>
    public static class GoreRecipes
    {
        static List<Type> _discovered;

        /// <summary>One of each standard recipe, in menu order.</summary>
        public static List<IWoundRecipe> CreateDefaultList()
        {
            return new List<IWoundRecipe>
            {
                new SliceRecipe(),
                new CutRecipe(),
                new BulletRecipe(),
                new ShotgunRecipe(),
                new RemoveHeadRecipe(),
            };
        }

        /// <summary>Every concrete, serializable recipe type with a parameterless constructor. Standard recipes first, the rest by name. Cached for the session.</summary>
        public static IReadOnlyList<Type> Discover()
        {
            if (_discovered != null) return _discovered;
            var found = new List<Type>();
#if UNITY_EDITOR
            foreach (var t in UnityEditor.TypeCache.GetTypesDerivedFrom<IWoundRecipe>()) if (IsUsable(t)) found.Add(t);
#else
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch (Exception) { continue; }
                foreach (var t in types) if (t != null && typeof(IWoundRecipe).IsAssignableFrom(t) && IsUsable(t)) found.Add(t);
            }
#endif
            var standard = new List<Type> { typeof(SliceRecipe), typeof(CutRecipe), typeof(BulletRecipe), typeof(ShotgunRecipe), typeof(RemoveHeadRecipe) };
            found.RemoveAll(t => standard.Contains(t));
            found.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            standard.AddRange(found);
            _discovered = standard;
            return _discovered;
        }

        /// <summary>A new instance of a discovered recipe type.</summary>
        public static IWoundRecipe Create(Type type)
        {
            return (IWoundRecipe)Activator.CreateInstance(type);
        }

        static bool IsUsable(Type t)
        {
            return !t.IsAbstract && !t.IsInterface && !t.IsGenericTypeDefinition && t.IsSerializable
                && t.GetConstructor(Type.EmptyTypes) != null;
        }
    }
}
