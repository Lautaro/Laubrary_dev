using System.Collections.Generic;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds {

    /// <summary>What one value in a snapshot is. The number is serialized; append only.</summary>
    public enum SnapshotValueKind {
        /// <summary>An effect's parameter (node by identity, param by index).</summary>
        EffectParam = 0,
        /// <summary>An effect on or off (a crossfade between with and without it when glided).</summary>
        EffectOn = 1,
        /// <summary>A modifier's own parameter (an oscillator's rate, a random value's range...).</summary>
        ModifierParam = 2,
        /// <summary>How strongly a modifier moves one parameter (its binding's depth).</summary>
        BindingDepth = 3,
        /// <summary>Where a modifier's ZPOC rests before game code speaks (a value set by code wins over it).</summary>
        ZpocRest = 4,
        /// <summary>The Zound's own volume range (a, b); a play draws its volume from it.</summary>
        VolumeRange = 5,
        /// <summary>The Zound's own pitch range (a, b); a play draws its pitch from it.</summary>
        PitchRange = 6,
    }

    /// <summary>One saved value. <see cref="node"/> and <see cref="mod"/> are permanent identities, so a snapshot keeps
    /// finding its effect after the chain is reordered.</summary>
    [System.Serializable]
    public struct ZoundSnapshotValue {
        public SnapshotValueKind kind;
        public string node;
        public string mod;
        public int param;
        public float a, b;
    }

    /// <summary>
    /// A named, saved set of a Zound's settings (T-0498): every effect setting and whether each effect is on, every
    /// modifier setting and binding strength, where each ZPOC rests, and the Zound's own volume and pitch ranges. A
    /// playing sound glides to one over a duration, from wherever it is. The settings as they are in the editor are the
    /// snapshot called Default, which always exists and is never stored.
    /// </summary>
    [System.Serializable]
    public class ZoundSnapshot {
        public string name = "Snapshot";
        public List<ZoundSnapshotValue> values = new List<ZoundSnapshotValue>();

        public ZoundSnapshot DeepCopy() => new ZoundSnapshot { name = name, values = new List<ZoundSnapshotValue>(values) };
    }

    /// <summary>Capturing, finding and reading snapshots.</summary>
    public static class ZoundSnapshots {

        public const string DefaultName = "Default";

        /// <summary>How a value moves during a glide.</summary>
        public enum Motion { Glide, SwitchAtMidpoint, NotInSnapshots }

        /// <summary>
        /// How a parameter moves in a glide: continuous values glide along their control; on/off, whole numbers and choices
        /// switch at the glide's midpoint; a setting that sizes the sound's memory when a play starts (the delay's longest
        /// time) is left out of snapshots, since it cannot change while a sound plays.
        /// </summary>
        public static Motion MotionOf(ParamDesc pd, bool isEffect) {
            if (pd.curve == ParamCurve.Toggle || pd.curve == ParamCurve.Integer || pd.IsChoice) return Motion.SwitchAtMidpoint;
            if (isEffect && !pd.automatable) return Motion.NotInSnapshots;
            return Motion.Glide;
        }

        /// <summary>The Zound's settings as they are now, as a snapshot. Gives effects and modifiers their permanent
        /// identity if they have none yet (the caller records Undo first when that matters).</summary>
        public static ZoundSnapshot Capture(Zound zound, string name) => Capture(zound, name, assignIdentities: true);

        /// <summary>
        /// <paramref name="assignIdentities"/> false (Default, built at play time): nothing is written to the Zound; an effect
        /// or modifier without an identity yet is addressed by its position ("#3"), which is exact because Default is always
        /// built from the very chain it is applied to.
        /// </summary>
        public static ZoundSnapshot Capture(Zound zound, string name, bool assignIdentities) {
            var s = new ZoundSnapshot { name = name };
            if (zound == null) return s;
            // The chain as it plays, the sound's own curves included, so they glide too.
            if (assignIdentities) {
                var stored = ZoundDspPlayback.ResolveChain(zound, out _);
                bool added = stored != null && stored.EnsureUids();
                if (zound.ownCurves != null && zound.ownCurves.EnsureUids(stored)) { zound.ownCurves.Touch(); added = true; }
                if (added) ZoundDspPlayback.InvalidateLayout(zound);
            }
            var chain = ZoundDspPlayback.PlayChain(zound);
            if (chain != null) {
                string NodeId(int i) => string.IsNullOrEmpty(chain.nodes[i].uid) ? "#" + i : chain.nodes[i].uid;
                string ModId(int i) => string.IsNullOrEmpty(chain.modifiers[i].uid) ? "#" + i : chain.modifiers[i].uid;
                for (int i = 0; i < chain.nodes.Count; i++) {
                    var n = chain.nodes[i]; n.EnsureParams();
                    var d = ZoundEffectDescriptors.Get(n.type);
                    s.values.Add(new ZoundSnapshotValue { kind = SnapshotValueKind.EffectOn, node = NodeId(i), a = n.enabled ? 1f : 0f });
                    for (int k = 0; k < d.parameters.Length; k++) {
                        if (MotionOf(d.parameters[k], true) == Motion.NotInSnapshots) continue;
                        s.values.Add(new ZoundSnapshotValue { kind = SnapshotValueKind.EffectParam, node = NodeId(i), param = k, a = EffectiveParam(zound, i, k, n.Param(k)) });
                    }
                }
                for (int m = 0; m < chain.modifiers.Count; m++) {
                    var mod = chain.modifiers[m]; mod.EnsureParams();
                    var d = ZoundEffectDescriptors.GetModifier(mod.type);
                    for (int k = 0; k < d.parameters.Length; k++)
                        s.values.Add(new ZoundSnapshotValue { kind = SnapshotValueKind.ModifierParam, mod = ModId(m), param = k, a = mod.Param(k) });
                    if (mod.HasZpoc && mod.type != ZoundModifierType.Code)
                        s.values.Add(new ZoundSnapshotValue { kind = SnapshotValueKind.ZpocRest, mod = ModId(m), a = mod.zpocRest });
                }
                foreach (var b in chain.bindings) {
                    if (b.modifierIndex < 0 || b.modifierIndex >= chain.modifiers.Count) continue;
                    string target = b.nodeIndex >= 0 && b.nodeIndex < chain.nodes.Count ? NodeId(b.nodeIndex) : "";
                    s.values.Add(new ZoundSnapshotValue { kind = SnapshotValueKind.BindingDepth, mod = ModId(b.modifierIndex), node = target, param = b.paramIndex, a = b.depth });
                }
            }
            s.values.Add(new ZoundSnapshotValue { kind = SnapshotValueKind.VolumeRange, a = zound.minVolume, b = zound.maxVolume });
            s.values.Add(new ZoundSnapshotValue { kind = SnapshotValueKind.PitchRange, a = zound.minPitch, b = zound.maxPitch });
            return s;
        }

        /// <summary>An effect parameter as this Zound hears it: its own override of a shared chain preset, if it has one.</summary>
        static float EffectiveParam(Zound zound, int node, int param, float value) {
            if (zound.chainOverrides != null)
                foreach (var o in zound.chainOverrides) if (o.nodeIndex == node && o.paramIndex == param) return o.value;
            return value;
        }

        /// <summary>The snapshot a name reaches on <paramref name="zound"/> (names matched the way Zound names are), or null.
        /// "Default" always answers: the settings as authored, captured now.</summary>
        public static ZoundSnapshot Find(Zound zound, string name) {
            if (zound == null) return null;
            var key = ZpocKeys.Key(name);
            if (key == null) return null;
            if (zound.snapshots != null)
                foreach (var s in zound.snapshots) if (s != null && ZpocKeys.Key(s.name) == key) return s;
            if (key == ZpocKeys.Key(DefaultName)) return Capture(zound, DefaultName, assignIdentities: false);
            return null;
        }

        /// <summary>The effect a snapshot value's identity names in <paramref name="chain"/> ("#3" is a position), or -1.</summary>
        public static int NodeIndex(ZoundEffectChain chain, string id) {
            if (chain == null || string.IsNullOrEmpty(id)) return -1;
            if (id[0] == '#') return int.TryParse(id.Substring(1), out int i) && i >= 0 && i < chain.nodes.Count ? i : -1;
            return chain.NodeIndexOfUid(id);
        }

        /// <summary>The modifier a snapshot value's identity names in <paramref name="chain"/>, or -1.</summary>
        public static int ModifierIndex(ZoundEffectChain chain, string id) {
            if (chain == null || string.IsNullOrEmpty(id)) return -1;
            if (id[0] == '#') return int.TryParse(id.Substring(1), out int i) && i >= 0 && i < chain.modifiers.Count ? i : -1;
            return chain.ModifierIndexOfUid(id);
        }

        /// <summary>Whether <paramref name="zound"/> or anything it plays has a snapshot with this name (Default: always).</summary>
        public static bool AnywhereIn(Zound zound, string name, int depth = 0) {
            if (zound == null || depth > 16) return false;
            if (Find(zound, name) != null) return true;
            if (zound is CompositeZound c && c.zoundEntries != null)
                foreach (var e in c.zoundEntries)
                    if (e != null && c.TryGetEntryZound(e, out var child) && AnywhereIn(child, name, depth + 1)) return true;
            return false;
        }
    }
}
