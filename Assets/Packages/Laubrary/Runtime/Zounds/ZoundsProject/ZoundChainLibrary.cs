using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Chain library operations: named presets shared by live reference (Zound.chainPresetId), with
    /// Detach (break the link into a local copy) and Reconnect, following the same shared/local pattern
    /// the library uses for Zounds themselves (originalId / ZoundEntry.local).
    /// Every method mutates the in-memory project only; the caller wraps it in the editor's undo/save.
    /// </summary>
    public static class ZoundChainLibrary {

        public static List<ZoundChainPreset> Presets => ZoundsProject.Instance.chainPresets;

        public static ZoundChainPreset Find(int id) => ZoundsProject.Instance.FindChainPreset(id);

        public static int CountUsers(int presetId) => presetId == 0 ? 0 : ZoundsProject.Instance.CountChainPresetUsers(presetId);

        public static List<Zound> FindUsers(int presetId) {
            var users = new List<Zound>();
            if (presetId == 0) return users;
            ZoundsProject.Instance.zoundLibrary.ForEachZound(z => { if (z.chainPresetId == presetId) users.Add(z); });
            return users;
        }

        private static int NewId() {
            int id;
            do { id = Random.Range(int.MinValue, int.MaxValue); } while (id == 0 || Find(id) != null);
            return id;
        }

        public static string EnsureUniqueName(string name, ZoundChainPreset ignore = null) {
            if (string.IsNullOrWhiteSpace(name)) name = "New chain";
            string candidate = name;
            int n = 1;
            while (Presets.Exists(p => p != ignore && p.name == candidate)) candidate = name + " (" + (++n) + ")";
            return candidate;
        }

        /// <summary>Creates a preset from a copy of <paramref name="source"/> (or an empty chain).</summary>
        public static ZoundChainPreset Create(string name, ZoundEffectChain source = null) {
            var preset = new ZoundChainPreset { id = NewId(), name = EnsureUniqueName(name), chain = source != null ? source.DeepCopy() : new ZoundEffectChain() };
            Presets.Add(preset);
            return preset;
        }

        /// <summary>Overwrites a preset's chain with a copy of the zound's current chain (every user hears it).</summary>
        public static void UpdateFrom(ZoundChainPreset preset, ZoundEffectChain source) {
            preset.chain.CopyFrom(source);
        }

        /// <summary>Links a zound to a preset by live reference; its inline chain is kept aside untouched.</summary>
        public static void Assign(Zound zound, ZoundChainPreset preset) {
            zound.chainPresetId = preset.id;
            zound.detachedChainPresetId = 0;
            zound.chainOverrides.Clear();
            zound.effectChain.Touch();
        }

        /// <summary>Unlinks the zound; it goes back to its own inline chain.</summary>
        public static void Unlink(Zound zound) {
            zound.detachedChainPresetId = zound.chainPresetId;
            zound.chainPresetId = 0;
            zound.chainOverrides.Clear();
            zound.effectChain.Touch();
        }

        /// <summary>Breaks the link into a local copy of the preset (with the zound's overrides folded in).</summary>
        public static void Detach(Zound zound) {
            var preset = Find(zound.chainPresetId);
            if (preset == null) { Unlink(zound); return; }
            var copy = preset.chain.DeepCopy();
            foreach (var o in zound.chainOverrides) {
                if (o.nodeIndex >= 0 && o.nodeIndex < copy.nodes.Count) {
                    var node = copy.nodes[o.nodeIndex];
                    node.EnsureParams();
                    if (o.paramIndex >= 0 && o.paramIndex < node.p.Length) node.p[o.paramIndex] = o.value;
                }
            }
            zound.effectChain.CopyFrom(copy);
            zound.detachedChainPresetId = preset.id;
            zound.chainPresetId = 0;
            zound.chainOverrides.Clear();
        }

        /// <summary>Restores the live link a Detach broke, if the preset still exists.</summary>
        public static bool Reconnect(Zound zound) {
            var preset = Find(zound.detachedChainPresetId);
            if (preset == null) return false;
            zound.chainPresetId = preset.id;
            zound.detachedChainPresetId = 0;
            zound.chainOverrides.Clear();
            zound.effectChain.Touch();
            return true;
        }

        public static bool CanReconnect(Zound zound) => zound.chainPresetId == 0 && Find(zound.detachedChainPresetId) != null;

        /// <summary>Deletes a preset; refused while referenced unless every user is detached first.</summary>
        public static bool Delete(ZoundChainPreset preset, bool detachUsers) {
            var users = FindUsers(preset.id);
            if (users.Count > 0) {
                if (!detachUsers) return false;
                foreach (var z in users) Detach(z);
            }
            Presets.Remove(preset);
            return true;
        }

        public static ZoundChainPreset Duplicate(ZoundChainPreset preset) {
            return Create(preset.name, preset.chain);
        }

        // ── overrides on a linked zound ──

        public static bool TryGetOverride(Zound zound, int nodeIndex, int paramIndex, out float value) {
            for (int i = 0; i < zound.chainOverrides.Count; i++) {
                var o = zound.chainOverrides[i];
                if (o.nodeIndex == nodeIndex && o.paramIndex == paramIndex) { value = o.value; return true; }
            }
            value = 0f;
            return false;
        }

        public static void SetOverride(Zound zound, int nodeIndex, int paramIndex, float value) {
            for (int i = 0; i < zound.chainOverrides.Count; i++) {
                var o = zound.chainOverrides[i];
                if (o.nodeIndex == nodeIndex && o.paramIndex == paramIndex) { o.value = value; zound.chainOverrides[i] = o; zound.effectChain.Touch(); return; }
            }
            zound.chainOverrides.Add(new ChainParamOverride { nodeIndex = nodeIndex, paramIndex = paramIndex, value = value });
            zound.effectChain.Touch();
        }

        public static void ClearOverride(Zound zound, int nodeIndex, int paramIndex) {
            zound.chainOverrides.RemoveAll(o => o.nodeIndex == nodeIndex && o.paramIndex == paramIndex);
            zound.effectChain.Touch();
        }
    }

}
