using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// How a modifier's ZPOC value acts on that modifier's bindings. The number is serialized; append only.
    /// </summary>
    public enum ZpocMode {
        /// <summary>The authored depths times the value: nought is off, one is exactly as authored, never beyond.</summary>
        Scale = 0,
        /// <summary>The value IS the strength: the strongest binding moves to it and the others keep their proportion to
        /// it, so one is the most this modifier can do and a value above the authored strength is reachable.</summary>
        Set = 1,
    }
    /// <summary>
    /// One effect in a chain: a type, a bypass flag and positional parameters whose meaning comes from
    /// <see cref="Dsp.ZoundEffectDescriptors"/>. A tagged union rather than a class per effect because
    /// ZoundsProject serializes through JsonUtility, which cannot round-trip polymorphic lists.
    /// </summary>
    [System.Serializable]
    public class ZoundEffectNode {
        public ZoundEffectType type;
        public bool enabled = true;
        public float[] p = new float[0];
        /// <summary>A permanent identity that survives reordering (T-0498): what a snapshot addresses this effect by.
        /// Empty until something needs it (the first snapshot captured), then kept for good.</summary>
        public string uid = "";

        public ZoundEffectNode() { }
        public ZoundEffectNode(ZoundEffectType type) {
            this.type = type;
            p = Dsp.ZoundEffectDescriptors.DefaultParams(type);
        }

        public ZoundEffectNode DeepCopy() {
            var copy = new ZoundEffectNode { type = type, enabled = enabled, p = (float[])p.Clone(), uid = uid };
            return copy;
        }

        /// <summary>Parameter value with the descriptor default for indices the serialized array lacks.</summary>
        public float Param(int index) {
            if (p != null && index < p.Length) return p[index];
            return Dsp.ZoundEffectDescriptors.Get(type).parameters[index].def;
        }

        /// <summary>Grows the parameter array to the descriptor's size, filling with defaults.</summary>
        public void EnsureParams() {
            var desc = Dsp.ZoundEffectDescriptors.Get(type);
            if (p != null && p.Length >= desc.parameters.Length) return;
            var np = new float[desc.parameters.Length];
            for (int i = 0; i < np.Length; i++) np[i] = (p != null && i < p.Length) ? p[i] : desc.parameters[i].def;
            p = np;
        }
    }

    /// <summary>
    /// A value source that drives chain parameters through bindings: Envelope over the play length, an
    /// LFO, a per-trigger Random value, or a Step list. Positional parameters per the modifier descriptor.
    /// </summary>
    [System.Serializable]
    public class ZoundModifier {
        public ZoundModifierType type;
        public bool enabled = true;
        public string name = "";
        public float[] p = new float[0];
        /// <summary>Envelope: the shape. LFO: the ramp envelope multiplying the output over time.</summary>
        public Envelope curve = new Envelope(0f, 1f);
        /// <summary>Step: the value list.</summary>
        public float[] steps = new float[0];

        /// <summary>
        /// The id game code uses to reach this modifier through a play's token (ZPOC). Empty: not exposed. Unique within
        /// the Zound only, and matched the way Zound names are (case, spaces, underscores and hyphens ignored).
        /// </summary>
        public string zpocId = "";
        /// <summary>How the ZPOC value acts on this modifier's bindings. Not used by a Code modifier, whose output IS the value.</summary>
        public ZpocMode zpocMode = ZpocMode.Scale;
        /// <summary>
        /// Where the ZPOC value rests before code sends anything, 0..1. Below nought (the default) means "as authored",
        /// so exposing an existing modifier changes nothing until code speaks. A Code modifier rests at its own Value.
        /// </summary>
        public float zpocRest = -1f;
        /// <summary>How long a value sent by code takes to be reached, so a jump from code is never heard as a click.</summary>
        public float zpocSmoothMs = 30f;

        /// <summary>A permanent identity that survives reordering (T-0498); see <see cref="ZoundEffectNode.uid"/>.</summary>
        public string uid = "";

        public bool HasZpoc => !string.IsNullOrEmpty(zpocId);

        public ZoundModifier() { }
        public ZoundModifier(ZoundModifierType type) {
            this.type = type;
            p = Dsp.ZoundEffectDescriptors.DefaultModifierParams(type);
            name = Dsp.ZoundEffectDescriptors.GetModifier(type).displayName;
        }

        public float Param(int index) {
            if (p != null && index < p.Length) return p[index];
            return Dsp.ZoundEffectDescriptors.GetModifier(type).parameters[index].def;
        }

        public void EnsureParams() {
            var desc = Dsp.ZoundEffectDescriptors.GetModifier(type);
            if (p != null && p.Length >= desc.parameters.Length) return;
            var np = new float[desc.parameters.Length];
            for (int i = 0; i < np.Length; i++) np[i] = (p != null && i < p.Length) ? p[i] : desc.parameters[i].def;
            p = np;
        }

        public ZoundModifier DeepCopy() {
            return new ZoundModifier {
                type = type, enabled = enabled, name = name, p = (float[])p.Clone(),
                curve = curve != null ? curve.DeepCopy() : new Envelope(0f, 1f),
                steps = (float[])steps.Clone(),
                zpocId = zpocId, zpocMode = zpocMode, zpocRest = zpocRest, zpocSmoothMs = zpocSmoothMs, uid = uid,
            };
        }
    }

    /// <summary>Connects one modifier's output to one (node, parameter). nodeIndex -1 is the source stage.</summary>
    [System.Serializable]
    public class ZoundModifierBinding {
        public int modifierIndex;
        public int nodeIndex = -1;
        public int paramIndex;

        /// <summary>
        /// How the modulator combines with the authored value. Only meaningful once <see cref="schema"/> says so; before
        /// that, <see cref="op"/> holds the answer and is translated on load.
        /// </summary>
        public Dsp.ModulationCombine combine = Dsp.ModulationCombine.Shift;

        /// <summary>
        /// How far the modulator may move the parameter, as a fraction of that parameter's own control — so one means
        /// "from one end of this parameter's range to the other", and the same number means the same thing on a cutoff
        /// measured in thousands of hertz as on a mix measured from nought to one.
        ///
        /// Before <see cref="schema"/> reached its current value this was a raw amount in the parameter's units instead,
        /// which is precisely what made it unguessable and is why the schema number exists.
        /// </summary>
        public float depth = 0.5f;

        /// <summary>
        /// Which meaning the numbers above carry. Zero is the original form, where the combining was in <see cref="op"/>
        /// and the depth was a raw amount. A binding still at zero is translated every time it is used, and can be
        /// rewritten in place to stop that.
        ///
        /// **It defaults to zero deliberately, and that is load-bearing.** Saved chains predate this field, so they arrive
        /// with nothing to put in it — and a field left alone keeps whatever the type declares. Declaring the current
        /// value here would therefore make every old chain announce itself as already converted, and its raw amounts would
        /// be read as fractions: a sweep authored as three thousand hertz would peg its parameter at maximum forever.
        /// Code that creates a NEW binding must say so explicitly.
        /// </summary>
        public int schema;

        /// <summary>The original way of combining. Kept only so chains saved before the change can still be read.</summary>
        public ModifierOp op = ModifierOp.Multiply;

        public ZoundModifierBinding DeepCopy() {
            return new ZoundModifierBinding {
                modifierIndex = modifierIndex, nodeIndex = nodeIndex, paramIndex = paramIndex,
                op = op, depth = depth, combine = combine, schema = schema
            };
        }
    }

    /// <summary>Sparse per-parameter override of a referenced preset, addressed as (node, param).</summary>
    [System.Serializable]
    public struct ChainParamOverride {
        public int nodeIndex;
        public int paramIndex;
        public float value;
    }

    /// <summary>Source stage parameter indices (the stage that reads sample data ahead of the chain).</summary>
    public static class SourceStageParam {
        public const int Pitch = 0;
        public const int Gain = 1;
        /// <summary>Live time-stretch (T-0409): how fast the sound moves through its source without changing its
        /// pitch. Only heard on a sound with live speed switched on. Appended, so stored bindings keep their meaning.</summary>
        public const int Speed = 2;
        /// <summary>The Zound's own volume, after every effect (T-0493). Appended, so stored bindings keep their meaning.</summary>
        public const int Volume = 3;
        public const int Count = 4;
    }

    /// <summary>
    /// An ordered, reorderable list of effect nodes plus the modifiers and bindings that drive them.
    /// Authored data: read on the main thread only. The audio thread sees a flattened
    /// <see cref="Dsp.ChainLayout"/> built from this.
    /// </summary>
    [System.Serializable]
    public class ZoundEffectChain {
        public List<ZoundEffectNode> nodes = new List<ZoundEffectNode>();
        public List<ZoundModifier> modifiers = new List<ZoundModifier>();
        public List<ZoundModifierBinding> bindings = new List<ZoundModifierBinding>();

        /// <summary>Bumped by the editor on any edit so cached layouts can be rebuilt.</summary>
        [System.NonSerialized] public int version;

        public bool IsEmpty => (nodes == null || nodes.Count == 0) && (modifiers == null || modifiers.Count == 0);

        public ZoundEffectChain DeepCopy() {
            var copy = new ZoundEffectChain();
            foreach (var n in nodes) copy.nodes.Add(n.DeepCopy());
            foreach (var m in modifiers) copy.modifiers.Add(m.DeepCopy());
            foreach (var b in bindings) copy.bindings.Add(b.DeepCopy());
            return copy;
        }

        public void CopyFrom(ZoundEffectChain other) {
            nodes.Clear(); modifiers.Clear(); bindings.Clear();
            foreach (var n in other.nodes) nodes.Add(n.DeepCopy());
            foreach (var m in other.modifiers) modifiers.Add(m.DeepCopy());
            foreach (var b in other.bindings) bindings.Add(b.DeepCopy());
            version++;
        }

        public void Touch() { version++; }

        /// <summary>Gives every effect and modifier a permanent identity if it has none yet (T-0498). Returns whether any
        /// was added (the caller records Undo first, as for any edit).</summary>
        public bool EnsureUids() {
            bool added = false;
            // A duplicated effect or modifier carries its original's identity; within one chain every identity must differ.
            var seen = new HashSet<string>();
            foreach (var n in nodes) if (string.IsNullOrEmpty(n.uid) || !seen.Add(n.uid)) { n.uid = System.Guid.NewGuid().ToString("N").Substring(0, 12); seen.Add(n.uid); added = true; }
            seen.Clear();
            foreach (var m in modifiers) if (string.IsNullOrEmpty(m.uid) || !seen.Add(m.uid)) { m.uid = System.Guid.NewGuid().ToString("N").Substring(0, 12); seen.Add(m.uid); added = true; }
            return added;
        }

        public int NodeIndexOfUid(string uid) {
            if (string.IsNullOrEmpty(uid)) return -1;
            for (int i = 0; i < nodes.Count; i++) if (nodes[i].uid == uid) return i;
            return -1;
        }

        public int ModifierIndexOfUid(string uid) {
            if (string.IsNullOrEmpty(uid)) return -1;
            for (int i = 0; i < modifiers.Count; i++) if (modifiers[i].uid == uid) return i;
            return -1;
        }

        /// <summary>Removes bindings that point at a node, modifier or parameter that no longer exists.</summary>
        public void PruneBindings() {
            bindings.RemoveAll(b => b.modifierIndex < 0 || b.modifierIndex >= modifiers.Count
                || b.nodeIndex < -1 || b.nodeIndex >= nodes.Count
                || b.paramIndex < 0
                || (b.nodeIndex == -1 ? b.paramIndex >= SourceStageParam.Count
                                      : b.paramIndex >= Dsp.ZoundEffectDescriptors.Get(nodes[b.nodeIndex].type).parameters.Length));
        }

        public void RemoveNode(int index) {
            nodes.RemoveAt(index);
            foreach (var b in bindings) {
                if (b.nodeIndex == index) b.nodeIndex = int.MinValue;
                else if (b.nodeIndex > index) b.nodeIndex--;
            }
            PruneBindings();
            version++;
        }

        public void MoveNode(int from, int to) {
            if (from == to || from < 0 || from >= nodes.Count || to < 0 || to >= nodes.Count) return;
            var node = nodes[from];
            nodes.RemoveAt(from);
            nodes.Insert(to, node);
            foreach (var b in bindings) {
                if (b.nodeIndex < 0) continue;
                if (b.nodeIndex == from) b.nodeIndex = to;
                else if (from < to && b.nodeIndex > from && b.nodeIndex <= to) b.nodeIndex--;
                else if (from > to && b.nodeIndex >= to && b.nodeIndex < from) b.nodeIndex++;
            }
            version++;
        }

        public void RemoveModifier(int index) {
            modifiers.RemoveAt(index);
            foreach (var b in bindings) {
                if (b.modifierIndex == index) b.modifierIndex = int.MinValue;
                else if (b.modifierIndex > index) b.modifierIndex--;
            }
            PruneBindings();
            version++;
        }
    }

    /// <summary>A named, reusable chain shared by live reference (see Zound.chainPresetId).</summary>
    [System.Serializable]
    public class ZoundChainPreset {
        public int id;
        public string name = "New chain";
        public ZoundEffectChain chain = new ZoundEffectChain();
    }

}
