// ChunkWindow.DebrisScatterCard — the card for a Debris Scatter: the shrapnel-swarm producer, and the one
// capability with the most authored shape (four ways to look, an optional floor, an optional pixel-modifier
// stack on sampled cuts). The shape to copy is PyreBlastCard's: the ONE control that decides which other
// controls exist (Visual) comes first and rebuilds only this card when it changes; each mode's own fields sit
// directly under it and are ABSENT — not disabled — when the mode does not have them; the rest pack into rows
// of short controls; the floor is a box because it is a self-contained on/off feature, not a redundant title;
// the layer slot is last because it answers "where does this draw," not "what is this."
//
// Direction is deliberately NOT a dial here: resolved decision #1 (CHUNKS-DESIGN-DECISIONS.md §8) makes a
// burst's aim a recipe-level input every producer inherits, so only Spread (the cone's own width) is this
// capability's business — matching DebrisScatter.cs, which carries spreadDeg and no directionDeg.
using System;
using System.Collections.Generic;
using Laubrary.SpriteFx;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildDebrisScatterCard(VisualElement body, ChunkSpec c, DebrisScatter cap)
        {
            string id = cap.id;

            // ── what a chunk looks like, and only that choice's own dials ────────────
            body.Add(Z.Field("Visual",
                "What each chunk is made of. Only the chosen kind's own dials are authored.",
                Z.MiniRadio((int)cap.visual, new[] { "Squares", "Sprites", "Sampled", "Animated" },
                    "What each chunk is made of. Only the chosen kind's own dials are authored.",
                    i => DialAndRebuildCard(id, "Set Debris Visual", () => cap.visual = (DebrisVisual)i))));

            // Pixels/unit is the RESOLUTION of the generated sprite (the procedural square, or a Sampled cut)
            // — never its size on screen: Chunk.Init normalises whatever sprite it is given to the Size dial's
            // world units, so a chunk is exactly as big as Size says whatever this is set to. Irrelevant to
            // Sprites/Animated, which bring their own artwork. Defaults to the project's own Pixel Scale
            // setting (Laubrary/Pixel Scale Project Settings) so debris is blocky at the project's own density
            // without any authoring; the override float only shows once that default is turned off.
            if (cap.visual == DebrisVisual.Squares || cap.visual == DebrisVisual.Sampled)
            {
                body.Add(Z.Toggle("Use project pixel scale",
                    "Pull pixels-per-unit from the project's Pixel Scale Project Settings asset instead of the " +
                    "override below. Off = always use the override, whatever the project says.",
                    cap.useProjectPixelScale, v => DialAndRebuildCard(id, "Toggle Use Project Pixel Scale",
                        () => cap.useProjectPixelScale = v)));
                if (!cap.useProjectPixelScale)
                    body.Add(Z.Field("Pixels/unit",
                        "How many pixels across the generated chunk sprite is. Its size on screen is the Size " +
                        "dial's, not this — a higher value is a crisper chunk, not a bigger one.",
                        Z.Float(cap.pixelsPerUnitOverride, "Resolution of the generated chunk sprite. Size on screen is the Size dial's.",
                            v => Dial("Edit Pixels/Unit", () => cap.pixelsPerUnitOverride = Mathf.Max(1f, v)), 70f)));
                else
                    body.Add(Z.Text($"Currently {cap.EffectivePixelsPerUnit:0.#} px/unit (project setting).",
                        ZuiText.Subtle, "The live value read from the project's Pixel Scale Project Settings asset."));
            }

            switch (cap.visual)
            {
                case DebrisVisual.Squares:
                    break;

                case DebrisVisual.Sprites:
                    {
                        var listField = SpriteListField(c, cap);
                        if (listField != null) body.Add(listField);
                        break;
                    }

                case DebrisVisual.Sampled:
                    BuildSampledBlock(body, c, cap, id);
                    break;

                case DebrisVisual.Animated:
                    body.Add(Z.Field("Animation",
                        "Animated content every chunk plays instead of a static sprite — a Pyre, a Zoe, or a " +
                        "wrapper that overrides its speed or looping.",
                        AssetPicker(cap.animationSource,
                                    o => DialAndRebuildCard(id, "Set Animation Source", () => cap.animationSource = o),
                                    typeof(IChunkAnimation), cap.Title,
                                    "Animated content every chunk plays instead of a static sprite.")));
                    break;
            }

            // ── emission ───────────────────────────────────────────────────────────
            body.Add(Z.MicroMinMax("Count", cap.countMin, cap.countMax, 0f, 64f,
                "How many chunks a burst spawns. Each burst picks one random count in between.",
                (lo, hi) => Dial("Edit Count", () =>
                {
                    cap.countMin = Mathf.RoundToInt(lo);
                    cap.countMax = Mathf.RoundToInt(hi);
                }), 150f, showValue: true, decimals: 0));

            body.Add(Z.HGroup(
                Z.MicroMinMax("Speed", cap.speedMin, cap.speedMax, 0f, 20f,
                    "Launch speed, world units/sec. Each chunk picks one random speed in between.",
                    (lo, hi) => Dial("Edit Speed", () => { cap.speedMin = lo; cap.speedMax = hi; }),
                    150f, showValue: true, decimals: 2),
                Z.MicroSlider("Upward bias", cap.upwardBias, -10f, 10f,
                    "Extra upward velocity on every chunk, so even a radial burst pops. Below zero it presses " +
                    "them down instead.",
                    v => Dial("Edit Upward Bias", () => cap.upwardBias = v), 150f, showValue: true, decimals: 2)));

            body.Add(Z.MicroSlider("Spread", cap.spreadDeg, 0f, 180f,
                "Cone half-angle around the recipe's own aim. 0 = a tight jet; 180 = a full circle.",
                v => Dial("Edit Spread", () => cap.spreadDeg = v), 150f, showValue: true, decimals: 0));

            // ── flight ─────────────────────────────────────────────────────────────
            body.Add(Z.HGroup(
                Z.MicroSlider("Gravity", cap.gravity, 0f, 40f,
                    "Downward acceleration, world units/sec². Higher = snappier arcs that fall fast.",
                    v => Dial("Edit Gravity", () => cap.gravity = v), 150f, showValue: true, decimals: 1),
                Z.MicroSlider("Drag", cap.drag, 0f, 5f,
                    "Air resistance: per-second damping of velocity. 0 = none, ~1 = noticeable, ~3 = soupy.",
                    v => Dial("Edit Drag", () => cap.drag = v), 150f, showValue: true, decimals: 2)));

            // A tumbling sampled cut takes its rate from Tumble speed instead — the two are the same dial for
            // one chunk, and DebrisScatter.Fire reads only one of them. Showing both would offer a spin dial
            // that silently does nothing, which is exactly what the mode branches above exist to avoid.
            if (!(cap.UsesSampledDebris && cap.tumble))
                body.Add(Z.MicroMinMax("Spin", cap.angularSpeedMin, cap.angularSpeedMax, 0f, 720f,
                    "Spin rate, degrees/sec. Each chunk picks its own rate in between and its own direction.",
                    (lo, hi) => Dial("Edit Spin", () => { cap.angularSpeedMin = lo; cap.angularSpeedMax = hi; }),
                    150f, showValue: true, decimals: 0));

            body.Add(Z.Toggle("Face velocity",
                "Point each chunk along its travel direction instead of spinning it freely.",
                cap.faceVelocity, v => Dial("Toggle Face Velocity", () => cap.faceVelocity = v)));

            // ── life / look ────────────────────────────────────────────────────────
            body.Add(Z.HGroup(
                Z.MicroMinMax("Life", cap.lifeMin, cap.lifeMax, 0.01f, 8f,
                    "Lifetime, seconds. Each chunk picks its own, then fades out on the curves below.",
                    (lo, hi) => Dial("Edit Life", () => { cap.lifeMin = Mathf.Max(0.01f, lo); cap.lifeMax = Mathf.Max(cap.lifeMin, hi); }),
                    150f, showValue: true, decimals: 2),
                Z.MicroMinMax("Size", cap.sizeMin, cap.sizeMax, 0.001f, 2f,
                    "On-screen size, world units. Each chunk picks its own, then rides Size over life from there.",
                    (lo, hi) => Dial("Edit Size", () => { cap.sizeMin = Mathf.Max(0.001f, lo); cap.sizeMax = Mathf.Max(cap.sizeMin, hi); }),
                    150f, showValue: true, decimals: 3)));

            body.Add(Z.Field("Size over life",
                "Size multiplier across a chunk's life, left (spawn) to right (death).",
                Z.Envelope(cap.sizeEnvelope,
                    new ZuiEnvelopeOptions { xMin = 0f, xMax = 1f, yMin = 0f, yMax = 2f },
                    "Size multiplier across a chunk's life, left (spawn) to right (death).",
                    onChanged: EnvelopeChanged,
                    onBeforeMutate: () => EnvelopeUndo("Edit Size Over Life"))));
            body.Add(Z.Field("Alpha over life",
                "Opacity across a chunk's life, left (spawn) to right (death).",
                Z.Envelope(cap.alphaEnvelope,
                    new ZuiEnvelopeOptions { xMin = 0f, xMax = 1f, yMin = 0f, yMax = 1f },
                    "Opacity across a chunk's life, left (spawn) to right (death).",
                    onChanged: EnvelopeChanged,
                    onBeforeMutate: () => EnvelopeUndo("Edit Alpha Over Life"))));
            body.Add(Z.Field("Colour over life",
                "Tint multiplied onto each chunk across its life, left (spawn) to right (death).",
                Z.Gradient(cap.colorOverLife, "Tint multiplied onto each chunk across its life.",
                    v => Dial("Edit Colour Over Life", () => cap.colorOverLife = v))));

            // ── floor ──────────────────────────────────────────────────────────────
            var floor = Z.BoxKeyed("Floor", "Bounce chunks off a horizontal floor. No Physics2D colliders involved.",
                                    "Chunks.card." + id + ".floor");
            floor.AddHeaderContent(Z.Toggle("On",
                "Bounce chunks off a horizontal floor at the height below.",
                cap.useFloor, v => Dial("Toggle Floor", () => { cap.useFloor = v; floor.SetEnabled(v); })));
            floor.SetEnabled(cap.useFloor);
            floor.Add(Z.HGroup(
                Z.Field("Floor Y",
                    "World height the chunks land on.",
                    Z.Float(cap.floorY, "World height the chunks land on.",
                        v => Dial("Edit Floor Y", () => cap.floorY = v), 70f)),
                Z.MicroSlider("Bounce", cap.bounciness, 0f, 1f,
                    "Speed that survives a floor hit. 0 = dead stop, 1 = full bounce.",
                    v => Dial("Edit Bounciness", () => cap.bounciness = v), 150f, showValue: true, decimals: 2)));
            floor.Add(Z.MicroSlider("Friction", cap.floorFriction, 0f, 1f,
                "Sideways speed lost per floor hit. 0 = frictionless slide, 1 = stops sliding at once.",
                v => Dial("Edit Floor Friction", () => cap.floorFriction = v), 150f, showValue: true, decimals: 2));
            floor.Add(Z.Toggle("Rest on floor",
                "Let a slow chunk settle on the floor until it fades, instead of despawning where it lands.",
                cap.restOnFloor, v => Dial("Toggle Rest On Floor", () => cap.restOnFloor = v)));
            body.Add(floor);

            body.Add(Z.Field("Seed",
                "Fixes every random pick so the scatter is identical every play. 0 rerolls.",
                Z.Int(cap.seed, "Fixes every random pick so the scatter is identical every play.",
                      v => Dial("Edit Debris Seed", () => cap.seed = v), 70f)));

            var slot = LayerSlotRow(c, () => cap.layerName, v => cap.layerName = v);
            if (slot != null) body.Add(slot);
        }

        // ── Sprites mode: the sprite pool stays Unity's own bound list UI ────────────────────────────────────
        // ZUI has no reorderable-list control and this is one place reimplementing one buys nothing (same call
        // taken by the pre-capability-stack window). Unlike that window's flat `ChunkSpec.sprites`, this field
        // now lives on a capability inside a [SerializeReference] stack, so the SerializedProperty has to be
        // reached through `capabilities.Array.data[i]` rather than found directly by name.
        static VisualElement SpriteListField(ChunkSpec c, DebrisScatter cap)
        {
            var so = new SerializedObject(c);
            var caps = so.FindProperty("capabilities");
            int index = c.capabilities != null ? c.capabilities.IndexOf(cap) : -1;
            if (caps == null || index < 0 || index >= caps.arraySize) return null;
            var capProp = caps.GetArrayElementAtIndex(index);
            var spritesProp = capProp.FindPropertyRelative("sprites");
            if (spritesProp == null) return null;

            var field = new PropertyField(spritesProp, "Sprites")
            {
                tooltip = "Chunk sprites to pick from at random.",
            };
            field.Bind(so);
            // A PropertyField's inner ListView is not a BaseField, so the sheet's flex-grow:0 guard doesn't
            // reach it and it would otherwise span the whole card.
            field.style.width = 300f;
            field.style.flexShrink = 0f;
            return field;
        }

        // ── Sampled mode: cut source, tumble, tint, and the baked pixel-modifier stack ───────────────────────
        void BuildSampledBlock(VisualElement body, ChunkSpec c, DebrisScatter cap, string id)
        {
            // A plain Sprite, not a LauAsset — Z.Object<T> is the established raw-asset control for this
            // exact case across the codebase (Pyre, BackSplash, Cartographer, SpriteFx all reach for it for
            // a Sprite field), never the LauAsset chip AssetPicker wraps, which only browses registered
            // LauAsset types and would show an empty list for a plain imported sprite.
            body.Add(Z.Field("Sample source",
                "The sprite small chunks are cut out of, so the debris is made of the exploding object's own " +
                "pixels. Its texture needs Read/Write Enabled.",
                Z.Object<Sprite>(cap.sampleSource, "The sprite small chunks are cut out of.",
                    v => DialAndRebuildCard(id, "Set Sample Source", () => cap.sampleSource = v), 200f)));

            if (cap.sampleSource == null) return;

            body.Add(Z.MicroMinMax("Sample px", cap.samplePxMin, cap.samplePxMax, 1f, 64f,
                "Size of each cut, in source-texture pixels. Anything wider than the source is trimmed to it.",
                (lo, hi) => Dial("Edit Sample Px", () =>
                {
                    cap.samplePxMin = Mathf.Max(1, Mathf.RoundToInt(lo));
                    cap.samplePxMax = Mathf.Max(cap.samplePxMin, Mathf.RoundToInt(hi));
                }), 150f, showValue: true, decimals: 0));

            body.Add(Z.Toggle("Tumble",
                "Turn each sampled piece with a squash+shade trick that reads as a lit 3D fragment, instead of " +
                "a flat 2D spin.",
                cap.tumble, v => DialAndRebuildCard(id, "Toggle Tumble", () => cap.tumble = v)));

            if (cap.tumble)
            {
                body.Add(Z.HGroup(
                    Z.MicroMinMax("Tumble speed", cap.tumbleSpeedMin, cap.tumbleSpeedMax, 0f, 720f,
                        "Simulated tumble rate, degrees/sec. Each piece picks its own rate and direction.",
                        (lo, hi) => Dial("Edit Tumble Speed", () => { cap.tumbleSpeedMin = lo; cap.tumbleSpeedMax = hi; }),
                        150f, showValue: true, decimals: 0),
                    Z.MicroSlider("Shade", cap.tumbleShadeStrength, 0f, 1f,
                        "How strong the light/dark swing is as a piece turns. 0 = squash only, 1 = full swing.",
                        v => Dial("Edit Tumble Shade", () => cap.tumbleShadeStrength = v), 150f, showValue: true, decimals: 2)));
            }

            var tint = Z.BoxKeyed("Tint",
                "Recolours sampled debris as it is cut: none, every opaque pixel, just the rim, or everywhere " +
                "except the rim.", "Chunks.card." + id + ".tint");
            tint.Add(Z.Field("Mode",
                "Which pixels of a cut chunk get recoloured.",
                Z.MiniRadio((int)cap.tintMode, new[] { "None", "Whole", "Edges only", "Excluding edges" },
                    "Which pixels of a cut chunk get recoloured.",
                    i => DialAndRebuildCard(id, "Set Tint Mode", () => cap.tintMode = (ChunkTintMode)i), true)));
            if (cap.tintMode != ChunkTintMode.None)
            {
                tint.Add(Z.HGroup(
                    Z.Field("Colour",
                        "The colour the cut pixels are pulled towards.",
                        Z.Color(cap.tintColor, "The colour the cut pixels are pulled towards.",
                            v => Dial("Edit Tint Colour", () => cap.tintColor = v), 110f)),
                    Z.MicroSlider("Strength", cap.tintStrength, 0f, 1f,
                        "How far the tint pulls the source pixel. 0 = no visible effect, 1 = fully replaced.",
                        v => Dial("Edit Tint Strength", () => cap.tintStrength = v), 150f, showValue: true, decimals: 2)));
                if (cap.tintMode != ChunkTintMode.Whole)
                    tint.Add(Z.Field("Edge px",
                        "Edge modes only: how many pixels in from the rim count as edge.",
                        Z.Int(cap.edgeThicknessPx, "How many pixels in from the rim count as edge.",
                              v => Dial("Edit Edge Thickness", () => cap.edgeThicknessPx = Mathf.Max(1, v)), 70f)));
            }
            body.Add(tint);

            BuildModifiersBox(body, c, cap, id);

            // The old window's sampled-cut preview cell (ChunkWindow.Preview.cs, ~150 lines of texture-buffer
            // IMGUI painting) is not lifted here — it is a bespoke canvas island sized for the shared Preview
            // stage W2.4 owns, not a card row; flagged in this task's handover for that task to pick up.
        }

        // ── the baked pixel-modifier stack, lifted from the pre-capability-stack window's BuildModifiers ──────
        // Same polymorphic list pattern Pyre's own modifier stack uses: a drag-reorder grip, an enable toggle,
        // a fold, and every field of the concrete type drawn generically by ZuiReflect. Structural edits (add /
        // remove / reorder / enable) go through DialAndRebuildCard like everything else on this card; a live
        // per-field edit inside ZuiReflect does not fit Dial's single-apply-action shape (it fires once per
        // keystroke/drag tick, not once per gesture), so it routes through the same direct Undo/SetDirty/
        // InvalidatePreview calls PyreWindow.Modifiers.cs's ModifierDrawerOptions already uses for the identical
        // seam — not a new pattern, the established one for this specific control.
        void BuildModifiersBox(VisualElement parent, ChunkSpec c, DebrisScatter cap, string id)
        {
            var box = Z.BoxKeyed("Modifiers",
                "Pixel modifiers baked once into each sampled chunk at spawn, applied top-to-bottom — a cheap " +
                "way to style sampled debris (tint, posterise, dither, dissolve, …). Empty = the raw sampled " +
                "pixels, unchanged.", "Chunks.card." + id + ".modifiers");
            var listHost = new VisualElement();
            box.Add(listHost);
            RefillModifiers(listHost, c, cap, id);

            var addBtn = Z.Button("+ Add modifier",
                "Add a shaped SpriteFx pixel modifier to the stack (tint, posterise, dither, dissolve, …).", null);
            addBtn.W(130f);
            addBtn.clicked += () => ShowAddModifierMenu(addBtn, c, cap, id, listHost);
            box.Add(addBtn);
            parent.Add(box);
        }

        void RefillModifiers(VisualElement listHost, ChunkSpec c, DebrisScatter cap, string id)
        {
            listHost.Clear();
            cap.modifiers ??= new List<PixelModifier>();
            var list = cap.modifiers;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) { list.RemoveAt(i); i--; continue; }
                listHost.Add(BuildModifierBlock(listHost, c, cap, id, list, i));
            }
        }

        VisualElement BuildModifierBlock(VisualElement listHost, ChunkSpec c, DebrisScatter cap, string id,
                                         List<PixelModifier> list, int index)
        {
            var m = list[index];
            var box = Z.Box(null, null);

            var header = new VisualElement();
            header.AddToClassList("zui-row");

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder — a modifier's position is its apply order.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, box, listHost, (from, to) =>
                DialAndRebuildCard(id, "Reorder Modifier", () =>
                {
                    var mm = list[from];
                    list.RemoveAt(from);
                    list.Insert(to, mm);
                }));
            header.Add(grip);

            var enableToggle = Z.Toggle("", "Enable or disable this modifier.", m.enabled,
                v => DialAndRebuildCard(id, "Toggle Modifier", () => m.enabled = v));
            header.Add(enableToggle);
            header.Add(Z.Text(m.DisplayName, ZuiText.Body, m.DisplayName + " modifier."));
            header.Add(Z.Flexible());
            var removeBtn = Z.Button("×", "Remove this modifier (undoable).", () =>
                DialAndRebuildCard(id, "Remove Modifier", () => list.Remove(m))).W(22f);
            header.Add(removeBtn);
            box.Add(header);

            VisualElement fieldBody = null;
            if (m.enabled)
            {
                fieldBody = new VisualElement();
                ZuiReflect.BuildFields(fieldBody, m, ModifierDrawerOptions(c, id));
                box.Add(fieldBody);
            }

            ZuiFoldCard.Wire(m, header, fieldBody, enableToggle, removeBtn);
            return box;
        }

        // Same contract every ZuiReflect host in this codebase gives it (Pyre's own modifiers editor included):
        // Undo before the mutation, dirty + repaint after, and a full card rebuild only when the stack's own
        // SHAPE changed (add/remove — the enable toggle already goes through DialAndRebuildCard above).
        ZuiReflect.Options ModifierDrawerOptions(ChunkSpec c, string id) => new ZuiReflect.Options
        {
            OnBeforeChange = () => { if (c != null) Undo.RecordObject(c, "Edit Chunk Modifier"); },
            OnChanged = () => { if (c != null) EditorUtility.SetDirty(c); InvalidatePreview(); },
            OnStructureChanged = () => RebuildCard(id),
            Skip = f => f.Name == "enabled",
            FloatWrapperProperty = StaticValueProp,
            TooltipFor = f => $"{ObjectNames.NicifyVariableName(f.Name)} — a modifier parameter.",
        };

        static System.Reflection.PropertyInfo StaticValueProp(Type t)
        {
            if (t == null || t.IsPrimitive || t == typeof(string) || t.IsEnum) return null;
            var p = t.GetProperty("staticValue", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return (p != null && p.PropertyType == typeof(float) && p.CanRead && p.CanWrite) ? p : null;
        }

        void ShowAddModifierMenu(VisualElement anchor, ChunkSpec c, DebrisScatter cap, string id, VisualElement listHost)
        {
            var menu = Z.Menu(anchor);
            foreach (var e in AddableModifiers())
            {
                var type = e.type;
                string label = e.label;
                menu.Item(label, $"Add the {label} modifier to the stack.", () =>
                    DialAndRebuildCard(id, "Add Modifier", () =>
                        cap.modifiers.Add((PixelModifier)Activator.CreateInstance(type))));
            }
            menu.Show();
        }

        // Every concrete SHAPED PixelModifier a chunk's inline bake can actually apply — reflection-discovered
        // so the add menu tracks the SpriteFx set with zero hand-maintained catalogue. Cached: the scan runs
        // once per domain.
        struct ModifierEntry { public Type type; public string label; }
        static List<ModifierEntry> _addableModifiers;
        static IEnumerable<ModifierEntry> AddableModifiers()
        {
            if (_addableModifiers != null) return _addableModifiers;
            var found = new List<ModifierEntry>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t.IsAbstract || !typeof(PixelModifier).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    PixelModifier inst;
                    try { inst = (PixelModifier)Activator.CreateInstance(t); }
                    catch { continue; }
                    if (!SpriteFxStack.IsShaped(inst)) continue;
                    string label = inst.DisplayName;
                    if (string.IsNullOrEmpty(label)) label = ObjectNames.NicifyVariableName(t.Name);
                    found.Add(new ModifierEntry { type = t, label = label });
                }
            }
            found.Sort((a, b) => string.CompareOrdinal(a.label, b.label));
            _addableModifiers = found;
            return _addableModifiers;
        }
    }
}
