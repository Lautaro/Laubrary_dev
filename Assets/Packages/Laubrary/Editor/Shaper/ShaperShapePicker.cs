// ShaperShapePicker — ONE picker for everything a node's shape can be (T-0182).
//
// Before this file the window asked the author two separate questions in two separate places: a "Kind"
// radio (Primitive/Bag/Composite/Solid) plus a "Primitive" radio in the Shape section, and a wholly
// separate "Generator" section with its own menu. Those are not two decisions. They are one decision —
// "what does this node draw?" — split across two cards, which is why the owner could not tell what the
// difference between them was: there is none. A node has exactly one shape source, so there is exactly
// one control that chooses it.
//
// ── Why a registry rather than a switch ──────────────────────────────────────────────────────────────
// The choices come from four independent places and one of them is not written yet: the primitive enum,
// the solid-form enum, the bag, every concrete PyreForm (named by its own PyreFormInfoAttribute), and
// every non-form composite source that declares itself pickable (ShaperCompositeSourceInfoAttribute —
// the Fire and Fireball simulations today). A hand-written switch would have to be edited every time any
// of those grows, which is the failure mode both attributes exist to avoid. So the catalog is a list of
// PROVIDERS: each yields entries tagged with a category, and a new family joins the picker by
// registering one, never by editing this file. Category order is registration order, so an extension
// registered from an [InitializeOnLoadMethod] appends its column after the built-ins rather than
// interleaving unpredictably.
//
// ── Why an entry carries a mutation and not a kind ───────────────────────────────────────────────────
// Picking a shape has to set several fields at once (node kind AND the primitive kind / solid form /
// composite source), and it must do so inside ONE ShaperWindow.Change so a single Ctrl+Z puts the node
// back exactly as it was. An entry therefore hands the window a pure mutation to run; the entry never
// touches Undo itself, because the window owns that contract.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Laubrary.Pyre;
using Laubrary.PyreShaper;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    /// One thing a node's shape can BE, as the picker shows it.
    public struct ShaperShapeEntry
    {
        /// The picker column this entry sits in. Entries sharing a category share a column.
        public string Category;
        /// What the picker calls it, and what the Shape header reads back afterwards.
        public string Label;
        /// A ZUI icon name, or null. An unresolved name draws nothing rather than an empty box.
        public string Icon;
        /// Written as the effect of picking it, never as a restatement of the label.
        public string Tooltip;
        /// Whether the node is currently this entry — drives the checkmark.
        public Func<ShaperNode, bool> IsCurrent;
        /// The mutation that makes the node this entry. Pure: the window wraps it in one Change().
        public Action<ShaperNode> Apply;
    }

    /// <summary>
    /// Everything the shape picker offers, assembled from registered providers.
    ///
    /// <b>Extension point.</b> A tool that adds a new family of shape sources registers a provider —
    /// <c>ShaperShapeCatalog.Register(() => …)</c> from an <c>[InitializeOnLoadMethod]</c> — and its
    /// entries get their own picker column with no edit to the window. Providers are enumerated in
    /// registration order and the built-ins register first, so an added category lands after them.
    /// </summary>
    public static class ShaperShapeCatalog
    {
        static readonly List<Func<IEnumerable<ShaperShapeEntry>>> Providers =
            new List<Func<IEnumerable<ShaperShapeEntry>>>();

        static ShaperShapeCatalog()
        {
            Register(Primitives);
            Register(Solids);
            Register(BagEntry);
            Register(Generators);
        }

        // Built once per domain reload. The providers read the DOMAIN (TypeCache), which cannot change
        // without a reload, and static state is discarded by one — so the cache can never go stale, while
        // the header suffix that names the current shape stops re-scanning every type on every repaint.
        static List<ShaperShapeEntry> cache;

        /// Add a family of shape choices. Registration order is column order.
        public static void Register(Func<IEnumerable<ShaperShapeEntry>> provider)
        {
            if (provider == null || Providers.Contains(provider)) return;
            Providers.Add(provider);
            cache = null;
        }

        /// Every entry, in provider order.
        public static List<ShaperShapeEntry> All()
        {
            if (cache != null) return cache;
            var built = new List<ShaperShapeEntry>();
            foreach (var p in Providers)
            {
                IEnumerable<ShaperShapeEntry> got;
                // A provider that throws must cost its own column, never the whole picker — a broken
                // third-party family would otherwise take Rect and Ellipse down with it.
                try { got = p(); if (got != null) built.AddRange(got); }
                catch (Exception e) { Debug.LogException(e); }
            }
            return cache = built;
        }

        /// The entries grouped into their columns, columns in first-seen (= registration) order.
        public static List<KeyValuePair<string, List<ShaperShapeEntry>>> Columns()
        {
            var order = new List<string>();
            var byCat = new Dictionary<string, List<ShaperShapeEntry>>();
            foreach (var e in All())
            {
                string cat = string.IsNullOrEmpty(e.Category) ? "Other" : e.Category;
                if (!byCat.TryGetValue(cat, out var list))
                {
                    byCat[cat] = list = new List<ShaperShapeEntry>();
                    order.Add(cat);
                }
                list.Add(e);
            }
            return order.Select(c => new KeyValuePair<string, List<ShaperShapeEntry>>(c, byCat[c])).ToList();
        }

        /// The entry this node currently IS, or null when nothing matches (a Composite with no source
        /// assigned yet is the real case — it is a state the document can hold, so the picker says so
        /// rather than lying about being something).
        public static ShaperShapeEntry? Current(ShaperNode node)
        {
            if (node == null) return null;
            foreach (var e in All())
                if (e.IsCurrent != null && e.IsCurrent(node)) return e;
            return null;
        }

        // ── built-in providers ───────────────────────────────────────────────────────────────────────

        // A Phosphor glyph per primitive. Named per SHAPE rather than per index so reordering the enum
        // cannot silently repoint an icon; an unknown name resolves to null and simply draws no icon.
        static readonly Dictionary<ShaperPrimitiveKind, string> PrimitiveIcons =
            new Dictionary<ShaperPrimitiveKind, string>
            {
                { ShaperPrimitiveKind.Rect, "rectangle" },
                { ShaperPrimitiveKind.Ellipse, "circle" },
                { ShaperPrimitiveKind.Diamond, "diamond" },
                { ShaperPrimitiveKind.Triangle, "triangle" },
                { ShaperPrimitiveKind.Capsule, "pill" },
                { ShaperPrimitiveKind.NGon, "polygon" },
                { ShaperPrimitiveKind.Star, "star" },
                { ShaperPrimitiveKind.Sprite, "image" },
                { ShaperPrimitiveKind.Text, "text-t" },
            };

        static readonly Dictionary<ShaperPrimitiveKind, string> PrimitiveTips =
            new Dictionary<ShaperPrimitiveKind, string>
            {
                { ShaperPrimitiveKind.Rect, "Draw a rectangle, with an optional corner radius." },
                { ShaperPrimitiveKind.Ellipse, "Draw an ellipse from two radii — equal radii give a circle." },
                { ShaperPrimitiveKind.Diamond, "Draw a four-pointed diamond from a horizontal and a vertical radius." },
                { ShaperPrimitiveKind.Triangle, "Draw an isosceles triangle from a base width and a height." },
                { ShaperPrimitiveKind.Capsule, "Draw a stadium — a straight segment with a round cap at each end." },
                { ShaperPrimitiveKind.NGon, "Draw a regular polygon with as many sides as you set." },
                { ShaperPrimitiveKind.Star, "Draw a star whose arms you control for count, reach, width and twist." },
                { ShaperPrimitiveKind.Sprite, "Take the shape from a sprite's alpha, so any drawn artwork becomes the outline." },
                { ShaperPrimitiveKind.Text, "Make the glyphs of a line of text the shape itself, so fill, border and light apply to the letters." },
            };

        static IEnumerable<ShaperShapeEntry> Primitives()
        {
            foreach (ShaperPrimitiveKind k in Enum.GetValues(typeof(ShaperPrimitiveKind)))
            {
                var kind = k;
                PrimitiveIcons.TryGetValue(kind, out string icon);
                PrimitiveTips.TryGetValue(kind, out string tip);
                yield return new ShaperShapeEntry
                {
                    Category = "Primitives",
                    // T-0257 — not NicifyVariableName (it splits every internal capital, so NGon reads "N
                    // Gon"), and no longer the raw identifier either: ShaperWords is the one table that says
                    // Rect is a Rectangle, a Capsule is a Pill and an NGon is a Polygon. A kind with no entry
                    // falls through to its own name, which is what this line used to do for all of them.
                    Label = ShaperWords.Of(kind),
                    Icon = icon,
                    Tooltip = tip ?? ("Draw a " + ShaperWords.Of(kind).ToLowerInvariant() + "."),
                    IsCurrent = n => n.kind == ShaperNodeKind.Primitive && n.primitive != null && n.primitive.kind == kind,
                    Apply = n =>
                    {
                        n.kind = ShaperNodeKind.Primitive;
                        if (n.primitive == null) n.primitive = new ShaperPrimitiveDef();
                        n.primitive.kind = kind;
                        n.primitive.EnsureDials();
                    },
                };
            }
        }

        // A pyramid's silhouette IS a triangle, so it shares that glyph deliberately rather than borrowing
        // an unrelated one; the column header is what says these are the pseudo-3D solids.
        static readonly Dictionary<ShaperSolidForm, string> SolidIcons =
            new Dictionary<ShaperSolidForm, string>
            {
                { ShaperSolidForm.Box, "cube" },
                { ShaperSolidForm.Pyramid, "triangle" },
                { ShaperSolidForm.Can, "cylinder" },
                { ShaperSolidForm.Orb, "sphere" },
                { ShaperSolidForm.Gem, "diamond" },
                { ShaperSolidForm.Ring, "chart-donut" },
            };

        static IEnumerable<ShaperShapeEntry> Solids()
        {
            foreach (ShaperSolidForm f in Enum.GetValues(typeof(ShaperSolidForm)))
            {
                var form = f;
                SolidIcons.TryGetValue(form, out string icon);
                yield return new ShaperShapeEntry
                {
                    Category = "Solids",
                    Label = form.ToString(),
                    Icon = icon,
                    Tooltip = "Draw a shaded pseudo-3D " + form.ToString().ToLowerInvariant()
                        + " — it replaces the shape stage and then takes the ordinary fill, border and light "
                        + "pipeline. The form decides which of its dials do anything.",
                    IsCurrent = n => n.kind == ShaperNodeKind.Solid && n.solid != null && n.solid.form == form,
                    Apply = n =>
                    {
                        n.kind = ShaperNodeKind.Solid;
                        if (n.solid == null) n.solid = new ShaperSolidDef();
                        n.solid.form = form;
                    },
                };
            }
        }

        static IEnumerable<ShaperShapeEntry> BagEntry()
        {
            yield return new ShaperShapeEntry
            {
                Category = "Bag",
                Label = "Combine children",
                Icon = "stack",
                Tooltip = "Build this node's shape out of several members instead of one shape, each adding to, "
                    + "carving out of, or intersecting what the members before it left.",
                IsCurrent = n => n.kind == ShaperNodeKind.Bag,
                Apply = n =>
                {
                    n.kind = ShaperNodeKind.Bag;
                    if (n.children == null) n.children = new List<ShaperNode>();
                    // T-0267 — picking "Combine children" on a node that has none left the picture BLANK
                    // (measured: 4168 opaque px -> 0) until the author found "+ Add member" themselves, the
                    // same "does nothing" failure this programme has already fixed at every other affordance.
                    // Seeded with the same quarter-canvas member ShaperWindow.NewBagMember gives "+ Add
                    // member" — this provider has no window/document to size against, so a fixed 24px radius
                    // (Pyre's own DefaultSize convention, ShaperWindow.cs:507) stands in; visible at every
                    // canvas size this tool authors (32-256) without needing one.
                    if (n.children.Count == 0)
                    {
                        var seed = new ShaperNode { name = "Member 1" };
                        seed.primitive.EnsureDials();
                        seed.primitive.rectHalfWDial.staticValue = 24f;
                        seed.primitive.rectHalfHDial.staticValue = 24f;
                        n.children.Add(seed);
                    }
                },
            };
        }

        /// Every composite generator, from the same two attribute-declared families the old Generator menu
        /// read: concrete PyreForms named by PyreFormInfoAttribute, and non-form sources that opt in with
        /// ShaperCompositeSourceInfoAttribute. Both are read from the domain, so a newly written generator
        /// appears here without anyone remembering to list it.
        static IEnumerable<ShaperShapeEntry> Generators()
        {
            // T-0190 (PM vet of T-0182) — a form is OFFERED only if it has been declared for authoring: a
            // PyreFormInfoAttribute naming a group, or a PyreCompositeCatalog entry for its display name.
            // Without this, every concrete PyreForm in the domain lands in a fallback "Forms" column,
            // including test and fixture types — the picker screenshot showed "Test Disc Form" alone in a
            // column of its own, offering the author a shape that exists to be asserted about. The filter is
            // a declaration check rather than a name blacklist on purpose: a real generator declares itself
            // (that is what the attribute is FOR), and a fixture, by not declaring, opts out for free.
            var forms = TypeCache.GetTypesDerivedFrom<PyreForm>()
                .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (type: t, info: t.GetCustomAttribute<PyreFormInfoAttribute>()))
                .Where(x => !string.IsNullOrEmpty(x.info?.Group) || IsInShaperCatalog(x.type, x.info))
                .Select(x => (x.type, name: x.info?.DisplayName ?? ObjectNames.NicifyVariableName(x.type.Name),
                              group: string.IsNullOrEmpty(x.info?.Group) ? "Forms" : x.info.Group,
                              icon: x.info?.Icon, isForm: true));

            var sources = TypeCache.GetTypesDerivedFrom<IShaperCompositeSource>()
                .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (type: t, info: t.GetCustomAttribute<ShaperCompositeSourceInfoAttribute>()))
                .Where(x => x.info != null)
                .Select(x => (x.type, name: x.info.DisplayName, group: x.info.Group,
                              icon: (string)null, isForm: false));

            foreach (var e in forms.Concat(sources).OrderBy(x => x.group).ThenBy(x => x.name))
            {
                var type = e.type;
                bool isForm = e.isForm;
                yield return new ShaperShapeEntry
                {
                    Category = e.group,
                    Label = e.name,
                    Icon = e.icon ?? (isForm ? "sparkle" : "flame"),
                    Tooltip = isForm
                        ? e.name + ": bake this generator's own picture into the node. Its dials appear below "
                          + "the picker; the node's fill, border and transform are kept."
                        : e.name + ": run this simulation into the node. Its dials appear below the picker; "
                          + "the node's fill, border and transform are kept.",
                    IsCurrent = n =>
                    {
                        if (n.kind != ShaperNodeKind.Composite || n.composite == null) return false;
                        var src = n.composite.source;
                        if (isForm) return (src as PyreFormCompositeSource)?.form?.GetType() == type;
                        return src != null && !(src is PyreFormCompositeSource) && src.GetType() == type;
                    },
                    Apply = n =>
                    {
                        n.kind = ShaperNodeKind.Composite;
                        if (isForm) ApplyForm(n, type); else ApplySource(n, type);
                    },
                };
            }
        }

        /// The second half of the offer test above: a form with no attribute group is still offered if Shaper's
        /// own composite catalog names it, because that catalog IS a declaration that the form is authorable
        /// here. Instantiating to read DisplayName is what the assignment path already does; this runs once per
        /// domain reload behind ShaperShapeCatalog's cache, and a form whose constructor throws is simply not
        /// offered rather than taking the picker down with it.
        static bool IsInShaperCatalog(Type formType, PyreFormInfoAttribute info)
        {
            try
            {
                string name = info?.DisplayName;
                if (string.IsNullOrEmpty(name))
                    name = (Activator.CreateInstance(formType) as PyreForm)?.DisplayName;
                return !string.IsNullOrEmpty(name) && PyreCompositeCatalog.Find(name).displayName != null;
            }
            catch { return false; }
        }

        // ── the two composite assignments (pure mutations) ───────────────────────────────────────────
        // The bake box (half extents + bake resolution) is carried across from whatever the node already
        // had, so switching generator never silently resizes the node's footprint — moot on the next render
        // (ShaperCompositeDef.FitTo re-fits it to the canvas regardless) but keeps the fallback branch below
        // sane before that first render happens. T-0254 — the reason/note are retired off ShaperCompositeDef
        // entirely: §6.2's classification now lives on the catalog entry / ShaperCompositeSourceInfoAttribute,
        // never copied onto the def here.

        /// Assign a generator that is a hosted PyreForm.
        public static void ApplyForm(ShaperNode node, Type formType)
        {
            var form = (PyreForm)Activator.CreateInstance(formType);
            var entry = PyreCompositeCatalog.All.FirstOrDefault(e => e.displayName == form.DisplayName);
            node.composite = entry.displayName != null
                ? PyreCompositeCatalog.Build(form, entry,
                    node.composite.halfExtentX, node.composite.halfExtentY,
                    node.composite.bakeWidth, node.composite.bakeHeight)
                : new ShaperCompositeDef
                {
                    source = new PyreFormCompositeSource { form = form },
                    halfExtentX = node.composite.halfExtentX,
                    halfExtentY = node.composite.halfExtentY,
                    bakeWidth = node.composite.bakeWidth,
                    bakeHeight = node.composite.bakeHeight,
                };
        }

        /// Assign a generator that is a composite SOURCE rather than a hosted form (a stateful simulation).
        public static void ApplySource(ShaperNode node, Type sourceType)
        {
            var source = (IShaperCompositeSource)Activator.CreateInstance(sourceType);
            var entry = PyreCompositeCatalog.Find(source.SourceLabel);
            node.composite = entry.displayName != null
                ? PyreCompositeCatalog.BuildSource(source, entry,
                    node.composite.halfExtentX, node.composite.halfExtentY,
                    node.composite.bakeWidth, node.composite.bakeHeight)
                : new ShaperCompositeDef
                {
                    source = source,
                    halfExtentX = node.composite.halfExtentX,
                    halfExtentY = node.composite.halfExtentY,
                    bakeWidth = node.composite.bakeWidth,
                    bakeHeight = node.composite.bakeHeight,
                };
        }
    }

    public partial class ShaperWindow
    {
        // The picker menu is sized to show every column at once. The owner's constraint is the shape of the
        // screen, quoted: "We have a lot of width but less height. Don't make a big menu narrow and force it
        // to scroll. At least 4 columns is totally acceptable." The catalog currently yields NINE categories,
        // and at Pyre's own 104px column + 12px gutter that is ~1044 — so the menu is 1060 wide and every
        // category is visible in one row. The column row WRAPS rather than scrolls, so a screen too narrow
        // for all of them loses a row of columns instead of turning into one tall scrolling list.
        const float ShapeMenuWidth = 1060f;
        const float ShapeMenuColumnWidth = 104f;

        /// The one control that decides what this node draws. Reads back the current choice by name and
        /// icon, so the Shape card says what it is showing without the author opening anything.
        VisualElement BuildShapePickerRow(ShaperNode node)
        {
            var current = ShaperShapeCatalog.Current(node);
            string label = current?.Label ?? "Nothing yet";
            string tip = current.HasValue
                ? "This node draws " + current.Value.Label + ". Opens the full list of shapes, solids, "
                  + "generators and simulations — picking one keeps this node's fill, border and transform."
                : "This node has no shape source yet. Opens the full list of shapes, solids, generators and "
                  + "simulations.";

            // No "Shape" label beside it: the card is already called Shape, and a field labelled the same as
            // the box it sits in is the redundant title the layout rules forbid. The button's own content —
            // the icon and the name of what this node draws — is the label.
            // The button's content is built from CHILDREN, not from Button.text: a Button paints its own
            // text in its content box, so an icon added as a child would be drawn over the word rather
            // than before it. Icon, then name, then the caret that says this opens a list.
            UnityEngine.UIElements.Button btn = null;
            btn = Z.Button("", tip, () => ShowShapeMenu(node, btn));
            btn.style.flexDirection = FlexDirection.Row;
            btn.style.alignItems = Align.Center;
            btn.style.minWidth = 168f;      // wide enough for the longest generator name without truncating
            var icon = Z.Icon(current?.Icon, 14f);
            if (icon != null) { icon.style.marginRight = 5f; btn.Add(icon); }
            var name = new Label(label) { pickingMode = PickingMode.Ignore };
            name.style.flexGrow = 1f;
            btn.Add(name);
            var caret = new Label("▾") { pickingMode = PickingMode.Ignore };
            caret.style.marginLeft = 6f;
            btn.Add(caret);

            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.Add(btn);
            return row;
        }

        /// The picker itself: one column per category, exactly Pyre's shape menu (PyreWindow.cs:1193-1244,
        /// PyreWindow.Forms.cs:61-107) — the same item chrome, the same checkmark on the current entry — so
        /// the two tools' main pickers are the same object and not two dialects of one idea.
        void ShowShapeMenu(ShaperNode node, VisualElement anchor)
        {
            if (node == null) return;
            var menu = Z.Menu(anchor ?? shapeSection).Width(ShapeMenuWidth);
            menu.Custom((body, close) =>
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                // Wrap, never scroll: a screen too narrow for every column drops the tail onto a second row
                // of columns, which stays readable — a single scrolling column is the thing being replaced.
                row.style.flexWrap = Wrap.Wrap;
                foreach (var col in ShaperShapeCatalog.Columns())
                    row.Add(ShapeColumn(col.Key, col.Value, node, close));
                body.Add(row);
            });
            menu.Show();
        }

        VisualElement ShapeColumn(string title, List<ShaperShapeEntry> entries, ShaperNode node, Action close)
        {
            var col = new VisualElement();
            col.style.flexDirection = FlexDirection.Column;
            col.style.marginRight = 12f;
            col.style.minWidth = ShapeMenuColumnWidth;
            var head = new Label(title)
            {
                tooltip = title + " — every shape source in this family. Picking one replaces what this node "
                    + "draws and keeps its fill, border and transform.",
                pickingMode = PickingMode.Ignore,
            };
            head.AddToClassList("zui-menu__section");
            col.Add(head);

            foreach (var e in entries)
            {
                var entry = e;
                var item = new VisualElement { tooltip = entry.Tooltip };
                item.AddToClassList("zui-menu__item");
                item.style.flexDirection = FlexDirection.Row;
                item.style.alignItems = Align.Center;

                var check = new Label(entry.IsCurrent != null && entry.IsCurrent(node) ? "✓" : "")
                { pickingMode = PickingMode.Ignore };
                check.AddToClassList("zui-menu__check");
                item.Add(check);

                var img = Z.Icon(entry.Icon, 14f);
                if (img != null) { img.pickingMode = PickingMode.Ignore; img.style.marginRight = 5f; item.Add(img); }

                var lbl = new Label(entry.Label) { pickingMode = PickingMode.Ignore };
                lbl.AddToClassList("zui-menu__label");
                item.Add(lbl);

                // One Change for the whole switch (node kind AND the source it needs), so one Ctrl+Z puts
                // the node back — a two-step edit would take two undos and could leave a half-switched node.
                item.AddManipulator(new Clickable(() =>
                {
                    if (entry.Apply != null) Change(() => entry.Apply(node));
                    Rebuild();
                    close?.Invoke();
                }));
                col.Add(item);
            }
            return col;
        }
    }
}
