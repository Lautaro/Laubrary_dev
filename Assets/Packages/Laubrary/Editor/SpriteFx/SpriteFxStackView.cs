// SpriteFxStackView — a REUSABLE, host-agnostic editor control that draws a SpriteFx effect stack, so Pyre / Zoe /
// Chunks / a dedicated SpriteFx window can all embed the SAME stack UI without each re-implementing it.
//
// It edits a plain List<PixelModifier> (the exact type SpriteFxSpec.modifiers and SpriteFxFilter.modifiers both
// carry), so ONE call site drives an authored "SpriteFx Stack" asset AND an inline filter list with zero adapter.
//
// Modelled on PyrePlusWindow.Modifiers' generic stack loop (ZuiReorder drag-reorder grip, a folding per-effect
// card, a GenericMenu "+ Add", per-effect bodies drawn by the shared reflection drawer ZuiReflect.BuildFields) —
// but lifted OUT of any EditorWindow: no window/asset type is referenced. Everything the control needs to talk to
// its owner (record Undo, mark dirty + repaint, repaint downstream of a structural change, the control width)
// arrives through the small `Host` callback bag.
//
// SURFACE NAMING: the family is called "SpriteFx" / "effect" in every label, button and tooltip — never "modifier"
// in user-facing text. The C# TYPE names (PyreModifier / PixelModifier / TintModifier / …) are left untouched: they
// are [SerializeReference]'d in committed demo assets, so renaming them would null authored data. "SpriteFx" is a
// surface skin only.
//
// The Add menu offers ONLY the "shaped" gather-free pixel family that SpriteFxStack actually applies at runtime —
// discovered by reflection (every parameterless-constructible PixelModifier subclass) and gated by the authoritative
// SpriteFxStack.IsShaped, so geometry / post / edge mods and non-shaped pixel mods (VoronoiCrack, Dissolve) never
// appear. The catalog is scanned once per domain and cached, like PyrePlus's AddableModifiers.
using System;
using System.Collections.Generic;
using Laubrary.SpriteFx;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.SpriteFx.Editor
{
    /// <summary>
    /// Draws an editable SpriteFx effect stack (a <c>List&lt;PixelModifier&gt;</c>) as an embeddable
    /// <see cref="VisualElement"/>. Host-agnostic: give it the list plus a <see cref="Host"/> callback bag and it
    /// owns its own in-place rebuild for add / remove / reorder / enable.
    /// </summary>
    public static class SpriteFxStackView
    {
        /// The callbacks the control needs to reach its owner — deliberately carries NO window or asset type so the
        /// same control embeds in Pyre, Zoe, Chunks or a standalone window.
        public sealed class Host
        {
            /// Fires ONCE before a mutation — the owner's <c>Undo.RecordObject(asset, …)</c> hook.
            public Action OnBeforeChange;
            /// Fires after every value edit — the owner's <c>EditorUtility.SetDirty</c> + repaint.
            public Action OnChanged;
            /// Fires after a STRUCTURAL change (add / remove / reorder / enable) — the control has already rebuilt its
            /// own rows in place; this lets the owner repaint anything downstream (a live preview, a summary). A host
            /// that re-invokes <see cref="Build"/> here is safe (fold/curve state is keyed per instance and survives).
            public Action Rebuild;
            /// Width for the reflected value controls inside each effect body (MicroSliders / Z.Value rows).
            public float ControlWidth = 150f;
        }

        /// <summary>
        /// Build the stack control over <paramref name="stack"/>. The returned element is a bare container (the outer
        /// titled/captured box is the host's concern — this control never wraps itself in one). Rebuildable in place:
        /// it owns a body element it clears/refills whenever the stack's structure changes.
        /// </summary>
        public static VisualElement Build(List<PixelModifier> stack, Host host)
        {
            host ??= new Host();
            var root = new VisualElement();
            var body = new VisualElement();   // the OWNED region cleared/refilled on every structural change
            root.Add(body);

            // Record-before + changed-after, the single mutation contract every edit routes through.
            void Dirty(Action mutate)
            {
                host.OnBeforeChange?.Invoke();
                mutate();
                host.OnChanged?.Invoke();
            }

            // A structural change: rebuild the rows in place, THEN notify the host so it can repaint downstream.
            void Structural()
            {
                Rebuild();
                host.Rebuild?.Invoke();
            }

            // Clear + refill the owned body: one folding card per effect, then the "+ Add SpriteFx" / "Paste" row.
            void Rebuild()
            {
                body.Clear();
                if (stack == null)
                {
                    body.Add(Z.Text("(no stack)", ZuiText.Small, "This control has no effect list to edit."));
                    return;
                }

                // A dedicated host for the rows so ZuiReorder's insertion line + index math only ever see effect
                // cards, never the "+ Add" row below (mirrors PyrePlus's own listHost split).
                var listHost = new VisualElement();
                body.Add(listHost);
                for (int i = 0; i < stack.Count; i++)
                {
                    if (stack[i] == null) { int at = i; Dirty(() => stack.RemoveAt(at)); Rebuild(); return; }
                    listHost.Add(BuildRow(listHost, i));
                }

                var pasteBtn = Z.Button(
                    "Paste" + (s_clipboard != null ? " " + s_clipboard.DisplayName : ""),
                    "Append a copy of the last-copied effect.",
                    () =>
                    {
                        if (s_clipboard == null) return;
                        Dirty(() => stack.Add((PixelModifier)s_clipboard.Clone()));
                        Structural();
                    });
                pasteBtn.SetEnabled(s_clipboard != null);

                body.Add(WrapRow(
                    Z.Button("+ Add SpriteFx", "Add a colour / mask effect to the stack.", () => ShowAddMenu()),
                    pasteBtn));
            }

            // One effect: a folding card whose header keeps grip / enable / name / Copy / × visible when collapsed,
            // and whose body is every editable field of the effect drawn generically by the shared reflection drawer.
            VisualElement BuildRow(VisualElement listHost, int index)
            {
                var m = stack[index];
                var box = Z.Box(null, null);   // untitled per-item card — bare, non-captured (not view-keyed)

                var header = new VisualElement();
                header.AddToClassList("zui-row");

                var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder — an effect's position IS its apply order.");
                grip.style.unityFontStyleAndWeight = FontStyle.Bold;
                grip.style.width = 16f;
                ZuiReorder.MakeGrip(grip, box, listHost, (from, to) =>
                {
                    Dirty(() =>
                    {
                        var mm = stack[from];
                        stack.RemoveAt(from);
                        stack.Insert(to, mm);
                    });
                    Structural();
                });
                header.Add(grip);

                var enableToggle = Z.Toggle("", "Enable or disable this effect.", m.enabled, v =>
                {
                    Dirty(() => m.enabled = v);
                    Structural();   // rebuild so the body appears / disappears
                });
                header.Add(enableToggle);

                header.Add(Z.Text(m.DisplayName, ZuiText.Body, m.DisplayName + " effect."));
                header.Add(Z.Flexible());

                var copyBtn = Z.Button("Copy", "Copy this effect's settings to the clipboard.", () =>
                {
                    s_clipboard = (PixelModifier)m.Clone();
                    Rebuild();   // refresh the Paste button's label / enabled state (no data change → not Structural)
                }).W(46f);
                header.Add(copyBtn);

                var removeBtn = Z.Button("×", "Remove this effect (undoable).", () =>
                {
                    int at = stack.IndexOf(m);
                    if (at >= 0) { Dirty(() => stack.RemoveAt(at)); Structural(); }
                }).W(22f);
                header.Add(removeBtn);
                box.Add(header);

                // Body: every editable field of THIS effect, drawn by the shared reflection drawer with the host's
                // Undo/dirty/rebuild contract. Only when enabled (a disabled effect is header-only, nothing to fold).
                // The base `enabled` field is skipped — the enable toggle above already owns it.
                VisualElement bodyEl = null;
                if (m.enabled)
                {
                    bodyEl = new VisualElement();
                    ZuiReflect.BuildFields(bodyEl, m, DrawerOptions(m));
                    box.Add(bodyEl);
                }

                // Fold state is kept PER EFFECT INSTANCE (ZuiFoldCard's weak table) so it survives this control's
                // rebuilds (undo / reorder / re-embed). The grip guards its own drag; toggle / Copy / × must not fold.
                ZuiFoldCard.Wire(m, header, bodyEl, enableToggle, copyBtn, removeBtn);
                return box;
            }

            // The reflection drawer's Undo / dirty / rebuild contract for one effect's fields. A ZUIValue param gets
            // the FULL Static / Min-Max / Curve control automatically (ZuiReflect's ZUIValue branch, since slice 2) —
            // no FloatWrapperProperty needed. OnStructureChanged handles a nested list gaining/losing an element.
            ZuiReflect.Options DrawerOptions(PixelModifier m) => new ZuiReflect.Options
            {
                OnBeforeChange = host.OnBeforeChange,
                OnChanged = host.OnChanged,
                OnStructureChanged = Structural,
                Skip = f => f.Name == "enabled",
                ControlWidth = host.ControlWidth,
                TooltipFor = f => $"{ObjectNames.NicifyVariableName(f.Name)} — a {m.DisplayName} effect parameter.",
            };

            void ShowAddMenu()
            {
                var menu = new GenericMenu();
                foreach (var e in Catalog())
                {
                    var type = e.type;
                    menu.AddItem(new GUIContent(e.label), false, () =>
                    {
                        Dirty(() => stack.Add((PixelModifier)Activator.CreateInstance(type)));
                        Structural();
                    });
                }
                menu.ShowAsContext();
            }

            Rebuild();
            return root;
        }

        // Single in-memory clipboard (last-copied wins) — static so it survives closing/reopening a host window
        // within the session, like a real clipboard. A deep Clone() on copy AND on paste keeps every pasted effect
        // independent of the source and of each other.
        static PixelModifier s_clipboard;

        static VisualElement WrapRow(params VisualElement[] kids)
        {
            var r = Z.Row(kids);
            r.style.flexWrap = Wrap.Wrap;
            return r;
        }

        // Every SpriteFx effect the Add menu offers: a parameterless-constructible PixelModifier subclass that
        // SpriteFxStack.IsShaped accepts (the gather-free family the runtime filter actually applies) — discovered by
        // reflection so the menu tracks the runtime's shaped set with zero hand-maintained list. Cached: scanned once
        // per domain (mirrors PyrePlus's AddableModifiers).
        struct AddEntry { public Type type; public string label; }
        static List<AddEntry> s_catalog;
        static IEnumerable<AddEntry> Catalog()
        {
            if (s_catalog != null) return s_catalog;
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
                    if (!SpriteFxStack.IsShaped(inst)) continue;   // ONLY the family the runtime stack applies
                    string label = inst.DisplayName;
                    if (string.IsNullOrEmpty(label)) label = ObjectNames.NicifyVariableName(t.Name);
                    found.Add(new AddEntry { type = t, label = label });
                }
            }
            found.Sort((a, b) => string.CompareOrdinal(a.label, b.label));
            s_catalog = found;
            return s_catalog;
        }
    }
}
