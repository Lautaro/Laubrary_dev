// DotGenClone — a deep copy of one generator definition, subtree modules and all.
//
// Duplicating a generator has to produce something that shares NOTHING with the original: two entries that
// quietly point at one mutator list look right until the first edit changes both. Unity's own JsonUtility
// cannot do it, because a generator's module lists are [SerializeReference] polymorphic and JsonUtility
// serializes them by their declared (abstract) type, so a round trip loses every concrete module.
//
// So the copy is made by reflection over the live object graph, which reads each value's REAL type and
// therefore keeps polymorphism intact. Two things are deliberately shared rather than copied: a
// UnityEngine.Object reference (a texture a fill points at is one asset, not a new one), and any type this
// cannot construct — in which case the reference is passed through rather than the whole duplicate failing.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Laubrary.DotGen.Editor
{
    public static class DotGenClone
    {
        /// A generator with the same settings and its own copies of every module. Ids are copied verbatim —
        /// the caller mints fresh ones, because only the document owns the id counter.
        public static DotGenerator CloneGenerator(DotGenerator src)
            => src == null ? null : (DotGenerator)CloneValue(src);

        static object CloneValue(object src)
        {
            if (src == null) return null;
            var t = src.GetType();

            if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)) return src;

            // An asset reference is a reference to ONE asset; copying it would be copying the wrong thing.
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return src;

            if (t == typeof(AnimationCurve))
            {
                var c = (AnimationCurve)src;
                return new AnimationCurve(c.keys) { preWrapMode = c.preWrapMode, postWrapMode = c.postWrapMode };
            }

            if (t == typeof(Gradient))
            {
                var g = (Gradient)src;
                var n = new Gradient { mode = g.mode };
                n.SetKeys(g.colorKeys, g.alphaKeys);
                return n;
            }

            if (t.IsArray)
            {
                var srcArray = (Array)src;
                var elem = t.GetElementType();
                var copyArray = Array.CreateInstance(elem, srcArray.Length);
                for (int i = 0; i < srcArray.Length; i++) copyArray.SetValue(CloneValue(srcArray.GetValue(i)), i);
                return copyArray;
            }

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var copyList = (IList)Activator.CreateInstance(t);
                foreach (var item in (IList)src) copyList.Add(CloneValue(item));
                return copyList;
            }

            object copy;
            try { copy = Activator.CreateInstance(t, nonPublic: true); }
            catch { return src; }   // nothing this can build — share it rather than lose the duplicate

            CopyFields(src, copy, t);
            return copy;
        }

        static void CopyFields(object src, object dst, Type t)
        {
            // Walk the whole chain: a base class's private fields are invisible to a single GetFields call.
            for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
            {
                var fields = cur.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                           | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    var f = fields[i];
                    if (f.IsNotSerialized) continue;
                    try { f.SetValue(dst, CloneValue(f.GetValue(src))); }
                    catch { /* a field this cannot write is left at its default rather than aborting the copy */ }
                }
            }
        }
    }
}
