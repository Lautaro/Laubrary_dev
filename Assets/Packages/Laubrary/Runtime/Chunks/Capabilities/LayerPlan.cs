using System.Collections.Generic;
using UnityEngine;
using Laubrary.Layering;

namespace Laubrary.Chunks
{
    /// One line of a recipe's Depth list: one thing the recipe draws, at one depth.
    ///
    /// It addresses a capability by its stable <see cref="ChunkCapability.id"/> and, optionally, ONE instance
    /// of it — the third piece a fracture cuts, the second point a pattern places. That per-instance address
    /// is the whole reason the Depth list exists: a named slot could only ever say "all of this card's output
    /// draws here", so nothing could sit strictly between two pieces of one fracture (T-0349 D1, the blocker
    /// UC6 hit).
    [System.Serializable]
    public struct DepthRow
    {
        /// Which capability this row draws. Its id, never its name or its stack position — both of which
        /// change under the author's hands while the row must not.
        public string capabilityId;

        /// Which ONE instance of that capability draws here (0-based: piece 0 reads as "1" on screen), or
        /// <b>-1 for the whole card</b> — every piece, point and particle it produces, on this one row.
        public int instance;

        public DepthRow(string capabilityId, int instance)
        {
            this.capabilityId = capabilityId;
            this.instance = instance;
        }

        /// Whether this row addresses the whole card rather than one instance of it.
        public bool IsWholeCard => instance < 0;
    }

    /// The recipe's DEPTH LIST: one row per thing the recipe draws, ordered back to front. The first row draws
    /// furthest back; reading down the list moves toward the viewer (owner's decision, T-0365 Q2).
    ///
    /// A COORDINATOR: it puts nothing on screen and has no moment, so it never fires and never takes a timing
    /// lane. It contributes the one thing every producer reads — what order the burst's output draws in.
    /// With no Depth list in the recipe every producer simply draws in stack order, which is exactly the
    /// pre-layering behaviour.
    ///
    /// It REPLACED a list of typed slot names that every producer card then picked from by name (the old
    /// "Layer Plan"). Two things were wrong with that and both are fixed here: a name had to be typed once and
    /// picked again on every card that used it (ui-rules §1 — a name is typed where it is DECLARED and picked
    /// everywhere else; that shape managed to be both), and a slot could only address a whole card, so the
    /// owner's own use case — a fragment in front of one blast and behind another, in one burst — could not be
    /// authored at all. The rows fill themselves in from the cards now, so there is no name to type, none to
    /// mistype, and nothing to rename.
    ///
    /// The old named slots are still read ONCE, by <see cref="MigrateRows"/>, to reproduce an existing
    /// recipe's own order as rows. Nothing writes them again.
    [System.Serializable]
    public class LayerPlan : ChunkCapability
    {
        public override string KindName => "Depth";

        /// Nothing is scheduled by a plan; it just is.
        public override bool OccupiesTime => false;

        [Tooltip("What the recipe draws, back to front. The top row draws furthest back; each row below it " +
                 "draws in front of the one above.")]
        public List<DepthRow> rows = new List<DepthRow>();

        /// The sorting LAYER the whole burst lives in, the step between rows, and — until it has been migrated
        /// once — the named slots this plan used to be. Hidden: the rows above are the authored surface now,
        /// and the slot names are migration input, not something to edit.
        [HideInInspector] public LayerSpec layers = new LayerSpec();

        /// Set the one time <see cref="MigrateRows"/> turns the named slots into rows, so it never runs twice
        /// and never re-reads slots the author has since moved past.
        [SerializeField, HideInInspector] bool rowsMigrated;

        // ── reading the plan ──────────────────────────────────────────────────────────────────────────────

        public int RowCount => rows != null ? rows.Count : 0;

        /// sortingOrder distance between consecutive rows. Kept at LayerSpec's own step (10) so there is one
        /// number, not two — a row's own sub-ordering (a splash's particles) lives inside that gap.
        public int Step => layers != null && layers.step > 1 ? layers.step : 10;

        /// The sorting LAYER the burst's renderers go on, or null/empty for "leave it alone".
        public string SortingLayerName => layers != null ? layers.sortingLayerName : null;

