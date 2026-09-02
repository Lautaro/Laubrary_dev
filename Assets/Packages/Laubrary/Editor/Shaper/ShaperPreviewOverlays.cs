// ShaperPreviewOverlays — the host half of Shaper's preview-overlay capability (see
// Runtime/Shaper/ShaperPreviewOverlay.cs for the interface and the reasoning).
//
// It collects the features in a DOCUMENT that want to mark something on the preview, builds one toggle strip
// for them, and draws the ones that are switched on. It knows nothing about swarms, or about any particular
// feature: it walks the document for IShaperPreviewOverlay and asks each instance whether it currently has
// anything to show. A second feature that wants marks implements the interface and appears here with no edit
// to this file and none to the window — which is the whole point of the interface existing.
//
// ── Two rules taken verbatim from authoring.md §15 ─────────────────────────────────────────────────────────
//   • The strip is a PERMANENTLY RESERVED row whose CONTENTS change — never added or removed. A row that
//     appears when a swarm is enabled and vanishes when it is disabled reflows the preview above it under the
//     user's cursor, which is exactly the jitter the stable-workspace rule exists to prevent. It keeps a
//     minimum height and an explaining tooltip when there is nothing to offer.
//   • A toggle's on/off state is keyed on the INSTANCE, never on its label and ordinal. An ordinal is a
//     position among the entries that currently qualify, and that set is recomputed on every collect, so a
//     flag remembered under "Swarm spawns 2" silently changes owner the moment anything renumbers.
//     ConditionalWeakTable is what keys on the instance without keeping a dead document's nodes alive; the
//     cost is that a choice does not survive a domain reload, which is the right trade.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    /// <summary>
    /// Collects a document's preview overlays, builds their toggle strip, and draws the ones switched on.
    /// See <see cref="IShaperPreviewOverlay"/> for how a feature opts in.
    /// </summary>
    internal static class ShaperPreviewOverlays
    {
        /// One collected overlay: the feature, its display label (disambiguated when several share one), and
        /// where the node it belongs to sits in canvas units.
        internal struct Entry
        {
            public IShaperPreviewOverlay Overlay;
            public string Label;
            public string Tooltip;
            public Vector2 Origin;
        }

        sealed class Flag { public bool on; }

        // Keyed on the overlay INSTANCE (see this file's header). A ConditionalWeakTable does not root its
        // keys, so a document the user browsed away from is collectable with its flags.
        static readonly ConditionalWeakTable<object, Flag> Flags = new ConditionalWeakTable<object, Flag>();

        internal static bool IsOn(IShaperPreviewOverlay o)
            => o != null && Flags.TryGetValue(o, out var f) && f.on;

        internal static void SetOn(IShaperPreviewOverlay o, bool on)
        {
            if (o == null) return;
            Flags.GetOrCreateValue(o).on = on;
        }

        /// <summary>Every overlay in <paramref name="doc"/> that has something to show right now, in layer
        /// then tree order. An entry per INSTANCE, so two swarmed nodes get one toggle each.</summary>
        internal static List<Entry> Collect(ShaperDocument doc, float phase01)
        {
            var found = new List<Entry>();
            if (doc?.layers == null) return found;

            foreach (var layer in doc.layers)
            {
                // A disabled layer paints nothing, so marking where its swarm would have gone would point at
                // a picture that is not there.
                if (layer == null || !layer.enabled || layer.root == null) continue;
                Walk(layer.root, ShaperMatrix.Identity, doc, phase01, found);
            }

            // Disambiguate repeats the same way SpriteFx does: a bare label plus an ordinal, with the tooltip
            // carrying the unambiguous version, because an ordinal on its own invites "is that the 2nd node?".
            var counts = new Dictionary<string, int>();
            foreach (var e in found)
            {
                counts.TryGetValue(e.Label, out int c);
                counts[e.Label] = c + 1;
            }
            var seen = new Dictionary<string, int>();
            for (int i = 0; i < found.Count; i++)
            {
                var e = found[i];
                if (counts[e.Label] <= 1) continue;
                seen.TryGetValue(e.Label, out int n);
                seen[e.Label] = ++n;
                e.Tooltip = e.Tooltip + $"\n\nThis is overlay {n} of {counts[e.Label]} with this name — one per "
                    + "node that has something to mark.";
                e.Label = e.Label + " " + n;
                found[i] = e;
            }
            return found;
        }

        static void Walk(ShaperNode node, in ShaperMatrix parent, ShaperDocument doc, float phase01,
                         List<Entry> into)
        {
            if (node == null || !node.enabled) return;

            var here = ShaperMatrix.Mul(parent,
                (node.transform ?? new ShaperTransformBlock()).ToMatrix(phase01, doc.seed));

            // Every overlay-capable thing hanging off this node. Today that is the swarm; the loop is written
            // over a list so the next one costs a line here and nothing anywhere else.
            AddIfWanted(node.swarm, here, into);

            if (node.children == null) return;
            foreach (var c in node.children) Walk(c, here, doc, phase01, into);
        }

        static void AddIfWanted(object candidate, in ShaperMatrix forward, List<Entry> into)
        {
            if (!(candidate is IShaperPreviewOverlay o) || !o.WantsPreviewOverlay) return;
            into.Add(new Entry
            {
                Overlay = o,
                Label = o.OverlayLabel ?? "Overlay",
                Tooltip = o.OverlayTooltip ?? "",
                // Applying the node's own forward map to (0,0) is just its translation column — where this
                // node's content is centred on the canvas right now.
                Origin = new Vector2(forward.m02, forward.m12),
            });
        }

        /// <summary>Draw every switched-on overlay into <paramref name="pixels"/>. The caller owns the buffer
        /// and must NOT hand in one the frame cache holds — see ShaperPreviewStage, which copies first.</summary>
        internal static void Draw(List<Entry> entries, Color32[] pixels, int w, int h, ShaperDocument doc,
                                  float phase01)
        {
            if (entries == null || entries.Count == 0 || pixels == null || doc == null) return;
            var canvas = new ShaperOverlayCanvas();
            canvas.Set(pixels, w, h, doc.pixelSize, phase01, doc.seed);
            foreach (var e in entries)
            {
                if (!IsOn(e.Overlay)) continue;
                canvas.origin = e.Origin;
                try { e.Overlay.DrawPreviewOverlay(canvas); }
                // One broken overlay must cost its own marks, never the picture: this runs inside the preview's
                // own refresh, and an exception here would leave the stage without an image at all.
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        /// <summary>Fill a reserved strip host with one segment per collected overlay. Call on every rebuild
        /// AND whenever the set can have changed; the host element itself is created once and kept.</summary>
        internal static void FillStrip(VisualElement host, List<Entry> entries, Action onChanged)
        {
            if (host == null) return;
            host.Clear();
            if (entries == null || entries.Count == 0)
            {
                // The row STAYS, with an explanation — see this file's header for why it is not removed.
                var empty = Z.Text("", ZuiText.Body,
                    "Nothing on this document has marks to show on the preview yet. A feature that can mark "
                    + "something — a swarm's spawn figure and placements, say — puts its own switch here when "
                    + "it is set up enough to have something to draw.");
                host.Add(empty);
                return;
            }

            var labels = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++) labels[i] = entries[i].Label;
            var snapshot = entries;

            // One joined multi-select for one set of view options over one picture (ui-layout-rules, "Control
            // choice") — never a row of separate checkboxes.
            var strip = Z.SegmentedMulti(
                i => i >= 0 && i < snapshot.Count && IsOn(snapshot[i].Overlay),
                labels,
                "Extra marks drawn over the preview. Preview only — nothing switched on here reaches a bake.",
                (i, on) =>
                {
                    if (i < 0 || i >= snapshot.Count) return;
                    SetOn(snapshot[i].Overlay, on);
                    onChanged?.Invoke();
                });
            host.Add(strip);

            // Each segment's tooltip is composed for the state it is IN, or it tells the reader the opposite
            // of what clicking will do.
            for (int i = 0; i < snapshot.Count; i++)
            {
                var seg = strip.SegmentAt(i);
                if (seg == null) continue;
                seg.tooltip = snapshot[i].Tooltip + (IsOn(snapshot[i].Overlay)
                    ? "\n\nShowing — click to hide."
                    : "\n\nHidden — click to show.");
            }
        }
    }
}
