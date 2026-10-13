using System;
using System.Linq;
using System.Xml.Linq;
using Laubrary.Audio;
using Laubrary.ZTracker.Model;
using Laubrary.ZTracker.Renoise;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>Explicit chain mapping owns a new sound asset, never edits a user's chosen sound in place.</summary>
    internal static class RenoiseInstrumentChainMapping
    {
        internal static void Apply(RenoiseImportLink link, RenoiseMapping mapping, Object owner, string chainId, bool dry)
        {
            RenoiseImportService.ValidateLink(link);
            if (mapping == null || mapping.removed || !link.mappings.Contains(mapping) || mapping.source.Kind != "EffectChain" || mapping.source.NestingDepth != 0 || mapping.source.InstrumentIndex < 0) throw new InvalidOperationException("Choose a current top-level instrument effect chain.");
            var row = link.mappings.FirstOrDefault(m => !m.removed && m.source.Kind == "Instrument" && m.source.InstrumentIndex == mapping.source.InstrumentIndex);
            if (row?.instrument == null) throw new InvalidOperationException("Choose an instrument target first, then map its effects.");
            if (row.instrument.schemaVersion != 1) throw new InvalidOperationException("The chosen instrument's schema is unsupported.");
            var selection = new RenoiseMapping { chainOwner = owner, chainId = chainId, bypass = dry };
            var chain = RenoiseImportService.ResolveChain(selection);
            if (chain == null) throw new InvalidOperationException("The chosen chain is unavailable.");
            var copy = Object.Instantiate(row.instrument); copy.name = row.instrument.name + " FX";
            // Imported samplers keep the exact source sample slots and XML. Other targets have no source-slot
            // correspondence: their first/whole-instrument chain is replaced as an explicit approximation.
            bool sourceSlots = copy.model.family == InstrumentFamily.Sampler && copy.model.id.StartsWith("renoise-instrument-", StringComparison.Ordinal);
            var affected = new System.Collections.Generic.List<int>();
            try
            {
            if (sourceSlots)
            {
                var source = XElement.Parse(copy.model.provenance);
                var samples = (source.Element("SampleGenerator") ?? source).Element("Samples")?.Elements().ToList();
                if (samples == null) throw new InvalidOperationException("Source sample slots unavailable; map the replacement instrument's FX in its editor.");
                var sourceChains = RenoiseXml.Element(source, "SampleGenerator/DeviceChains")?.Elements().ToList();
                var matches = sourceChains?.Select((c, i) => new { chain = c, index = i }).Where(c => RenoiseIdentity.ChainFingerprint(c.chain) == mapping.source.Fingerprint).ToList();
                if (matches != null && matches.Count > 1) matches = matches.Where(c => RenoiseXml.Text(c.chain, "Name") == mapping.source.Name).ToList();
                if (matches == null || matches.Count != 1) throw new InvalidOperationException("This chain cannot be matched unambiguously to the retained target's source samples. Edit the instrument's FX directly.");
                int sourceChainIndex = matches[0].index;
                var seen = new System.Collections.Generic.HashSet<int>();
                for (int i = 0; i < copy.model.sampler.samples.Count; i++)
                {
                    string sampleId = copy.model.sampler.samples[i].id;
                    if (!sampleId.StartsWith("sample-", StringComparison.Ordinal) || !int.TryParse(sampleId.Substring(7), out int ordinal) || ordinal < 0 || ordinal >= samples.Count || !seen.Add(ordinal)) throw new InvalidOperationException("Target sample identity is ambiguous; map its FX in the instrument editor.");
                    if (RenoiseXml.Int(samples[ordinal], "DeviceChainIndex", -1) == sourceChainIndex) affected.Add(i);
                }
                if (affected.Count == 0) throw new InvalidOperationException("This source chain has no assigned samples. No target was changed.");
                if (copy.model.fxChains.Count >= 64) throw new InvalidOperationException("This instrument has reached the 64-chain limit.");
                int index = copy.model.fxChains.Count; copy.model.fxChains.Add(ZTrackerMigration.Copy(chain));
                foreach (int sample in affected) copy.model.sampler.samples[sample].fxChain = index;
            }
            else
            {
                copy.model.fxChains.Clear(); copy.model.fxChains.Add(ZTrackerMigration.Copy(chain));
                foreach (var sample in copy.model.sampler.samples) sample.fxChain = 0;
            }
            copy.model.id = (sourceSlots ? "renoise-instrument-" + mapping.source.InstrumentIndex + "-copy-" : "instrument-") + Guid.NewGuid().ToString("N");
            }
            catch { Object.DestroyImmediate(copy); throw; }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            string path = AssetDatabase.GenerateUniqueAssetPath(link.ownedFolder + "/" + string.Join("_", copy.name.Split(System.IO.Path.GetInvalidFileNameChars())) + ".asset");
            try
            {
                AssetDatabase.CreateAsset(copy, path); Undo.RegisterCreatedObjectUndo(copy, "Map Renoise instrument FX");
                RenoiseImportService.SetInstrument(link, row, copy);
                mapping.chainOwner = owner; mapping.chainId = chainId; mapping.bypass = dry; mapping.explicitTarget = true;
                mapping.chainSnapshot = ZTrackerMigration.Copy(chain); mapping.status = RenoiseMappingStatus.Approximate;
                mapping.detail = sourceSlots ? "Owned instrument copy: source-assigned samples use the chosen replacement chain." : "Owned instrument copy: all sample/whole-instrument FX replaced; source chain topology is approximate.";
                foreach (var child in link.mappings.Where(m => m.source.ParentKey == mapping.source.Key)) { child.status = RenoiseMappingStatus.Approximate; child.detail = mapping.detail; }
                link.ownedAssets.Add(path); EditorUtility.SetDirty(link); AssetDatabase.SaveAssetIfDirty(copy);
                Undo.CollapseUndoOperations(group);
            }
            catch { Undo.RevertAllDownToGroup(group); if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path); else if (copy != null) Object.DestroyImmediate(copy); throw; }
        }
    }
}
