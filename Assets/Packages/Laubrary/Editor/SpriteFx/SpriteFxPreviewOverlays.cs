// SpriteFxPreviewOverlays.cs
// The host-side half of the preview-overlay registration pattern (the effect-side half is
// Runtime/SpriteFx/SpriteFxPreviewOverlay.cs). Given a stack, it answers three questions a window should not have
// to answer per effect: which effects want to draw on the preview right now, what toggle each of them gets, and
// which of those toggles are on.
//
// Deliberately a static class over a plain List<PyreModifier> and a plain Color32[] — it knows nothing about
// SpriteFxStackWindow, or about any window at all. ANY surface with a SpriteFx preview canvas (a Zoe event's
// inline preview, Chunks, a future combined stage) reserves one strip, keeps it, and calls Draw after it renders.
// The alternative — each window growing its own row of per-effect toggles — is exactly the hardcoding that made
// "Light radius" a permanent fixture of this window's transport row even for stacks with no light in them.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Laubrary.SpriteFx;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.SpriteFx.Editor
{
    /// <summary>
    /// Collects the effects in a stack that want to draw on the preview, builds the toggle strip for them, and
    /// draws the ones that are switched on. See <see cref="ISpriteFxPreviewOverlay"/> for how an effect opts in.
    /// </summary>
    public static class SpriteFxPreviewOverlays
    {
        /// One collected overlay: the effect, plus everything the host needs that the effect itself cannot know —
        /// where it sits in the stack, whether its label had to be disambiguated, and where its on/off flag lives.
        public sealed class Entry
        {
            /// The effect this toggle is identified by — and, when the type grouped its instances, the first of
            /// them. Always one of <see cref="Members"/>.
            public ISpriteFxPreviewOverlay Overlay;
            /// Everything this ONE toggle draws. A single item unless the type returned a shared
            /// <see cref="ISpriteFxPreviewOverlay.OverlayGroupKey"/>, in which case every instance sharing it.
            public List<ISpriteFxPreviewOverlay> Members = new List<ISpriteFxPreviewOverlay>();
            /// The label actually shown, after disambiguation — the bare OverlayLabel, or it plus an ordinal.
            public string Label;
            /// The tooltip actually shown — the effect's own, plus an identifying sentence when disambiguated.
            public string Tooltip;
            /// 1-based position in the stack (counting every modifier, not just the overlay-capable ones), so a
            /// disambiguating tooltip can point at the card the user is looking at.
            public int StackPosition;
            /// 1-based ordinal among the collected overlays SHARING this label — the "2" in "Light radius 2".
            public int Ordinal;
        }

        // ── collection ──────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// The overlays a stack currently offers, in stack order: every modifier that is non-null, ENABLED,
        /// implements <see cref="ISpriteFxPreviewOverlay"/>, and says it has something to show right now.
        /// </summary>
        ///
        /// The `enabled` half of that test is the host's on purpose — an effect that is switched off contributes
        /// nothing to the picture, so a marker for it would point at shading that is not there. Leaving it to each
        /// effect would mean every implementer re-deriving the same answer and one of them eventually forgetting.
        ///
        public static List<Entry> Collect(IList<PyreModifier> mods)
        {
            var list = new List<Entry>();
            if (mods == null) return list;

            // Pass 1: who qualifies. An instance whose type returned a group key joins the entry already opened
            // for that key instead of opening its own — that is the type answering "what does several of me
            // mean", and it is the only place the answer is allowed to come from.
            var groups = new Dictionary<string, Entry>();
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m == null || !m.enabled) continue;
                if (!(m is ISpriteFxPreviewOverlay ov) || !ov.WantsPreviewOverlay) continue;

                string label = string.IsNullOrEmpty(ov.OverlayLabel) ? m.DisplayName : ov.OverlayLabel;
                string group = ov.OverlayGroupKey;

                if (!string.IsNullOrEmpty(group) && groups.TryGetValue(group, out var open))
                {
                    open.Members.Add(ov);
                    continue;
                }

                var e = new Entry
                {
                    Overlay = ov,
                    Label = label,                       // resolved in pass 2, once the totals are known
                    Tooltip = ov.OverlayTooltip,
                    StackPosition = i + 1,
                    Ordinal = 0,
                };
                e.Members.Add(ov);
                list.Add(e);
                if (!string.IsNullOrEmpty(group)) groups[group] = e;
            }

            // Pass 2: ordinals and display labels. The ordinal counts TOGGLES sharing a label, not raw stack
            // positions: "Light radius 2" has to mean "the second light", which is what the user counts on
            // screen. Numbering by stack index instead would produce "Light radius 5" beside a stack whose two
            // lights are cards 2 and 5, and the label would then be naming something nobody can see. A lone
            // "Light radius" keeps its bare label — a "1" on a set of one reads as if a second exists off screen.
            var counts = new Dictionary<string, int>();
            foreach (var e in list)
            {
                counts.TryGetValue(e.Label, out int n);
                counts[e.Label] = n + 1;
            }

            var seen = new Dictionary<string, int>();
            foreach (var e in list)
            {
                string bare = e.Label;
                seen.TryGetValue(bare, out int n);
                n++;
                seen[bare] = n;
                e.Ordinal = n;

                if (counts[bare] > 1)
                {
                    // Compact — a parenthetical would double the strip's width. The tooltip carries the
                    // unambiguous version, naming BOTH numbers, because on its own an ordinal invites the wrong
                    // reading ("is that the 2nd effect in the stack?").
                    e.Label = bare + " " + n;
                    e.Tooltip = Join(e.Tooltip,
                        $"This toggle is the {Ordinal(n)} {NameOf(e.Overlay, bare)} effect in this stack, " +
                        $"at stack position {e.StackPosition}.");
                }
                else if (e.Members.Count > 1)
                {
                    e.Tooltip = Join(e.Tooltip,
                        $"One toggle for all {e.Members.Count} {NameOf(e.Overlay, bare)} effects in this stack — " +
                        "this effect asks for its instances to be shown together.");
                }
            }

            return list;
        }

        static string Join(string a, string b)
            => string.IsNullOrEmpty(a) ? b : a.TrimEnd() + " " + b;

        /// The effect's own DisplayName when we can reach it, so the tooltip says "the 2nd Fake light effect"
        /// rather than repeating the overlay's label back at the reader.
        static string NameOf(ISpriteFxPreviewOverlay ov, string fallback)
            => ov is PyreModifier m && !string.IsNullOrEmpty(m.DisplayName) ? m.DisplayName : fallback;

        static string Ordinal(int n)
        {
            int mod100 = n % 100;
            if (mod100 >= 11 && mod100 <= 13) return n + "th";
            switch (n % 10)
            {
                case 1: return n + "st";
                case 2: return n + "nd";
                case 3: return n + "rd";
                default: return n + "th";
            }
        }

        // ── on/off state ────────────────────────────────────────────────────────────────────────────────────
        // Keyed on the EFFECT INSTANCE, never on its label and ordinal. An ordinal is a position among the
        // overlays that currently QUALIFY, and that set is recomputed on every collect — so a remembered flag
        // stored under one would silently change owner. Three Point lights, the third switched on; disable the
        // middle one and the third becomes ordinal 2, inherits whatever ordinal 2 was set to, and its ring
        // vanishes — or a ring nobody asked for appears on a light the user never touched. Reordering, deleting
        // and flipping an earlier light to Directional all do the same. A label+ordinal is a POSITION, and a
        // position is not an identity.
        //
        // This is the UI guide's own prescription for pure view state: key it per instance (a side table on the
        // object) so it survives window rebuilds and never drifts on reorder. The price is that the choice does
        // not outlive a domain reload, which is the right trade — a diagnostic that has to be switched back on
        // after a recompile is a far smaller problem than one that quietly points at the wrong light. It also
        // ends the cross-asset bleed the pref key had: two unrelated stacks each holding a Fake Light no longer
        // share one flag, because they are not the same object.
        //
        // A ConditionalWeakTable, so a modifier that is deleted takes its flag with it instead of pinning the
        // managed object alive for the session.
        sealed class Flag { public bool On; }
        static readonly ConditionalWeakTable<object, Flag> s_shown = new ConditionalWeakTable<object, Flag>();

        /// Default FALSE, always. An overlay is a diagnostic drawn ON TOP of the thing being judged — the preview
        /// has to show what the game will show unless the author has explicitly asked for more.
        public static bool IsShown(Entry e)
            => e?.Overlay != null && s_shown.TryGetValue(e.Overlay, out var f) && f.On;

        public static void SetShown(Entry e, bool on)
        {
            if (e?.Overlay == null) return;
            if (!s_shown.TryGetValue(e.Overlay, out var f)) { f = new Flag(); s_shown.Add(e.Overlay, f); }
            f.On = on;
        }

        // ── the toggle strip ────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// The reserved row that carries one toggle per collected overlay. Build it ONCE, add it to your layout,
        /// keep it, and call <see cref="FillStrip"/> when the stack changes.
        /// </summary>
        ///
        /// STABLE WORKSPACE. This row exists whether or not there is anything in it, and it always holds at least
        /// its one-row height. That is not tidiness: a row that appears when a light is added and vanishes when it
        /// is disabled reflows everything around it, and the control the user was reaching for moves out from
        /// under the cursor mid-gesture — the exact failure the UI guide's stable-workspace rule was written for.
        /// Only the CONTENTS ever change.
        ///
        /// It is a MINIMUM height, not a fixed one, and the row may shrink horizontally. A fixed height paired
        /// with "never wraps, never shrinks" looks like the stricter reading of the rule and is actually the
        /// dangerous one: enough overlays and the row becomes an unshrinkable several-hundred-pixel control
        /// inside a column that has been told it may shrink to nothing, so it forces a horizontal scrollbar or
        /// wraps the whole column out from beside the preview stage — reflowing far more than the row ever could.
        /// Bounded width and a floor on height is the combination that actually keeps the workspace still.
        ///
        /// Put it LAST in its column, so the one direction it CAN grow has nothing after it to push.
        public static VisualElement BuildStrip(List<Entry> entries, Action onChanged)
        {
            var strip = new VisualElement();
            strip.AddToClassList("lau-tool-shell__overlay-strip");
            FillStrip(strip, entries, onChanged);
            return strip;
        }

        /// Refill an already-built strip in place — the counterpart to BuildStrip's "add it once and keep it".
        public static void FillStrip(VisualElement strip, List<Entry> entries, Action onChanged)
        {
            if (strip == null) return;
            strip.Clear();
            if (entries == null || entries.Count == 0)
            {
                // Nothing to offer. The row stays, empty, holding its height. Its tooltip is the only thing that
                // can explain an empty reserved space, so it says why it is empty rather than leaving a mystery gap.
                strip.tooltip = "Preview overlays appear here when the stack holds an effect with something to " +
                                "mark on the picture — a Fake Light's position and radius, for instance.";
                return;
            }

            var labels = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++) labels[i] = entries[i].Label;

            // A segmented multi-select, not a row of loose toggles: these are one set of view options over one
            // picture, and a joined multi-select is the control for a flag set (the same reason Pyre's matte
            // channels use it). It takes ONE shared tooltip, which would flatten several distinct explanations
            // into one — so the shared string is a strip-level summary and each segment's OWN tooltip is written
            // onto the produced child afterwards. ZuiSegmented adds its buttons in label order and nothing else,
            // so child i is segment i.
            string shared = StripTooltip(entries);
            ZuiSegmented seg = null;
            seg = Z.SegmentedMulti(
                i => IsShown(entries[i]),
                labels,
                shared,
                (i, on) =>
                {
                    if (i < 0 || i >= entries.Count) return;
                    SetShown(entries[i], on);
                    // The tooltips say which way the toggle will go next, so they are wrong the instant it moves.
                    ApplySegmentTooltips(seg, entries);
                    onChanged?.Invoke();
                });

            // The strip must never be the thing that decides how wide the pane has to be.
            seg.AddToClassList("lau-tool-shell__overlay-segments");

            ApplySegmentTooltips(seg, entries);
            strip.tooltip = shared;
            strip.Add(seg);
        }

        /// Per-segment tooltips, refreshed on every toggle. A tooltip has to read for the state the control is
        /// actually in — "Show the light's radius" hovering over an overlay that is already showing tells the
        /// reader the opposite of what clicking will do.
        static void ApplySegmentTooltips(VisualElement seg, List<Entry> entries)
        {
            if (seg == null || entries == null) return;
            int at = 0;
            foreach (var child in seg.Children())
            {
                if (at >= entries.Count) break;
                child.tooltip = SegmentTooltip(entries[at]);
                at++;
            }
        }

        static string SegmentTooltip(Entry e)
        {
            if (e == null) return null;
            string state = IsShown(e)
                ? "Showing on the preview — click to hide it."
                : "Hidden — click to show it on the preview.";
            return Join(state, e.Tooltip);
        }

        /// One shared line for the strip itself. Names every entry, so hovering the gap between segments still
        /// says what the row is and what is in it.
        static string StripTooltip(List<Entry> entries)
        {
            var sb = new StringBuilder("Diagnostic overlays drawn on top of the preview — never part of the " +
                                       "effect, never baked.");
            if (entries != null)
                for (int i = 0; i < entries.Count; i++)
                    sb.Append("\n\n").Append(entries[i].Label).Append(": ").Append(entries[i].Tooltip);
            return sb.ToString();
        }

        // ── drawing ─────────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Draw every switched-on overlay into <paramref name="buf"/>, straight after the stack has been run into
        /// it. The buffer is the preview's own — including its margin, if the stack needed one — so an effect's
        /// marks land in the same pixel space it was just shaded in.
        /// </summary>
        public static void Draw(List<Entry> entries, Color32[] buf, int width, int height)
        {
            if (entries == null || entries.Count == 0) return;
            if (buf == null || width <= 0 || height <= 0 || buf.Length < width * height) return;

            var canvas = new SpriteFxOverlayCanvas(buf, width, height);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e?.Members == null || !IsShown(e)) continue;
                // Usually one member. Several only when the type asked for its instances to share a toggle, and
                // then all of them draw — each still marking only itself.
                for (int k = 0; k < e.Members.Count; k++)
                    e.Members[k]?.DrawPreviewOverlay(canvas);
            }
        }
    }
}
