using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Laubrary.Rulesets
{
    // The single source of truth for "which rule parameters are exposed and tunable". Used by the Rules editor
    // (to render them) and by Story's PlotTwist page picker (to constrain what it can target) — so a visual
    // editor can never target a parameter the rules don't actually expose.
    //
    // A parameter is a public, serialized field that is a scalar (float/int/bool/string/enum) OR a wrapper that
    // exposes a public 'float staticValue { get; set; }' (e.g. ZUIValue). The staticValue duck-typing keeps
    // Rulesets free of a ZUI dependency while still exposing ZUIValue tunables.
    public static class RuleParams
    {
        const BindingFlags FieldFlags = BindingFlags.Public | BindingFlags.Instance;
        static Type[] _ruleTypes;

        // Every concrete GameRule subclass across all loaded assemblies (concrete rules live in the game assembly).
        public static Type[] AllRuleTypes()
        {
            if (_ruleTypes != null) return _ruleTypes;
            var result = new List<Type>();
            var seen = new HashSet<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                if (types == null) continue;
                foreach (var t in types)
                    if (t != null && t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(GameRule)) && seen.Add(t))
                        result.Add(t);
            }
            _ruleTypes = result.OrderBy(t => t.Name).ToArray();
            return _ruleTypes;
        }

        public static string[] AllRuleTypeNames() => AllRuleTypes().Select(t => t.Name).ToArray();

        public static Type TypeByName(string name) =>
            string.IsNullOrEmpty(name) ? null : AllRuleTypes().FirstOrDefault(t => t.Name == name);

        // The exposed, targetable parameter fields of a rule type (declared + inherited).
        public static IEnumerable<FieldInfo> ParamFields(Type ruleType)
        {
            if (ruleType == null) yield break;
            var chain = new List<Type>();
            for (Type cur = ruleType; cur != null && cur != typeof(object); cur = cur.BaseType) chain.Add(cur);
            chain.Reverse();
            foreach (var ct in chain)
                foreach (var f in ct.GetFields(FieldFlags | BindingFlags.DeclaredOnly))
                {
                    if (f.IsNotSerialized) continue;
                    if (Attribute.IsDefined(f, typeof(HideInInspector))) continue;
                    if (IsScalar(f.FieldType) || StaticValueProp(f.FieldType) != null) yield return f;
                }
        }

        public static string[] ParamNames(string ruleTypeName) =>
            ParamFields(TypeByName(ruleTypeName)).Select(f => f.Name).ToArray();

        public static bool IsScalar(Type t) =>
            t == typeof(float) || t == typeof(int) || t == typeof(bool) || t == typeof(string) || t.IsEnum;

        // The public 'float staticValue { get; set; }' of a wrapper type (e.g. ZUIValue), or null if it has none.
        public static PropertyInfo StaticValueProp(Type t)
        {
            if (t == null || t.IsPrimitive || t == typeof(string) || t.IsEnum) return null;
            var p = t.GetProperty("staticValue", BindingFlags.Public | BindingFlags.Instance);
            return (p != null && p.PropertyType == typeof(float) && p.CanRead && p.CanWrite) ? p : null;
        }
    }
}
