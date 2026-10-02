using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.MetaMapper.Editor
{
    /// The LEFT PANE: the subject readout and THE LAYER PANEL — the part of this window that matters most,
    /// because a layer that merely EXISTS is already a queryable fact ("is this clump a loot shelf?" is
    /// `HasLayer("LootShelf")`, and the answer needs no marks at all).
    ///
    /// AMENDMENT 2026-08-03 is enforced here: a layer's Uniform/PerFrame binding is an AUTHORED CHOICE that
    /// this panel always SHOWS, never infers. Nothing collapses a PerFrame track because its entries happen
    /// to match; PerFrame → Uniform warns and makes the author name the winning frame; Uniform → PerFrame
    /// expands losslessly.
    public partial class MetaMapperWindow
    {
        static readonly string[] KindNames = { "Points", "Directions", "Mask", "Shapes" };

        readonly List<VisualElement> layerCards = new List<VisualElement>();
        VisualElement declaredHost;
        string flash;
        double flashUntil;

        static readonly Color ActiveAccent = new Color(1f, 0.82f, 0.28f, 1f);

        /// A one-line note that survives a few seconds of pointer movement — how a destructive or surprising
        /// action reports what it did without a dialog and without adding a row that would reflow anything.
        internal void Flash(string message)
        {
            flash = message;
            flashUntil = EditorApplication.timeSinceStartup + 6.0;
            if (statusLine != null) statusLine.text = message;
        }

        bool TryShowFlash()
        {
            if (flash == null) return false;
            if (EditorApplication.timeSinceStartup >= flashUntil) { flash = null; return false; }
            statusLine.text = flash;
            return true;
        }

        void BuildLeftPane(VisualElement body)
        {
            body.Add(BuildSubjectBox());

            layersBody = new VisualElement();
            body.Add(Z.BoxKeyed("Layers",
                "The named meanings this map declares. THE LAYER ID IS THE SEMANTIC KEY the reading code " +
                "looks up, case-insensitively — a layer that merely exists already answers \"is this thing a " +
                "loot shelf?\". Marks are only needed when the answer also needs a position.",
                "metamapper.layers",
                BuildAddLayerRows(),
                layersBody));
            RebuildLayers();
        }

        // ── the subject box ─────────────────────────────────────────────────────────

        VisualElement BuildSubjectBox()
        {
            var d = Data;
            var col = new VisualElement();

            // The subject's own NAME, from the host that supplied the picture — not the subject ref's key,
            // which is opaque identity (a GUID for a clump) and would read as gibberish here.
            string subjectText = pushed != null && pushed.IsSet ? SubjectTitle()
                : map != null && map.subject != null && map.subject.subject != null ? map.subject.subject.name
                : "(pushed by a tool)";
            col.Add(Z.Field("Subject", "What this map explains. A map's coordinate SPACE comes from its " +
                                       "subject and is not hand-editable — changing it would reinterpret " +
                                       "every authored number.",
                Z.Text(subjectText, ZuiText.Body, "The subject this metadata is anchored to.")));

            // An EMBEDDED subject has no asset of its own — that is the point (design §3): the data lives on
            // the thing it describes, so there is nothing to create, name, or go looking for.
            if (map == null && pushed != null && pushed.IsSet)
                col.Add(Z.Field("Stored on", "Where this metadata actually lives. It is part of that asset, " +
                                             "not a separate file — nothing to lose track of.",
                    Z.Text(pushed.subject != null ? pushed.subject.name : "-", ZuiText.Body,
                        "The asset this metadata is embedded in.")));

            // Sprite is the ONE subject type this module can name (Sprite is UnityEngine's own), so it is the
            // only one pickable here; Clump/Level subjects are picked in the tool that owns them and arrive
            // through the provider registry.
            if (map != null)
            {
                var picker = Z.Object<Sprite>(map.subject?.AsSprite,
                    "Pick a Sprite subject. Other subject kinds (a Cartographer clump, a level) are handed " +
                    "over from their own tool — this window never references them.",
                    PickSprite, 170f);
                col.Add(Z.Field("Sprite", "The sprite this map explains, when the subject is a plain sprite.", picker));
            }

            col.Add(Z.Field("Space", "Pixels from the sprite rect's bottom-left, or (fractional) cells from " +
                                     "the subject's origin. Set at creation from the subject kind.",
                Z.Text(d != null ? d.space.ToString() : "-", ZuiText.Body, "This map's coordinate space.")));

            col.Add(Z.Field("Size", "The subject's footprint in MAP UNITS at authoring time — the drift guard, " +
                                    "and the native resolution a Mask layer paints at.",
                Z.Text(d != null ? $"{d.refSize.x} × {d.refSize.y}" : "-", ZuiText.Body, "Authored footprint.")));

            col.Add(Z.Field("Frames", "1 is a static map — the ordinary case, and not a degenerate animation. " +
                                      "More than one aligns to the owner's frame sequence.",
                Z.Text(d != null ? d.frameCount.ToString() : "-", ZuiText.Body, "Frame count of this map.")));

            // Reserved, always — BOTH lines. A drift notice that grew from zero height would shove everything
            // under it, and this is the one notice that appears exactly when the author is least expecting it.
            driftLabel = (Label)Z.Text("", ZuiText.Subtle,
                "Whether the subject still has the shape this map was authored against.");
            driftLabel.style.whiteSpace = WhiteSpace.NoWrap;
            driftLabel.style.overflow = Overflow.Hidden;
            driftLabel.style.flexShrink = 1f;
            driftLabel.style.minWidth = 0f;
            driftLabel.style.height = 18f;
            col.Add(driftLabel);

            reanchorButton = Z.Button("Re-anchor",
                "Move every mark and every painted cell WITH the art, so each one still describes the same " +
                "part of the subject it was placed on. The usual answer: the subject changed shape, the " +
                "meaning of what you authored did not.",
                () => ResolveDrift(reanchor: true));
            reanchorButton.style.width = 78f;

            keepCoordsButton = Z.Button("Keep coords",
                "Leave every authored coordinate exactly as it is and simply accept the subject's new " +
                "footprint. Choose this when YOU re-authored the geometry to match the numbers — the art may " +
                "well end up somewhere else relative to them, which is then what you meant.",
                () => ResolveDrift(reanchor: false));
            keepCoordsButton.style.width = 86f;

            driftButton = Z.Button("Rescale", "Rescale every mark proportionally and resample every mask by " +
                                              "nearest neighbour onto the subject's new size, then adopt it.",
                RescaleToSubject);
            driftButton.style.width = 62f;
            // 78 + 86 + 62 = 226px: the three fit inside the left pane's 240px MINIMUM, not just its 300px
            // default, so the row cannot start scrolling horizontally when the split is dragged narrow.

            driftRow = Z.Row(reanchorButton, keepCoordsButton, driftButton);
            driftRow.style.minHeight = 22f;
            driftRow.style.height = 22f;
            driftRow.style.flexWrap = Wrap.NoWrap;
            col.Add(driftRow);
            RefreshDriftRow();

            return Z.BoxKeyed("Subject", "The visual this map is anchored to, and the coordinate contract " +
                                         "between them.", "metamapper.subject", col);
        }

        void PickSprite(Sprite s)
        {
            if (map == null) return;
            Undo.RecordObject(map, "Set meta map subject");
            map.subject ??= new MetaSubjectRef();
            map.subject.kind = s != null ? MetaSubjectKind.Sprite : MetaSubjectKind.None;
            map.subject.subject = s;
            map.subject.key = "";
            // The space follows the subject kind — it is never hand-edited, and a sprite is always pixels.
            if (s != null && map.Data.LayerCount == 0) map.Data.space = MapSpace.SpritePixels;
            EditorUtility.SetDirty(map);
            SetMap(map);
        }

        internal void RefreshDriftRow()
        {
            if (driftRow == null) return;
            var d = Data;
            // A placeholder canvas cannot report drift: it was invented from the map, so it can only ever
            // agree with it, and saying "matches" about art nobody could load would be a lie.
            bool blind = visual == null || visual.isPlaceholder;
            var shape = blind ? null : visual.drift;
            bool resized = !blind && d != null && d.HasDrift(visual.refSize);

            if (shape != null)
            {
                // The host's own sentence, plus what it costs — an author deciding between two irreversible-
                // feeling buttons should not have to discover afterwards that one of them dropped something.
                string cost = d != null && shape.WouldLoseContent(d)
                    ? " Re-anchoring pushes some of it outside the new footprint."
                    : "";
                driftLabel.text = shape.what + cost;
            }
            else if (resized)
                driftLabel.text = $"Subject is now {visual.refSize.x}×{visual.refSize.y} — authored at " +
                                  $"{d.refSize.x}×{d.refSize.y}.";
            else driftLabel.text = blind ? "No subject art — layers still work." : "Subject shape matches.";
            driftLabel.tooltip = shape != null
                ? "The subject changed shape after this map was authored, so the marks and masks now describe " +
                  "different parts of it. NOTHING has been moved — pick which answer you want."
                : "Whether the subject still has the shape this map was authored against.";

            Vis(reanchorButton, shape != null);
            Vis(keepCoordsButton, shape != null);
            Vis(driftButton, shape == null && resized);
        }

        /// The author's answer to reported drift. Both branches are ONE undoable edit that also clears the
        /// host's drift baseline — otherwise the same notice returns on every open and can never be dismissed.
        void ResolveDrift(bool reanchor)
        {
            var d = Data;
            var dr = visual?.drift;
            if (d == null || dr == null || session == null) return;

            session.Edit(reanchor ? "Re-anchor meta map" : "Adopt new footprint", () =>
            {
                if (reanchor) d.Reanchor(dr.newRefSize, dr.newFootprintMin, dr.artShift);
                else d.AdoptFootprint(dr.newRefSize, dr.newFootprintMin);
                dr.onResolved?.Invoke();
            });

            string msg = reanchor
                ? $"Re-anchored — everything authored moved with the art by ({dr.artShift.x:0.##}, " +
                  $"{dr.artShift.y:0.##}) and still describes the same part of it."
                : $"Kept every coordinate as authored; the map now describes a {dr.newRefSize.x}×" +
                  $"{dr.newRefSize.y} footprint.";

            // The picture AND its drift report are both stale the moment the map changes, so re-resolve the
            // subject rather than patching the old visual — that is what makes the notice actually go away.
            ReopenSubject();
            Flash(msg);
        }

        void RescaleToSubject()
        {
            var d = Data;
            if (d == null || visual == null) return;
            var to = visual.refSize;
            session.Edit("Rescale meta map", () => d.RescaleTo(to));
            AfterEdit();
            Flash($"Rescaled every mark and mask to {to.x}×{to.y}.");
        }

        // ── adding layers — the shortest useful path in this whole window ───────────

        VisualElement BuildAddLayerRows()
        {
            var col = new VisualElement();

            var nameField = Z.TextInput(newLayerId,
                "The layer's ID — the key the reading code looks up, compared case-insensitively. Name it " +
                "after the MEANING (\"LootShelf\", \"muzzle\"), not after what you plan to draw on it.",
                v => newLayerId = v, 132f);
            nameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                AddLayer();
                e.StopPropagation();
            });

            col.Add(Z.Row(
                Z.Field("New layer", "Name a new layer and add it. Adding the layer is the whole job for an " +
                                     "identity question — no marks required.", nameField),
                Z.Button("+ Add layer", "Add a layer with this id and kind right now.", AddLayer)));

            col.Add(Z.Row(
                Z.Field("Kind", "FIXED at creation: changing a layer's kind once content is authored invites " +
                                "nonsense, so delete and recreate instead. Points = dots. Directions = a dot " +
                                "that also aims. Mask = a painted area. Shapes = rectangles and circles.",
                    Z.MiniRadio(Mathf.Clamp(newLayerKind, 0, KindNames.Length - 1), KindNames,
                        "Points = dots · Directions = dots that aim · Mask = a painted area · Shapes = " +
                        "rectangles and circles (collision footprints, foot circles).",
                        i => newLayerKind = i, wrap: true))));

            // The ids game code has DECLARED it reads, offered as one-click choices: a name is typed once, where
            // it is declared, and picked everywhere else. Refreshed with the layer list, so an id disappears from
            // here the moment this map carries it.
            declaredHost = new VisualElement();
            col.Add(declaredHost);
            return col;
        }

        void RefreshDeclared()
        {
            if (declaredHost == null) return;
            declaredHost.Clear();
            var d = Data;
            if (d == null) return;

            var flow = new VisualElement();
            flow.style.flexDirection = FlexDirection.Row;
            flow.style.flexWrap = Wrap.Wrap;
            foreach (var decl in MetaLayerRegistry.All)
            {
                if (d.HasLayer(decl.id)) continue;
                var dc = decl;
                string what = string.IsNullOrEmpty(dc.description) ? "" : " " + dc.description;
                flow.Add(Z.Button(dc.id,
                    $"Add the layer '{dc.id}' as {dc.kind} — the kind its reader expects.{what} (Declared by " +
                    $"{dc.declaredBy}.)",
                    () => AddLayerAs(dc.id, dc.kind)));
            }
            if (flow.childCount == 0) return;
            declaredHost.Add(Z.Field("Declared", "Layer ids game code says it reads ([MetaLayerId] or " +
                                                 "MetaLayerRegistry.Declare). Click one to add it with the " +
                                                 "right id and kind — nothing to type, nothing to misspell.",
                flow));
        }

        void AddLayer()
        {
            var kind = (LayerKind)Mathf.Clamp(newLayerKind, 0, KindNames.Length - 1);
            AddLayerAs(newLayerId, kind);
        }

        void AddLayerAs(string id, LayerKind kind)
        {
            var d = Data;
            if (d == null) return;
            MetaMapLayer added = null;
            session.Edit("Add meta layer", () => added = d.AddLayer(id, kind));
            activeLayerIndex = d.LayerCount - 1;
            stage?.ClearSelection();
            RebuildLayers();
            RebuildToolRow();
            stage?.Refresh();
            UpdateStatus();
            if (added != null)
                Flash($"Added layer '{added.id}' ({kind}, {added.binding}). " +
                      $"HasLayer(\"{added.id}\") now answers true — that alone may be all you need.");
        }

        // ── the layer list ──────────────────────────────────────────────────────────

        internal void RebuildLayers()
        {
            if (layersBody == null) return;
            layersBody.Clear();
            layerCards.Clear();
            RefreshDeclared();

            var d = Data;
            if (d == null || d.LayerCount == 0)
            {
                layersBody.Add(Z.Text("No layers yet.", ZuiText.Subtle,
                    "Name one above and hit + Add layer."));
                return;
            }

            for (int i = 0; i < d.layers.Count; i++)
            {
                var L = d.layers[i];
                if (L == null) continue;
                var card = LayerCard(L, i);
                layerCards.Add(card);
                layersBody.Add(card);
            }
        }

        VisualElement LayerCard(MetaMapLayer L, int index)
        {
            bool isActive = index == Mathf.Clamp(activeLayerIndex, 0, Mathf.Max(0, Data.LayerCount - 1));

            var card = new VisualElement();
            card.style.marginBottom = 3f;
            card.style.paddingLeft = 5f;
            card.style.paddingTop = 2f;
            card.style.paddingBottom = 2f;
            card.style.borderLeftWidth = 3f;
            card.style.borderLeftColor = isActive ? ActiveAccent : new Color(0f, 0f, 0f, 0f);
            card.tooltip = $"Layer '{L.id}'. Click anywhere on it to make it the active layer — the one the " +
                           "canvas edits.";

            // Row A — identity: colour, name, visibility, remove. Never wraps: a flexible gap in a wrapping
            // row pushes the × onto a line of its own, which is the "confusing empty space" bug every time.
            var swatch = Z.Color(L.color,
                "The layer's one display colour. A mask cell's 0–10 value ramps this colour's BRIGHTNESS " +
                "(MetaPalette.CellColor); it never picks a different hue.",
                c => { session.Edit("Layer colour", () => L.color = c); stage?.Refresh(); }, 44f, false);

            var nameField = Z.TextInput(L.id,
                "The layer's ID — the semantic key consumers look up, case-insensitively. Renaming it breaks " +
                "any code already asking for the old name.",
                v => RenameLayer(L, v), 116f);

            var eye = Z.Toggle("Show",
                "Draw this layer on the canvas. View state only — it is never written to the asset.",
                IsLayerVisible(L), v => ToggleVisible(L, v));
            eye.style.width = 48f;

            var remove = Z.Button("×", $"Delete the layer '{L.id}' and everything authored on it.",
                () => RemoveLayer(L));
            remove.style.width = 22f;

            var rowA = Z.Row(swatch, nameField, eye, remove);
            rowA.style.flexWrap = Wrap.NoWrap;

            // Row B — the Amendment's requirement: the binding state reads AT A GLANCE, on the row.
            var kindLabel = (Label)Z.Text(L.kind.ToString(), ZuiText.Subtle,
                "What this layer stores. Fixed at creation — delete and recreate to change it.");
            kindLabel.style.width = 66f;

            var binding = Z.Segmented(L.binding == FrameBinding.PerFrame ? 1 : 0,
                new[] { "Uniform", "Per frame" },
                "AN AUTHORED CHOICE, never inferred. Uniform = one entry serves every frame (the deliberate " +
                "\"true on every frame\" opt-in). Per frame = one entry per frame, and an EMPTY frame is a " +
                "real authored answer. Nothing here ever collapses or promotes a layer on its own.",
                i => SetBinding(L, i == 1));
            binding.style.width = 126f;

            // A QUIET hint, never an error: a map may carry any id at all. The slot is always there (fixed
            // width) so a layer becoming declared or undeclared never shifts its row.
            MetaLayerRegistry.TryGet(L.id, out var decl);
            string mark = decl == null ? "?" : decl.kind != L.kind ? "⚠" : "";
            var declared = (Label)Z.Text(mark, ZuiText.Subtle,
                decl == null
                    ? $"No code declares the id '{L.id}', so nothing may read this layer yet — or its reader has " +
                      "not declared it with [MetaLayerId]. A hint, not an error."
                    : decl.kind != L.kind
                        ? $"'{L.id}' is declared by {decl.declaredBy} as a {decl.kind} layer, but this one stores " +
                          $"{L.kind}. That reader asks for {decl.kind} and will not find this layer."
                        : $"Read by {decl.declaredBy}" +
                          (string.IsNullOrEmpty(decl.description) ? "." : ": " + decl.description));
            declared.style.width = 16f;
            declared.style.unityTextAlign = TextAnchor.MiddleCenter;

            var rowB = Z.Row(kindLabel, binding, declared);
            rowB.style.flexWrap = Wrap.NoWrap;

            card.Add(rowA);
            card.Add(rowB);

            // Bubble phase, and no rebuild — restyling in place is what keeps the clicked control alive to
            // receive its own event.
            card.RegisterCallback<PointerDownEvent>(_ => SetActiveLayer(index));
            return card;
        }

        void SetActiveLayer(int index)
        {
            if (activeLayerIndex == index) return;
            activeLayerIndex = index;
            for (int i = 0; i < layerCards.Count; i++)
                layerCards[i].style.borderLeftColor = i == index ? ActiveAccent : new Color(0f, 0f, 0f, 0f);
            stage?.ClearSelection();
            RebuildToolRow();
            stage?.Refresh();
            UpdateStatus();
        }

        void RenameLayer(MetaMapLayer L, string wanted)
        {
            wanted = wanted?.Trim();
            if (string.IsNullOrEmpty(wanted) || wanted == L.id) return;
            session.Edit("Rename meta layer", () => L.id = wanted);
            UpdateStatus();
        }

        void ToggleVisible(MetaMapLayer L, bool visible)
        {
            string id = L.id ?? "";
            if (visible) hiddenLayerIds.Remove(id);
            else if (!hiddenLayerIds.Contains(id)) hiddenLayerIds.Add(id);
            stage?.Refresh();
        }

        /// NO CONFIRM DIALOG, deliberately. Deleting a layer is a single undoable edit, and a modal "are you
        /// sure?" over an undoable action buys nothing: it interrupts every deliberate delete to protect
        /// against a mistake Ctrl+Z already fixes, and it makes the action untestable outside a human's hands.
        /// The rule this follows: confirm only what UNDO CANNOT TAKE BACK (deleting a clump's tile assets off
        /// disk does; this does not). What replaces it is the flash line saying what went and how to get it
        /// back — an answer AFTER the fact instead of a question before it.
        void RemoveLayer(MetaMapLayer L)
        {
            var d = Data;
            if (d == null || L == null) return;
            string id = L.id;
            bool hadContent = LayerHasAnyContent(L);

            session.Edit("Delete meta layer", () => d.layers.Remove(L));
            hiddenLayerIds.Remove(id ?? "");     // view state for a layer that no longer exists
            activeLayerIndex = Mathf.Clamp(activeLayerIndex, 0, Mathf.Max(0, d.LayerCount - 1));
            stage?.ClearSelection();
            RebuildLayers();
            RebuildToolRow();
            stage?.Refresh();
            UpdateStatus();
            Flash(hadContent
                ? $"Deleted layer '{id}' and everything authored on it. Ctrl+Z brings it back."
                : $"Deleted layer '{id}'. Ctrl+Z brings it back.");
        }

        static bool LayerHasAnyContent(MetaMapLayer L)
        {
            if (L == null) return false;
            if (L.uniform != null && !L.uniform.IsEmpty) return true;
            if (L.track != null)
                for (int i = 0; i < L.track.Count; i++)
                    if (L.track[i] != null && !L.track[i].IsEmpty) return true;
            return false;
        }

        // ── the binding toggle: explicit, warned, never inferred ────────────────────

        void SetBinding(MetaMapLayer L, bool perFrame)
        {
            var d = Data;
            if (d == null) return;
            var want = perFrame ? FrameBinding.PerFrame : FrameBinding.Uniform;
            if (L.binding == want) return;

            if (perFrame)
            {
                // LOSSLESS by construction: the one entry is copied to every frame, so the layer says exactly
                // what it said before. No warning is owed for an expansion that cannot lose anything.
                session.Edit("Expand layer to per-frame", () => L.ExpandToPerFrame(d.frameCount));
                Flash($"'{L.id}' is per-frame now — its entry was copied to all {d.frameCount} frame(s); " +
                      "nothing was lost. Empty frames stay empty on purpose.");
            }
            else
            {
                if (!TryCollapse(L, d)) { RebuildLayers(); return; }   // cancelled: put the segments back
            }

            RebuildLayers();
            RebuildToolRow();
            stage?.Refresh();
            UpdateStatus();
        }

        /// PerFrame → Uniform is a DESTRUCTIVE COLLAPSE: every frame but one is discarded. So the author is
        /// warned and must NAME the frame that wins — the system never guesses, and never collapses a track
        /// just because its entries currently look identical (Amendment 2026-08-03 (3)).
        bool TryCollapse(MetaMapLayer L, MetaMapData d)
        {
            var authored = new List<int>();
            if (L.track != null)
                for (int i = 0; i < L.track.Count; i++)
                    if (L.track[i] != null && !L.track[i].IsEmpty) authored.Add(i);

            int cur = Frame;
            if (authored.Count == 0)
            {
                // Nothing authored anywhere — there is no data to lose and therefore nothing to warn about.
                session.Edit("Collapse layer to uniform", () => L.CollapseToUniform(cur));
                Flash($"'{L.id}' is uniform now — one entry for every frame. It was empty, so nothing was lost.");
                return true;
            }

            int first = authored[0];
            string authoredList = string.Join(", ", authored.ConvertAll(i => (i + 1).ToString()));
            string message =
                $"'{L.id}' is PER-FRAME with content on frame(s) {authoredList}.\n\n" +
                "Collapsing to Uniform keeps ONE frame's content and DISCARDS the rest — a Uniform layer " +
                "stores a single entry that serves every frame.\n\nWhich frame wins?";

            if (authored.Count == 1 && first == cur)
            {
                if (!EditorUtility.DisplayDialog("Collapse to Uniform",
                        $"'{L.id}' is PER-FRAME with content on frame {first + 1} only.\n\n" +
                        "Collapsing keeps that frame's content and makes it true on EVERY frame — including " +
                        "frames you may have deliberately left empty.", $"Keep frame {first + 1}", "Cancel"))
                    return false;
                session.Edit("Collapse layer to uniform", () => L.CollapseToUniform(first));
                Flash($"'{L.id}' is uniform now — frame {first + 1}'s content serves every frame.");
                return true;
            }

            int choice = EditorUtility.DisplayDialogComplex("Collapse to Uniform", message,
                $"Keep frame {cur + 1}" + (IsAuthored(L, cur) ? "" : " (empty)"),
                "Cancel",
                $"Keep frame {first + 1}");
            int winner;
            if (choice == 1) return false;
            winner = choice == 0 ? cur : first;

            session.Edit("Collapse layer to uniform", () => L.CollapseToUniform(winner));
            Flash($"'{L.id}' is uniform now — frame {winner + 1} won; the other authored frame(s) were discarded.");
            return true;
        }

        static bool IsAuthored(MetaMapLayer L, int frame)
            => L.track != null && frame >= 0 && frame < L.track.Count
               && L.track[frame] != null && !L.track[frame].IsEmpty;
    }
}
