// ChunkWindow.Modifiers — the "Modifiers" sub-box of the Sampled Pseudo-3D Debris section (task #48).
// A ChunkSpec can carry a [SerializeReference] List<PixelModifier> stack baked ONCE into each SAMPLED
// chunk's texture at spawn (SampledChunkSprites.ApplyModifiers → SpriteFxFilter.Apply → SpriteFxStack.
// RunInline). This UI is the SAME polymorphic modifier-list pattern the Pyre / PyrePlus editors use — a
// drag-reorder grip + enable + "+ Add modifier" loop, with each modifier's body drawn generically by the
// shared reflection drawer (ZuiReflect) rather than a hand-written per-type switch — so every shaped
// SpriteFx pixel modifier is editable here with zero per-type code, and every dial records Undo.
//
// Only the SHAPED pixel modifiers are offered (the gather-free family RunInline actually applies:
// Tint/Contrast/Brightness/Saturation/Posterize/OrderedDither/LayerDissolve/AlphaMask), so the add menu
// never lists a modifier that would be a silent no-op on the chunk.
using System;
using System.Collections.Generic;
using System.Reflection;
using Laubrary.SpriteFx;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        // Stable body cleared/refilled on every add / remove / reorder / enable, so a structural change
        // repaints just this sub-box (mirrors PyrePlus's modifiersBody + RebuildModifiers split).
        VisualElement _modifiersBody;

        void BuildModifiers(VisualElement parent, ChunkSpec c)
        {
            var box = Z.Box("Modifiers",
                "Optional SpriteFx pixel modifiers baked once into each cut chunk's texture at spawn, applied " +
                "top-to-bottom — a cheap way to style sampled debris (tint, posterise, dither, dissolve, …). " +
                "Empty = the raw sampled pixels, unchanged. Only affects SAMPLED debris, not authored sprites, " +
                "procedural squares or animated content.");
            _modifiersBody = new VisualElement();
            box.Add(_modifiersBody);
            parent.Add(box);
            RebuildModifiers();
        }

        void RebuildModifiers()
        {
            var c = Spec;
            if (c == null || _modifiersBody == null) { _modifiersBody?.Clear(); return; }
            c.modifiers ??= new List<PixelModifier>();
            _modifiersBody.Clear();
            var list = c.modifiers;

            // A dedicated host for the rows so ZuiReorder's insertion line + index math only ever see modifier
            // blocks, never the "+ Add" button below (mirrors PyreWindow's own listHost split).
            var listHost = new VisualElement();
            _modifiersBody.Add(listHost);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) { int at = i; Dial("Remove modifier", () => list.RemoveAt(at)); RebuildModifiers(); return; }
                listHost.Add(BuildModifierBlock(listHost, list, i));
            }

            var addBtn = Z.Button("+ Add modifier",
                "Add a shaped SpriteFx pixel modifier to the stack (tint, posterise, dither, dissolve, …).", null);
            addBtn.W(130f);
            addBtn.clicked += () => ShowAddModifierMenu(addBtn, list);
            _modifiersBody.Add(addBtn);
        }

        VisualElement BuildModifierBlock(VisualElement listHost, List<PixelModifier> list, int index)
        {
            var m = list[index];
            var box = Z.Box(null, null);

            var header = new VisualElement();
            header.AddToClassList("zui-row");

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder — a modifier's position IS its apply order.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, box, listHost, (from, to) =>
            {
                Dial("Reorder modifier", () =>
                {
                    var mm = list[from];
                    list.RemoveAt(from);
                    list.Insert(to, mm);
                });
                RebuildModifiers();
            });
            header.Add(grip);

            var enableToggle = Z.Toggle("", "Enable or disable this modifier.", m.enabled, v =>
            {
                Dial("Toggle modifier", () => m.enabled = v);
                RebuildModifiers();   // rebuild so the body appears / disappears
            });
            header.Add(enableToggle);
            header.Add(Z.Text(m.DisplayName, ZuiText.Body, m.DisplayName + " modifier."));
            header.Add(Z.Flexible());
            var removeBtn = Z.Button("X", "Remove this modifier (undoable).", () =>
            {
                int at = list.IndexOf(m);
                if (at >= 0) { Dial("Remove modifier", () => list.RemoveAt(at)); RebuildModifiers(); }
            }).W(22f);
            header.Add(removeBtn);
            box.Add(header);

            // Body: every editable field of THIS modifier, drawn generically by the toolkit's reflection drawer,
            // in its own container so the card can FOLD to just the header (task #52). Only when enabled (a
            // disabled modifier is header-only, matching Pyre's own list — and has nothing to fold).
            VisualElement body = null;
            if (m.enabled)
            {
                body = new VisualElement();
                ZuiReflect.BuildFields(body, m, ModifierDrawerOptions(m));
                box.Add(body);
            }

            // Clicking the header folds the field body away, leaving the grip / enable / name / ✕ visible;
            // fold state is kept PER MODIFIER INSTANCE so it survives this window's rebuilds (undo / reorder /
            // spec selection). The grip already guards its own drag; the toggle / ✕ must not fold on click.
            ZuiFoldCard.Wire(m, header, body, enableToggle, removeBtn);

            return box;
        }

        // The reflection drawer's Undo / dirty / rebuild contract for a modifier's fields — the same
        // record-before / dirty-after wiring ChunkWindow.Dial gives every other dial. A modifier's animatable
        // ZUIValue params surface as their plain static value (the accepted shared-drawer limitation — see
        // PyrePlusWindow.Modifiers).
        ZuiReflect.Options ModifierDrawerOptions(PixelModifier m) => new ZuiReflect.Options
        {
            OnBeforeChange = () => { var c = Spec; if (c != null) Undo.RecordObject(c, "Edit chunk modifier"); },
            // The slicing preview bakes this stack into its debris, so it refreshes with every modifier edit.
            OnChanged = () => { var c = Spec; if (c != null) EditorUtility.SetDirty(c); RefreshChunkPreview(); },
            OnStructureChanged = () => { RebuildModifiers(); RefreshChunkPreview(); },
            // The enable toggle lives in the header row, so hide the base `enabled` field the drawer would surface.
            Skip = f => f.Name == "enabled",
            FloatWrapperProperty = StaticValueProp,
            TooltipFor = f => $"{ObjectNames.NicifyVariableName(f.Name)} — a {m.DisplayName} parameter.",
        };

        // The public `float staticValue { get; set; }` of a ZUIValue-like wrapper, or null — so an animatable
        // param renders as its static value. Mirrors PyrePlusWindow.StaticValueProp.
        static PropertyInfo StaticValueProp(Type t)
        {
            if (t == null || t.IsPrimitive || t == typeof(string) || t.IsEnum) return null;
            var p = t.GetProperty("staticValue", BindingFlags.Public | BindingFlags.Instance);
            return (p != null && p.PropertyType == typeof(float) && p.CanRead && p.CanWrite) ? p : null;
        }

        void ShowAddModifierMenu(VisualElement anchor, List<PixelModifier> list)
        {
            var menu = Z.Menu(anchor);
            foreach (var e in AddableModifiers())
            {
                var type = e.type;
                string label = e.label;
                menu.Item(label, $"Add the {label} modifier to the stack.", () =>
                {
                    Dial("Add modifier", () => list.Add((PixelModifier)Activator.CreateInstance(type)));
                    RebuildModifiers();
                });
            }
            menu.Show();
        }

        // Every concrete SHAPED PixelModifier a chunk's inline pass can actually apply — a parameterless-
        // constructible PixelModifier that SpriteFxStack.IsShaped accepts (the gather-free family RunInline
        // runs). Discovered by reflection so the add menu tracks the SpriteFx set with zero hand-maintained
        // catalog; non-shaped pixel modifiers (VoronoiCrack) are excluded because they would be silent no-ops
        // through RunInline. Cached — the scan runs once per domain.
        struct AddEntry { public Type type; public string label; }
        static List<AddEntry> _addable;
        static IEnumerable<AddEntry> AddableModifiers()
        {
            if (_addable != null) return _addable;
            var found = new List<AddEntry>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }   // skip dynamic / partially-loaded assemblies
                foreach (var t in types)
                {
                    if (t.IsAbstract || !typeof(PixelModifier).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    PixelModifier inst;
                    try { inst = (PixelModifier)Activator.CreateInstance(t); }
                    catch { continue; }
                    if (!SpriteFxStack.IsShaped(inst)) continue;   // only the family RunInline actually applies
                    string label = inst.DisplayName;
                    if (string.IsNullOrEmpty(label)) label = ObjectNames.NicifyVariableName(t.Name);
                    found.Add(new AddEntry { type = t, label = label });
                }
            }
            found.Sort((a, b) => string.CompareOrdinal(a.label, b.label));
            _addable = found;
            return _addable;
        }
    }
}
