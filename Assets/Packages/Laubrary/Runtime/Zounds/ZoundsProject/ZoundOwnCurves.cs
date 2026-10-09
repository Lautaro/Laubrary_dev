using System.Collections.Generic;

namespace Laubrary.Zounds {

    /// <summary>
    /// One of a sound's own curves (its Volume, Pitch or Time curve, drawn over its waveform): the curve itself and how it
    /// acts on the sound's own value. Kept exactly as the chain once kept it (a modifier plus its one binding), so nothing
    /// authored is lost or reinterpreted: every point, random range, anchor mode, extra time, value range, enabled flag,
    /// ZPOC id and identity travels with it. <see cref="ZoundModifierBinding.modifierIndex"/> means nothing here; it is set
    /// when the curve is laid out with the chain (<see cref="ZoundOwnCurves.Merge"/>).
    /// </summary>
    [System.Serializable]
    public class ZoundOwnCurve {
        public bool has;
        public ZoundModifier modifier;
        public ZoundModifierBinding binding;

        public bool Has => has && modifier != null && binding != null;

        public ZoundOwnCurve DeepCopy() => new ZoundOwnCurve {
            has = Has, modifier = Has ? modifier.DeepCopy() : null, binding = Has ? binding.DeepCopy() : null
        };
    }

    /// <summary>
    /// The sound's own Volume, Pitch and Time curves (owner, 2026-10-08): properties of the sound, not entries of its
    /// modifier list. They used to be stored as envelope modifiers bound to the source stage inside the effect chain; a
    /// sound saved that way converts on its first access in the editor (<see cref="Adopt"/>), exactly, and plays the
    /// same. The engine still hears them as source-stage envelopes: <see cref="Merge"/> lays them out with the chain,
    /// and <c>ZoundDspPlayback.PlayChain</c> is the one place that chain comes from.
    /// </summary>
    [System.Serializable]
    public class ZoundOwnCurves {
        public ZoundOwnCurve volume = new ZoundOwnCurve();
        public ZoundOwnCurve pitch = new ZoundOwnCurve();
        public ZoundOwnCurve time = new ZoundOwnCurve();
        /// <summary>
        /// The sound's own Gain curve (owner, 2026-10-09): its level going INTO the effects (the source stage's Drive),
        /// drawn over the waveform and shaping how the waveform looks. A sound saved before it existed has none, and plays
        /// exactly as it did: an empty slot adds nothing to the played chain.
        /// </summary>
        public ZoundOwnCurve gain = new ZoundOwnCurve();

        /// <summary>Every source-stage value a sound can have an own curve on, in the order they are laid out with the
        /// chain (an appended value keeps every older sound's layout as it was).</summary>
        public static readonly int[] Params = { SourceStageParam.Volume, SourceStageParam.Pitch, SourceStageParam.Speed, SourceStageParam.Gain };

        /// <summary>Bumped on any edit of a curve, so the laid-out chain and the layouts built from it are rebuilt.</summary>
        [System.NonSerialized] public int version;

        public bool Any => volume.Has || pitch.Has || time.Has || (gain != null && gain.Has);

        public void Touch() { version++; }

        /// <summary>The slot for a source-stage value.</summary>
        public ZoundOwnCurve Of(int sourceParam) {
            switch (sourceParam) {
                case SourceStageParam.Volume: return volume;
                case SourceStageParam.Pitch: return pitch;
                case SourceStageParam.Speed: return time;
                case SourceStageParam.Gain: return gain ?? (gain = new ZoundOwnCurve());
                default: return null;
            }
        }

        public void Set(int sourceParam, ZoundModifier m, ZoundModifierBinding b) {
            var slot = Of(sourceParam);
            if (slot == null) return;
            slot.has = m != null && b != null; slot.modifier = m; slot.binding = b;
            if (b != null) { b.nodeIndex = -1; b.paramIndex = sourceParam; }
            version++;
        }

        public void Clear(int sourceParam) {
            var slot = Of(sourceParam);
            if (slot == null || !slot.has) return;
            slot.has = false; slot.modifier = null; slot.binding = null;
            version++;
        }

        public ZoundOwnCurves DeepCopy() => new ZoundOwnCurves { volume = volume.DeepCopy(), pitch = pitch.DeepCopy(), time = time.DeepCopy(), gain = gain != null ? gain.DeepCopy() : new ZoundOwnCurve() };

