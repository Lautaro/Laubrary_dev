// SpriteFxStackView — a REUSABLE, host-agnostic editor control that draws a SpriteFx effect stack, so Pyre / Zoe /
// Chunks / a dedicated SpriteFx window can all embed the SAME stack UI without each re-implementing it.
//
// It edits a plain List<PyreModifier> (the exact type SpriteFxSpec.modifiers and SpriteFxFilter.modifiers both
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
// The Add menu offers the WHOLE modifier family — colour/mask, geometry warps, and the whole-frame passes that read
// a pixel's neighbours (outline, bloom, drop shadow) — discovered by reflection and grouped by what each does to the
// picture. It used to offer only the eleven "shaped" gather-free pixel effects, because those are the ones the Burst
// job can express; everything else was unreachable from a sprite, which was most of what an effect is usually FOR.
// SpriteFxStack.RunStack dispatches each family and keeps the Burst fast path for runs of shaped ones. Edge modifiers
// stay out: they deform a shape's outline mid-rasterisation, and a sprite arrives as finished pixels.
// The catalog is scanned once per domain and cached, like PyrePlus's AddableModifiers.
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
            // 150 is the MicroSlider norm and is TOO NARROW for a ZUIValue row: that control also has to
            // reserve a value field and the ... config button, which is the ONLY way to reach Curve /
            // Steps / Min-Max / Oscillation. Squeezed to 150 the switch has nowhere to go and every
            // animatable parameter reads as a plain static number -- the UI guide's own 170-200 figure
            // for a Value row exists for exactly this.
            public float ControlWidth = 190f;
        }

        /// <summary>
        /// Build the stack control over <paramref name="stack"/>. The returned element is a bare container (the outer
        /// titled/captured box is the host's concern — this control never wraps itself in one). Rebuildable in place:
        /// it owns a body element it clears/refills whenever the stack's structure changes.
        /// </summary>
        /// The Add menu offers every effect family; there is no longer a host flag for "may I also show the
        /// non-shaped colour ones", because the runtime dispatches all of them.
        public static VisualElement Build(List<PyreModifier> stack, Host host)
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
                        Dirty(() => stack.Add(s_clipboard.Clone()));
                        Structural();
                    });
                pasteBtn.SetEnabled(s_clipboard != null);

                var addBtn = Z.Button("+ Add SpriteFx", "Add a colour / mask effect to the stack.", null);
                addBtn.clicked += () => ShowAddMenu(addBtn);
                body.Add(WrapRow(addBtn, pasteBtn));
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
                    s_clipboard = m.Clone();
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
                    // The effect's SHORT fields FLOW rather than stacking. Each one used to claim a full row,
                    // so a card with five short sliders was five rows tall in a pane wide enough for three of
                    // them side by side — the space-economy rule's exact case.
                    //
                    // A WIDE control still takes its own line, per the same rule's other half. Left to pure
                    // wrapping, an animatable value's curve thumbnail got dragged up beside a shape radio and
                    // its label ended up stranded halfway across the card, naming nothing near it.
                    bodyEl = new VisualElement();
                    ZuiReflect.FlowFields(bodyEl, m, DrawerOptions(m));
                    box.Add(bodyEl);
                }

                // Fold state is kept PER EFFECT INSTANCE (ZuiFoldCard's weak table) so it survives this control's
                // rebuilds (undo / reorder / re-embed). The grip guards its own drag; toggle / Copy / × must not fold.
                //
                // No caret: the header still folds on click, it just does not spend a column on a glyph saying
                // so. Whether a card is open is already obvious from whether its fields are showing.
                ZuiFoldCard.Wire(m, header, bodyEl, showCaret: false, enableToggle, copyBtn, removeBtn);
                return box;
            }

            // The reflection drawer's Undo / dirty / rebuild contract for one effect's fields. A ZUIValue param gets
            // the FULL Static / Min-Max / Curve control automatically (ZuiReflect's ZUIValue branch, since slice 2) —
            // no FloatWrapperProperty needed. OnStructureChanged handles a nested list gaining/losing an element.
            ZuiReflect.Options DrawerOptions(PyreModifier m) => new ZuiReflect.Options
            {
                OnBeforeChange = host.OnBeforeChange,
                OnChanged = host.OnChanged,
                OnStructureChanged = Structural,
                Skip = f => f.Name == "enabled",
                ControlWidth = host.ControlWidth,
                // A field that documents itself keeps ITS description — a generated "X — a Y effect parameter"
                // only restates the label, which is the one thing a tooltip must not do. The generated line
                // stays as the fallback for anything undocumented, since it at least names the owning effect.
                TooltipFor = f => ZuiReflect.TooltipAttributeOf(f)
                                  ?? $"{ObjectNames.NicifyVariableName(f.Name)} — a {m.DisplayName} effect parameter.",
                // A SpriteFx stack resolves EVERY ZUIValue mode through SpriteFxStack.LifeEval, oscillation
                // included, so this is a host that may offer it.
                //
                // But it resolves them through the *AtNorm* evaluators — the value is sampled at the stack's
                // LIFE, a 0→1 position handed down by whatever is playing it. A per-value Duration / Warmup /
                // Loop is a wall-clock schedule those evaluators never consult, so the row did nothing here
                // at all: three dials offering a second timebase, inside the one window whose whole premise
                // is that the event above owns time. Hidden, which is what this option exists for.
                // Both of the envelope editor's meta rows are meaningless here and are hidden.
                //
                // Duration / Warmup / Loop is a WALL-CLOCK schedule, and LifeEval samples every mode through
                // the *AtNorm evaluators — the value is read at the stack's life, a position handed down by
                // whatever plays it. Three dials the runtime never consults, offering a second timebase.
                //
                // Value-Range lets the curve's Y run outside the field's own [Range], which the kernels clamp
                // to anyway — so it can only ever author a value that gets thrown away. Pinning it to the
                // declared range is what hideCurveRange does.
                //
                // The "live:" readout goes too. It evaluates the value against a WALL CLOCK, and a stack has
                // no wall clock — its life is a position handed down by whatever plays it. So the number
                // ticked away on a timeline unrelated to anything the author is looking at, next to a preview
                // showing the real one.
                ConfigureValue = (f, o) =>
                {
                    o.allowOscillation = true;
                    o.hideCurveTiming = true;
                    o.hideCurveRange = true;
                    o.hideLiveReadout = true;
                },
            };

            void ShowAddMenu(VisualElement anchor)
            {
                var menu = Z.Menu(anchor);
                string section = null;
                foreach (var e in Catalog())
                {
                    // Grouped by what the effect DOES to the picture, not by its C# base class — "Colour &
                    // mask" / "Warp" / "Whole frame" is the distinction an author is choosing between, and
                    // it also happens to be the one that decides how the runtime dispatches it.
                    if (e.section != section) { menu.Section(e.section); section = e.section; }
                    var type = e.type;
                    string label = e.label;
                    menu.Item(label, e.tooltip, () =>
                    {
                        Dirty(() => stack.Add((PyreModifier)Activator.CreateInstance(type)));
                        Structural();
                    });
                }
                menu.Show();
            }

            Rebuild();
            return root;
        }

        // Single in-memory clipboard (last-copied wins) — static so it survives closing/reopening a host window
        // within the session, like a real clipboard. A deep Clone() on copy AND on paste keeps every pasted effect
        // independent of the source and of each other.
        static PyreModifier s_clipboard;

        static VisualElement WrapRow(params VisualElement[] kids)
        {
            var r = Z.Row(kids);
            r.style.flexWrap = Wrap.Wrap;
            return r;
        }

        // Every effect the Add menu offers — the WHOLE modifier family, not just the eleven the Burst job can
        // express. Discovered by reflection, so a new effect appears here the moment it exists, and grouped by
        // how the runtime has to run it (see SpriteFxStack.RunStack).
        //
        // Edge modifiers are the one deliberate exclusion: they deform a SHAPE's outline while it is being
        // rasterised, and a sprite arrives as finished pixels with no shape to deform. Offering one would be
        // offering a control that cannot do anything.
        struct AddEntry { public Type type; public string label, section, tooltip; public int order; }
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
                    if (t.IsAbstract || !typeof(PyreModifier).IsAssignableFrom(t)) continue;
                    if (typeof(EdgeModifier).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    PyreModifier inst;
                    try { inst = (PyreModifier)Activator.CreateInstance(t); }
                    catch { continue; }

                    string section, tooltip;
                    int order;
                    if (inst is GeometryModifier)
                    {
                        section = "Warp the picture"; order = 1;
                        tooltip = "Moves pixels around — the sprite itself is bent, twisted or displaced. " +
                                  "Anything warped in from outside the frame arrives transparent.";
                    }
                    else if (inst is PostModifier)
                    {
                        section = "Whole frame"; order = 2;
                        tooltip = "Reads each pixel's NEIGHBOURS, so it can do what a per-pixel effect cannot — " +
                                  "outlines, glows, shadows. Runs over the whole picture at once.";
                    }
                    else
                    {
                        section = "Colour & mask"; order = 0;
                        tooltip = "Recolours or masks each pixel where it already is, without moving anything.";
                    }

                    string label = inst.DisplayName;
                    if (string.IsNullOrEmpty(label)) label = ObjectNames.NicifyVariableName(t.Name);
                    found.Add(new AddEntry
                    {
                        type = t, label = label, section = section, order = order,
                        tooltip = $"{label} — {tooltip}",
                    });
                }
            }
            found.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order)
                                                   : string.CompareOrdinal(a.label, b.label));
            s_catalog = found;
            return s_catalog;
        }
    }
}
