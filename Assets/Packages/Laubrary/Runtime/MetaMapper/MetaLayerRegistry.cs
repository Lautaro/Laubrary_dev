using System;
using System.Collections.Generic;
using System.Reflection;

namespace Laubrary.MetaMapper
{
    /// <summary>
    /// Declares, from GAME code, a layer id that game code reads: its id, the kind it expects and one line on
    /// what it means. Put it on the class that does the reading, next to the id's own constant:
    /// <code>
    /// [MetaLayerId(Footprint, LayerKind.Shapes, "Floor footprint: the part of a prop bodies collide with.")]
    /// public class ShelfCollision : MonoBehaviour
    /// {
    ///     public const string Footprint = "footprint";
    /// }
    /// </code>
    /// or on the assembly (<c>[assembly: MetaLayerId(...)]</c>) when no single class owns the meaning.
    ///
    /// Layer ids are an API contract between a map and the code reading it, and an attribute is where that
    /// contract can be read without running anything: the editor finds it by reflection, offers the id as a
    /// choice when an author adds a layer, and marks layers that no code has declared. Laubrary itself declares
    /// nothing game-specific — what a layer MEANS is always the game's to say.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct,
        AllowMultiple = true, Inherited = false)]
    public sealed class MetaLayerIdAttribute : Attribute
    {
        public string Id { get; }
        public LayerKind Kind { get; }
        public string Description { get; }

        public MetaLayerIdAttribute(string id, LayerKind kind, string description = "")
        {
            Id = id;
            Kind = kind;
            Description = description ?? "";
        }
    }

    /// <summary>One declared layer id: what game code expects to find under that name.</summary>
    public sealed class MetaLayerDeclaration
    {
        public readonly string id;
        public readonly LayerKind kind;
        public readonly string description;

        /// <summary>Who declared it (the class or assembly name), so a mismatch can say where to look.</summary>
        public readonly string declaredBy;

        /// <summary>True when it came from a <see cref="MetaLayerIdAttribute"/> rather than a Declare call.</summary>
        public readonly bool fromAttribute;

        public MetaLayerDeclaration(string id, LayerKind kind, string description, string declaredBy,
            bool fromAttribute = false)
        {
            this.id = id;
            this.kind = kind;
            this.description = description ?? "";
            this.declaredBy = declaredBy ?? "";
            this.fromAttribute = fromAttribute;
        }
    }

    /// <summary>
    /// THE REGISTRY of layer ids game code reads. A hint for authoring, never a gate: a map may carry any layer
    /// id at all, registered or not, and every query works the same either way. What registration buys is that
    /// the editor can OFFER the right id (a name is typed once, where it is declared, and picked everywhere
    /// else) and can point out a layer nothing reads.
    ///
    /// Two ways in, one list out: <see cref="MetaLayerIdAttribute"/> (preferred — readable without running
    /// game code, so it works in the editor out of Play mode) and <see cref="Declare"/> (for ids only known at
    /// runtime). Ids compare case-insensitively, like every other id in MetaMapper. The first declaration of an
    /// id wins; a second one with a different kind is kept in <see cref="Conflicts"/> rather than silently
    /// overriding, because two readers disagreeing about one id is exactly what should be noticed.
    /// </summary>
    public static class MetaLayerRegistry
    {
        static readonly List<MetaLayerDeclaration> declared = new List<MetaLayerDeclaration>();
        static List<MetaLayerDeclaration> merged;
        static readonly List<MetaLayerDeclaration> conflicts = new List<MetaLayerDeclaration>();

        /// <summary>Declare an id from code. Idempotent for an identical (id, kind); returns false (and records
        /// a conflict) when the id is already declared with a different kind.</summary>
        public static bool Declare(string id, LayerKind kind, string description = "", string declaredBy = "")
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            var existing = Find(declared, id);
            if (existing != null)
            {
                if (existing.kind == kind) return true;
                conflicts.Add(new MetaLayerDeclaration(id.Trim(), kind, description, declaredBy));
                return false;
            }
            declared.Add(new MetaLayerDeclaration(id.Trim(), kind, description, declaredBy));
            merged = null;
            return true;
        }

        /// <summary>Every declared id: code declarations first, then attribute declarations, de-duplicated.</summary>
        public static IReadOnlyList<MetaLayerDeclaration> All
        {
            get
            {
                if (merged == null) Build();
                return merged;
            }
        }

        /// <summary>Declarations of an already-declared id with a DIFFERENT kind — kept, never applied.</summary>
        public static IReadOnlyList<MetaLayerDeclaration> Conflicts
        {
            get
            {
                if (merged == null) Build();
                return conflicts;
            }
        }

        public static bool TryGet(string id, out MetaLayerDeclaration declaration)
        {
            declaration = Find(All, id);
            return declaration != null;
        }

        public static bool IsDeclared(string id) => Find(All, id) != null;

        /// <summary>Forget the attribute scan so the next read rescans. Code declarations are kept. The editor
        /// never needs this (a recompile reloads the domain, which resets everything); it exists for a game
        /// that loads assemblies late.</summary>
        public static void Rescan() => merged = null;

        static MetaLayerDeclaration Find(IReadOnlyList<MetaLayerDeclaration> list, string id)
        {
            if (list == null || string.IsNullOrWhiteSpace(id)) return null;
            string key = id.Trim();
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].id, key, StringComparison.OrdinalIgnoreCase)) return list[i];
            return null;
        }

        static void Build()
        {
            var result = new List<MetaLayerDeclaration>(declared);
            conflicts.RemoveAll(c => c.fromAttribute);

            // Only assemblies that REFERENCE MetaMapper can carry the attribute, which narrows a scan of every
            // loaded assembly to the handful of game assemblies worth walking type by type.
            string self = typeof(MetaLayerRegistry).Assembly.GetName().Name;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                bool isSelf = asm == typeof(MetaLayerRegistry).Assembly;
                if (!isSelf && !References(asm, self)) continue;

                try
                {
                    foreach (var a in asm.GetCustomAttributes<MetaLayerIdAttribute>())
                        Merge(result, a, asm.GetName().Name);
                    foreach (var t in SafeTypes(asm))
                    {
                        if (t == null || !t.IsDefined(typeof(MetaLayerIdAttribute), false)) continue;
                        foreach (var a in t.GetCustomAttributes<MetaLayerIdAttribute>(false))
                            Merge(result, a, t.FullName);
                    }
                }
                catch (Exception) { /* a half-loaded assembly is not worth breaking the editor over */ }
            }
            merged = result;
        }

        static void Merge(List<MetaLayerDeclaration> into, MetaLayerIdAttribute a, string by)
        {
            if (a == null || string.IsNullOrWhiteSpace(a.Id)) return;
            var existing = Find(into, a.Id);
            var decl = new MetaLayerDeclaration(a.Id.Trim(), a.Kind, a.Description, by, fromAttribute: true);
            if (existing == null) into.Add(decl);
            else if (existing.kind != a.Kind) conflicts.Add(decl);
        }

        static bool References(Assembly asm, string name)
        {
            try
            {
                foreach (var r in asm.GetReferencedAssemblies())
                    if (r.Name == name) return true;
            }
            catch (Exception) { }
            return false;
        }

        static Type[] SafeTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types; }
        }
    }
}