        /// <summary>
        /// The chain the engine plays: the stored chain's effects, modifiers and bindings, with the own curves laid out as
        /// source-stage envelopes. The stored modifiers keep their indices (the own curves are appended), so a live edit or
        /// a snapshot addressed by position lands where it did; the own curves' bindings come FIRST, which is where a
        /// curve created on the waveform always sat among the bindings of its value (<see cref="Adopt"/> only moves a
        /// curve out when that was so, which is what keeps the sound identical). The element objects are shared with the
        /// stored chain and the slots, never copied: an edit to a modifier reaches the sound; only the lists are new.
        /// </summary>
        public static ZoundEffectChain Merge(ZoundEffectChain stored, ZoundOwnCurves own) {
            if (own == null || !own.Any) return stored;
            var merged = new ZoundEffectChain();
            if (stored != null) {
                merged.nodes.AddRange(stored.nodes);
                merged.modifiers.AddRange(stored.modifiers);
            }
            foreach (int p in Params) {
                var slot = own.Of(p);
                if (!slot.Has) continue;
                slot.binding.modifierIndex = merged.modifiers.Count;
                slot.binding.nodeIndex = -1; slot.binding.paramIndex = p;
                merged.modifiers.Add(slot.modifier);
                merged.bindings.Add(slot.binding);
            }
            if (stored != null) merged.bindings.AddRange(stored.bindings);
            merged.version = (stored != null ? stored.version : 0) * 31 + own.version;
            return merged;
        }

        /// <summary>
        /// Moves a sound's own curves out of its stored chain into their own slots, for a sound saved before the slots
        /// existed. Done only where the move changes nothing that plays: the curve is the first envelope on its value
        /// (which is what the waveform's curve bar has always read as "the" curve), it drives nothing else, and no other
        /// modifier acts on that value before it in the chain's order (laid out again by <see cref="Merge"/> it comes
        /// first). Anything else is left in the chain, where it keeps playing and is still found by the editor. Never for
        /// a sound playing a shared preset (its chain belongs to every sound linked to it). Returns whether anything moved;
        /// the caller records Undo first when that matters.
        /// </summary>
        public static bool Adopt(Zound zound) {
            if (!(zound is Klip) || zound.chainPresetId != 0 || zound.effectChain == null) return false;
            var chain = zound.effectChain;
            if (zound.ownCurves == null) zound.ownCurves = new ZoundOwnCurves();
            var own = zound.ownCurves;
            bool any = false;
            foreach (int p in new[] { SourceStageParam.Volume, SourceStageParam.Pitch, SourceStageParam.Speed }) {
                if (own.Of(p).Has) continue;
                int bi = -1;
                for (int i = 0; i < chain.bindings.Count && bi < 0; i++) {
                    var b = chain.bindings[i];
                    if (b.nodeIndex != -1 || b.paramIndex != p) continue;
                    if (b.modifierIndex < 0 || b.modifierIndex >= chain.modifiers.Count) continue;
                    if (chain.modifiers[b.modifierIndex].type != ZoundModifierType.Envelope) continue;
                    bi = i;
                }
                if (bi < 0) continue;
                int mi = chain.bindings[bi].modifierIndex;
                bool exact = true;
                for (int i = 0; i < chain.bindings.Count && exact; i++) {
                    var b = chain.bindings[i];
                    if (i != bi && b.modifierIndex == mi) exact = false;                       // drives something else too
                    if (i < bi && b.nodeIndex == -1 && b.paramIndex == p) exact = false;       // something acts on the value before it
                }
                if (!exact) continue;
                var mod = chain.modifiers[mi];
                var bind = chain.bindings[bi];
                chain.bindings.RemoveAt(bi);
                chain.RemoveModifier(mi);
                own.Set(p, mod, bind);
                any = true;
            }
            if (any) chain.Touch();
            return any;
        }

        /// <summary>Gives the own curves a permanent identity if they have none yet (what a snapshot addresses them by).</summary>
        public bool EnsureUids(ZoundEffectChain stored) {
            bool added = false;
            var seen = new HashSet<string>();
            if (stored != null) foreach (var m in stored.modifiers) if (!string.IsNullOrEmpty(m.uid)) seen.Add(m.uid);
            foreach (var slot in new[] { volume, pitch, time, gain }) {
                if (slot == null) continue;
                if (!slot.Has) continue;
                if (string.IsNullOrEmpty(slot.modifier.uid) || !seen.Add(slot.modifier.uid)) {
                    slot.modifier.uid = System.Guid.NewGuid().ToString("N").Substring(0, 12); seen.Add(slot.modifier.uid); added = true;
                }
            }
            return added;
        }
    }
}
