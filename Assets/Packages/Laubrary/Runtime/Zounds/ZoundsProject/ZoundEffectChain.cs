using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>Effect node types. The number is serialized; append only, never renumber.</summary>
    public enum ZoundEffectType {
        Gain = 0,
        Limiter = 1,
        Compressor = 2,
        Delay = 3,
        Reverb = 4,
        LowPass = 5,
        HighPass = 6,
        Flanger = 7,
        Chorus = 8,
        Phaser = 9,
        BitCrush = 10,
        Distortion = 11,
        EQ = 12,
        Normalize = 13,
        Fade = 14,
        TransientShaper = 15,
    }

    public enum ZoundModifierType {
        Envelope = 0,
        Lfo = 1,
        Random = 2,
        Step = 3,
    }

    public enum ModifierOp {
        Multiply = 0,
        Add = 1,
        Replace = 2,
    }

    public enum LfoShape { Sine = 0, Triangle = 1, Saw = 2, Square = 3 }
    public enum LfoMode { Oscillate = 0, Random = 1 }
    public enum StepTiming { PerTrigger = 0, PerInterval = 1 }
    public enum StepOrder { Sequential = 0, RoundRobinNoRepeat = 1 }

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

        public ZoundEffectNode() { }
        public ZoundEffectNode(ZoundEffectType type) {
            this.type = type;
            p = Dsp.ZoundEffectDescriptors.DefaultParams(type);
        }

        public ZoundEffectNode DeepCopy() {
            var copy = new ZoundEffectNode { type = type, enabled = enabled, p = (float[])p.Clone() };
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
                steps = (float[])steps.Clone()
            };
        }
    }

    /// <summary>Connects one modifier's output to one (node, parameter). nodeIndex -1 is the source stage.</summary>
    [System.Serializable]
    public class ZoundModifierBinding {
        public int modifierIndex;
        public int nodeIndex = -1;
        public int paramIndex;
        public ModifierOp op = ModifierOp.Multiply;
        public float depth = 1f;

        public ZoundModifierBinding DeepCopy() {
            return new ZoundModifierBinding { modifierIndex = modifierIndex, nodeIndex = nodeIndex, paramIndex = paramIndex, op = op, depth = depth };
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
        public const int Count = 2;
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
