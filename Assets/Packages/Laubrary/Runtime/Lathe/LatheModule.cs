// LatheModule — the plug-in solid-generation model for a Lathe solid (see LatheSolid.module).
//
// A module is self-contained: its dials are its own public fields ([Range]/[Tooltip] drive the editor
// through ZuiReflect — a module needs zero editor code), it is discovered by assembly scan (LatheWindow's
// module picker), and Generate is a PURE function of this instance's own serialized fields. This is the
// same pattern Pyre's PyreForm uses for its shape plug-ins, applied to solid geometry instead of a
// per-pixel raster.
//
// UNLIKE PyreForm, Generate takes no frame/time input — a Lathe solid is static geometry for v1. Animating
// a module's own dials over the turntable timeline (LatheSpec.turntableFrames today only orbits the camera
// and the whole assembly, never the generated mesh itself) is a real next step, not built yet.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Laubrary.Lathe
{
    /// Catalog metadata for the editor's module picker. `group` clusters modules under one heading.
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class LatheModuleInfoAttribute : Attribute
    {
        public string DisplayName { get; }
        public string Group { get; }
        public string Icon { get; }
        public LatheModuleInfoAttribute(string displayName, string group = "Solids", string icon = null)
        {
            DisplayName = displayName;
            Group = group;
            Icon = icon;
        }
    }

    [Serializable]
    public abstract class LatheModule
    {
        /// Label in the picker and on the module's card. Usually just the [LatheModuleInfo] display name.
        public abstract string DisplayName { get; }

        /// What the module's card tooltip says: what it builds and how its dials shape it.
        public virtual string Description => DisplayName + " solid.";

        /// Build this solid's LOCAL-space geometry into `data`. Called fresh on every preview repaint —
        /// cheap at the vertex counts these generators produce, so the caller never caches the result.
        public abstract void Generate(LatheMeshData data);

        /// Deep copy for "Duplicate solid" — MemberwiseClone shares list REFERENCES (profile points, path
        /// waypoints…), so every List<T> field gets its own fresh copy or editing a duplicate would
        /// silently mutate the original too.
        public virtual LatheModule Clone()
        {
            var c = (LatheModule)MemberwiseClone();
            LatheReflectionUtil.CloneListFields(this, c);
            return c;
        }
    }

    /// Shared deep-copy helper for LatheModule/LatheMeshModifier subclasses.
    static class LatheReflectionUtil
    {
        public static void CloneListFields(object from, object to)
        {
            foreach (var f in from.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!f.FieldType.IsGenericType || f.FieldType.GetGenericTypeDefinition() != typeof(List<>)) continue;
                if (f.GetValue(from) is not IList src) continue;
                var copy = (IList)Activator.CreateInstance(f.FieldType);
                foreach (var e in src) copy.Add(e);
                f.SetValue(to, copy);
            }
        }
    }
}
