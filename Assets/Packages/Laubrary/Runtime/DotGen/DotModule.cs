// DotModule.cs
// The four module categories and the registry that finds them.
//
// A module type owns its own fields, its own defaults (plain C# field initializers — so a fresh
// Activator.CreateInstance IS the registered default, which is what a double-click slider reset resolves
// against) and its own evaluation. Nothing outside a module knows what dials it has: the window reflects over
// the concrete instance, and this registry is the only place that enumerates the types. Adding a placement or
// a mutator is therefore one new class with one attribute — no switch anywhere else grows a case.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Laubrary.DotGen
{
    /// Tags a concrete module type so the registry can find it, name it in a menu, and name new instances.
    /// `Order` fixes the menu order: reflection's type order is not specified, and the menus must not shuffle
    /// between editor sessions.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DotModuleAttribute : Attribute
    {
        /// Stable serialized identifier. Never change one — it is what an authored asset stores.
        public string TypeId { get; }

        /// The name shown in the add menu and as a card's category label.
        public string DisplayName { get; }

        /// The name a freshly added instance is given (the user renames it from there).
        public string DefaultName { get; }

        /// Display order within the category.
        public int Order { get; set; }

        public DotModuleAttribute(string typeId, string displayName, string defaultName = null)
        {
            TypeId = typeId;
            DisplayName = displayName;
            DefaultName = defaultName ?? displayName;
        }
    }

    /// Everything common to a placement, selector, mutator and drawer: an id (references point at ids, never
    /// list positions), an author-facing name and an enable flag.
    [Serializable]
    public abstract class DotModule
    {
        public string id = "";
        public string name = "";
        public bool enabled = true;

        /// The registry entry for this instance's concrete type.
        public DotModuleAttribute Meta => DotModuleRegistry.MetaOf(GetType());
    }

    /// Produces the dots (and optionally the cells) of one generator area. Exactly one per generator is active.
    [Serializable]
    public abstract class DotPlacement : DotModule
    {
        /// True when this placement gives every dot cell metadata — which is what makes "Placement cell"
        /// available as a child's area basis and what a Fill drawer's cell targets need.
        public virtual bool ProvidesCells => true;

        /// Emit local dots (-0.5..0.5 in the area's own space) into `outDots`.
        public abstract void Evaluate(DotGenerator gen, in DotArea area, int instIdx, int globalSeed, List<DotLocal> outDots);
    }

    /// A reusable scalar field over an area's dots. It never changes anything by itself: a consumer decides
    /// what a weight means to it.
    [Serializable]
    public abstract class DotSelector : DotModule
    {
        public abstract float Weight(DotGenerator gen, in DotArea area, float worldX, float worldY, int pointIndex, int globalSeed);
    }

    /// Moves or removes dots, in list order. Writes a trace so a gizmo can show what it did without re-running
    /// the pipeline.
    [Serializable]
    public abstract class DotMutator : DotModule
    {
        /// The selector this mutator listens to; "" means All dots (a constant weight of 1). Hidden from the
        /// reflected body on purpose: a reference is a picker over declared names, never a typed string, so the
        /// card draws it itself.
        [HideInInspector] public string selectorId = "";

        /// Mutate `pts` in place. Anything deleted goes into `removed` so the trace can draw it.
        public abstract void Apply(DotGenerator gen, in DotArea area, List<DotPoint> pts, int instIdx, int globalSeed, DotSelector sel, List<DotPoint> removed);
    }

    /// Paints geometry after evaluation. A drawer never moves a dot and never affects child instantiation.
    [Serializable]
    public abstract class DotDrawer : DotModule
    {
        /// The selector this drawer filters its targets through; "" means All dots. Drawn as a picker by the
        /// card, so it is kept out of the reflected body (see DotMutator.selectorId).
        [HideInInspector] public string selectorId = "";

        /// The shapes this drawer wants painted, in stable evaluation order.
        public abstract void Targets(DotGenerator gen, DotGenGeneratorData gd, int globalSeed, List<DotDrawTarget> outTargets);

        /// The paint for one target. `fillKey` is the target's deterministic key.
        public abstract ZuiFill FillFor(int fillKey, int globalSeed);

        /// 0..1 opacity applied to the whole target.
        public abstract float OpacityFraction { get; }
    }

    /// One shape a drawer asked to be painted.
    public struct DotDrawTarget
    {
        public DotArea area;
        public DotShape shape;
        public int fillKey;
    }

    /// The type catalogue. Reflection runs once per domain load over this assembly only — modules live here by
    /// construction, and a project-wide scan would cost far more for nothing.
    public static class DotModuleRegistry
    {
        public struct Entry
        {
            public Type type;
            public string typeId;
            public string displayName;
            public string defaultName;
            public int order;
        }

        static List<Entry> _placements, _selectors, _mutators, _drawers;
        static Dictionary<Type, DotModuleAttribute> _meta;

        public static IReadOnlyList<Entry> Placements { get { Build(); return _placements; } }
        public static IReadOnlyList<Entry> Selectors { get { Build(); return _selectors; } }
        public static IReadOnlyList<Entry> Mutators { get { Build(); return _mutators; } }
        public static IReadOnlyList<Entry> Drawers { get { Build(); return _drawers; } }

        public static DotModuleAttribute MetaOf(Type t)
        {
            Build();
            return _meta.TryGetValue(t, out var m) ? m : null;
        }

        static void Build()
        {
            if (_placements != null) return;

            _placements = new List<Entry>();
            _selectors = new List<Entry>();
            _mutators = new List<Entry>();
            _drawers = new List<Entry>();
            _meta = new Dictionary<Type, DotModuleAttribute>();

            foreach (var t in typeof(DotModule).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(DotModule).IsAssignableFrom(t)) continue;
                var attr = t.GetCustomAttribute<DotModuleAttribute>(false);
                if (attr == null) continue;

                _meta[t] = attr;
                var e = new Entry
                {
                    type = t,
                    typeId = attr.TypeId,
                    displayName = attr.DisplayName,
                    defaultName = attr.DefaultName,
                    order = attr.Order
                };

                if (typeof(DotPlacement).IsAssignableFrom(t)) _placements.Add(e);
                else if (typeof(DotSelector).IsAssignableFrom(t)) _selectors.Add(e);
                else if (typeof(DotMutator).IsAssignableFrom(t)) _mutators.Add(e);
                else if (typeof(DotDrawer).IsAssignableFrom(t)) _drawers.Add(e);
            }

            _placements.Sort(ByOrder);
            _selectors.Sort(ByOrder);
            _mutators.Sort(ByOrder);
            _drawers.Sort(ByOrder);
        }

        static int ByOrder(Entry a, Entry b)
        {
            int c = a.order.CompareTo(b.order);
            return c != 0 ? c : string.CompareOrdinal(a.typeId, b.typeId);
        }

        public static IReadOnlyList<Entry> For<T>() where T : DotModule
        {
            if (typeof(DotPlacement).IsAssignableFrom(typeof(T))) return Placements;
            if (typeof(DotSelector).IsAssignableFrom(typeof(T))) return Selectors;
            if (typeof(DotMutator).IsAssignableFrom(typeof(T))) return Mutators;
            return Drawers;
        }

        public static bool TryFind(IReadOnlyList<Entry> list, string typeId, out Entry entry)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].typeId == typeId) { entry = list[i]; return true; }
            entry = default;
            return false;
        }

        /// A brand-new instance of a registered type, carrying its type defaults and its default name. The id
        /// is left blank — the document assigns it, because only the document owns the id counter.
        public static DotModule Create(Entry e)
        {
            var m = (DotModule)Activator.CreateInstance(e.type);
            m.name = e.defaultName;
            return m;
        }

        public static DotModule Create(IReadOnlyList<Entry> list, string typeId)
            => TryFind(list, typeId, out var e) ? Create(e) : null;
    }
}