        /// Which row draws <paramref name="capabilityId"/>'s instance <paramref name="instance"/>.
        ///
        /// Three answers, in order, and the order is the whole robustness story: the row for exactly that
        /// instance; else the row for the whole card; else ANY row that card has. The third is what keeps a
        /// hand-edited or half-reconciled plan honest — a piece with no row of its own draws with its own
        /// card's other pieces rather than leaping to the very front. -1 only when the card is not in the plan
        /// at all.
        /// <paramref name="exact"/> reports whether the instance itself was addressed, which is what tells the
        /// caller its sub-order has already been spent.
        public int RowIndexFor(string capabilityId, int instance, out bool exact)
        {
            exact = false;
            if (rows == null || string.IsNullOrEmpty(capabilityId)) return -1;
            int wholeCard = -1, any = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.capabilityId != capabilityId) continue;
                if (row.instance == instance && instance >= 0) { exact = true; return i; }
                if (row.IsWholeCard && instance < 0) return i;
                if (row.IsWholeCard && wholeCard < 0) wholeCard = i;
                if (any < 0) any = i;
            }
            return wholeCard >= 0 ? wholeCard : any;
        }

        /// The concrete sortingOrder for one instance of one capability.
        ///
        /// <paramref name="burstOrder"/> is the BURST's own sortingOrder — not zero. That is deliberate and it
        /// is the fix for T-0349 D3: the old slot maths started at LayerSpec.baseOrder (0), so adding a plan
        /// silently dropped the whole effect from 500 down to 0–39, level with the Zoe body that fired it and
        /// behind any game sprite above 0. Depth inside the burst is what this list authors; where the burst
        /// as a whole sits is the emitter's business, and it stays the emitter's business.
        ///
        /// <paramref name="sub"/> sub-orders several renderers that share ONE row (a splash's particles, a
        /// joined pattern's points). It is confined to the row's own gap, so it can never reach the next row
        /// and invert the depth the author dragged into place. It is spent only when the row addresses the
        /// whole card: an instance row already IS that instance's place.
        public int OrderFor(string capabilityId, int instance, int burstOrder, int sub = 0)
        {
            int row = RowIndexFor(capabilityId, instance, out bool exact);
            // Not in the plan at all draws in FRONT of everything that is — the same reading the old
            // "(stack order)" slot had, and what a producer added after the plan was built should do.
            if (row < 0) row = RowCount;
            long order = (long)burstOrder + (long)row * Step + (exact ? 0 : ClampSub(sub));
            return order < short.MinValue ? short.MinValue
                 : order > short.MaxValue ? short.MaxValue : (int)order;
        }

        /// A sub-order confined to one row's own gap: [0, Step-1].
        int ClampSub(int sub)
        {
            int step = Step;
            if (step <= 1 || sub <= 0) return 0;
            return sub < step ? sub : step - 1;
        }

        /// Whether this capability's output is split across several rows (one per piece/point) rather than
        /// drawn as one.
        public bool IsSplit(string capabilityId)
        {
            if (rows == null || string.IsNullOrEmpty(capabilityId)) return false;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].capabilityId == capabilityId && !rows[i].IsWholeCard) return true;
            return false;
        }

        // ── keeping the rows and the stack in step ────────────────────────────────────────────────────────

        /// Bring the rows back in line with the recipe: drop rows whose card is gone, drop instance rows past
        /// a card's current instance count, fill in missing instance rows for a split card, and give any
        /// producer with no row at all one at the FRONT (the end of the list) — which is what an output that
        /// is not in the plan already draws as, so adding the row changes nothing on screen.
        ///
        /// True when it actually changed something. Editor-side housekeeping: the resolver above degrades
        /// safely on its own, so nothing has to run this before a burst can fire.
        public bool Reconcile(IList<ChunkCapability> stack)
        {
            rows ??= new List<DepthRow>();
            bool changed = false;

            // Drop rows nothing in the stack answers for, and instance rows the card has shrunk past.
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                var cap = FindProducer(stack, rows[i].capabilityId);
                if (cap == null) { rows.RemoveAt(i); changed = true; continue; }
                if (!rows[i].IsWholeCard && rows[i].instance >= cap.DepthInstanceCount)
                { rows.RemoveAt(i); changed = true; }
            }

            // Drop duplicates of the same (card, instance) — a plan should never address one thing twice.
            var seen = new HashSet<string>();
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                string key = rows[i].capabilityId + "#" + rows[i].instance;
                if (!seen.Add(key)) { rows.RemoveAt(i); changed = true; }
            }

            if (stack != null)
                for (int s = 0; s < stack.Count; s++)
                {
                    var cap = stack[s];
                    if (cap == null || !cap.DrawsOutput) continue;
                    string id = cap.EnsureId();

                    if (IsSplit(id))
                    {
                        // A split card grew (its Pieces or Count dial went up): the new instances take their
                        // rows next to the ones already there, so the picture the author dragged stays put.
                        int count = cap.DepthInstanceCount;
                        for (int k = 0; k < count; k++)
                        {
                            if (HasRow(id, k)) continue;
                            rows.Insert(LastRowIndexOf(id) + 1, new DepthRow(id, k));
                            changed = true;
                        }
                        // A whole-card row alongside instance rows is a contradiction; the instances win.
                        for (int i = rows.Count - 1; i >= 0; i--)
                            if (rows[i].capabilityId == id && rows[i].IsWholeCard) { rows.RemoveAt(i); changed = true; }
                        continue;
                    }

                    if (LastRowIndexOf(id) < 0) { rows.Add(new DepthRow(id, -1)); changed = true; }
                }

            return changed;
        }

        /// Replace this card's ONE row with one row per instance, in place — so splitting changes nothing on
        /// screen until something is actually dragged. False when the card cannot split or already is.
        public bool Split(ChunkCapability cap)
        {
            if (cap == null || rows == null) return false;
            string id = cap.EnsureId();
            int count = cap.DepthInstanceCount;
            if (count < 2 || IsSplit(id)) return false;

            int at = LastRowIndexOf(id);
            if (at < 0) return false;
            rows.RemoveAt(at);
            for (int k = 0; k < count; k++) rows.Insert(at + k, new DepthRow(id, k));
            return true;
        }

        /// Replace this card's instance rows with ONE row, at the position of its FIRST instance row — the
        /// backmost place its output currently draws, which is the only choice that cannot pull anything
        /// forward past something the author put in front of it.
        public bool Join(ChunkCapability cap)
        {
            if (cap == null || rows == null) return false;
            string id = cap.EnsureId();
            if (!IsSplit(id)) return false;

            int at = FirstRowIndexOf(id);
            for (int i = rows.Count - 1; i >= 0; i--)
                if (rows[i].capabilityId == id) rows.RemoveAt(i);
            rows.Insert(Mathf.Clamp(at, 0, rows.Count), new DepthRow(id, -1));
            return true;
        }

        /// Move one row from one position to another (the drag). False, and nothing changed, for an
        /// out-of-range index or a no-op.
        public bool Move(int from, int to)
        {
            if (rows == null) return false;
            int count = rows.Count;
            if (from < 0 || from >= count || to < 0 || to >= count || from == to) return false;
            var moved = rows[from];
            rows.RemoveAt(from);
            rows.Insert(to, moved);
            return true;
        }

        bool HasRow(string id, int instance)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].capabilityId == id && rows[i].instance == instance) return true;
            return false;
        }

        int FirstRowIndexOf(string id)
        {
            for (int i = 0; i < rows.Count; i++) if (rows[i].capabilityId == id) return i;
            return -1;
        }

        int LastRowIndexOf(string id)
        {
            for (int i = rows.Count - 1; i >= 0; i--) if (rows[i].capabilityId == id) return i;
            return -1;
        }

        static ChunkCapability FindProducer(IList<ChunkCapability> stack, string id)
        {
            if (stack == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < stack.Count; i++)
            {
                var cap = stack[i];
                if (cap != null && cap.DrawsOutput && cap.id == id) return cap;
            }
            return null;
        }

        // ── the one-time migration from named slots ───────────────────────────────────────────────────────

        /// Turns the old named slots into rows, ONCE, reproducing exactly the order the recipe already drew
        /// in: slot by slot from the back, and within one slot by stack order. Cards that were in no slot are
        /// appended at the end — the front — which is precisely where an unslotted output already drew
        /// (its flat order was the emitter's 500-plus band, far above any slot's 0–39).
        ///
        /// The one order this cannot reproduce byte-for-byte is a TIE: two cards that named the same slot used
        /// to land on identical numbers, and which of them Unity drew on top was undefined. They become two
        /// rows in stack order, which is strictly less random than what they had.
        ///
        /// Returns true when it changed something, which is what tells <see cref="ChunkSpec.UpgradeIfNeeded"/>
        /// the asset is worth re-saving.
        public bool MigrateRows(IList<ChunkCapability> stack)
        {
            if (rowsMigrated) return false;
            rowsMigrated = true;
            rows ??= new List<DepthRow>();
            if (rows.Count > 0) return true;   // already authored as rows (built in code, hand-edited)

            var names = layers != null ? layers.layers : null;
            if (names != null)
                for (int s = 0; s < names.Count; s++)
                {
                    string slot = names[s];
                    if (string.IsNullOrEmpty(slot)) continue;
                    if (stack != null)
                        for (int i = 0; i < stack.Count; i++)
                        {
                            var cap = stack[i];
                            if (cap == null || !cap.DrawsOutput) continue;
                            if (cap.LayerName == slot) rows.Add(new DepthRow(cap.EnsureId(), -1));
                        }
                }

            if (stack != null)
                for (int i = 0; i < stack.Count; i++)
                {
                    var cap = stack[i];
                    if (cap == null || !cap.DrawsOutput) continue;
                    if (LastRowIndexOf(cap.id) < 0) rows.Add(new DepthRow(cap.EnsureId(), -1));
                }

            return true;
        }
    }
}
