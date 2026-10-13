using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;

namespace Laubrary.ZTracker.Renoise
{
    /// <summary>Editor-main-thread adapter. Its caller owns the new import folder and rollback transaction.</summary>
    public static class RenoiseInstrumentConverter
    {
        public static ZTrackerInstrument Convert(RenoiseArchive archive, XElement instrument, int sourceIndex, string ownedFolder, List<RenoiseIssue> issues, int maximumPcmBytes = RenoiseInstrumentAudio.MaximumPcmBytes)
        {
            if (archive == null || instrument == null || issues == null) throw new ArgumentNullException();
            if (maximumPcmBytes < 0 || maximumPcmBytes > RenoiseInstrumentAudio.MaximumPcmBytes) throw new ArgumentOutOfRangeException(nameof(maximumPcmBytes));
            ValidateFolder(ownedFolder);
            var samplesRoot = instrument.Element("SampleGenerator") ?? instrument;
            var sampleNodes = samplesRoot.Element("Samples")?.Elements("Sample").ToArray() ?? Array.Empty<XElement>();
            if (sampleNodes.Length > 4096) throw new InvalidDataException("Instrument has more than 4096 samples.");
            var result = ScriptableObject.CreateInstance<ZTrackerInstrument>();
            try
            {
                result.schemaVersion = ZTrackerMigration.CurrentVersion;
                result.name = Text(instrument, "Name", "Instrument " + sourceIndex.ToString("X2"));
                var model = result.model = new InstrumentData { id = "renoise-instrument-" + sourceIndex, name = result.name, family = InstrumentFamily.Sampler, provenance = instrument.ToString(SaveOptions.DisableFormatting) };
                for (int i = 0; i < model.macros.Length; i++) model.macros[i] = new InstrumentMacro { name = "Macro " + (i + 1) };
                model.parameters.fmOperators = Enumerable.Range(0, 4).Select(i => new ZTrackerInstrument.FMOperatorData { freqRatio = 1, level = i == 0 ? 1 : .5f, attack = .01f, decay = .2f, sustain = .7f, release = .5f }).ToArray();
                string location = "instrument[" + sourceIndex + "]";
                var globals = instrument.Element("GlobalProperties");
                model.sampler.volume = Number(globals, "Volume", 1);
                model.sampler.transpose = Integer(globals, "Transpose", 0);
                ReadModulation(samplesRoot, model, issues, location);
                FlagInstrument(instrument, samplesRoot, issues, location);
                bool exclusive = Text(samplesRoot, "KeyzoneOverlappingMode", "Play All") != "Play All";
                bool legacyMap = instrument.Element("SampleGenerator") == null && (instrument.Element("SplitMap") != null || instrument.Element("SampleSplitMap") != null);
                if (legacyMap) Issue(issues, "sample-legacy-map", "Legacy split-map format needs a separate adapter; audio is retained but zones remain inactive.", location);
                if (exclusive) Issue(issues, "sample-selection", "Cycle/random sample selection is unsupported; overlapping alternatives remain inactive rather than playing simultaneously.", location);
                var clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
                var encodedClips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
                long decodedBytes = 0;
                for (int index = 0; index < sampleNodes.Length; index++)
                {
                    XElement node = sampleNodes[index], mapping = node.Element("Mapping");
                    string at = location + "/sample[" + index + "]";
                    string layer = Text(mapping, "Layer", "Note-On Layer");
                    var sample = new SampleData {
                        id = "sample-" + index, name = Text(node, "Name", "Sample " + index),
                        volume = Number(node, "Volume", 1), pan = Number(node, "Panning", .5f) * 2 - 1,
                        transpose = Integer(node, "Transpose", 0), fineTuneCents = Integer(node, "Finetune", 0) * (100f / 127f),
                        baseNote = Integer(mapping, "BaseNote", Integer(node, "BaseNote", 48)),
                        releaseExitsLoop = Boolean(node, "LoopRelease", false), oneShot = Boolean(node, "OneShotTrigger", false),
                        loopStartFrame = Integer(node, "LoopStart", 0), loopEndFrame = Integer(node, "LoopEnd", 0),
                        muteGroup = Integer(node, "MuteGroupIndex", -1), modulationSet = Integer(node, "ModulationSetIndex", -1),
                        inactive = legacyMap || layer != "Note-On Layer" || Boolean(node, "IsAlias", false),
                        rawSliceMetadata = node.Element("SliceMarkers")?.ToString(SaveOptions.DisableFormatting) ?? ""
                    };
                    string loop = Text(node, "LoopMode", "Off"), nna = Text(node, "NewNoteAction", "NoteOff");
                    if (!Enum.TryParse(loop, out sample.loop)) { sample.loop = SampleLoop.Off; Issue(issues, "sample-loop-mode", "Unknown sample loop mode was disabled: " + loop, at); }
                    sample.nna = nna == "Cut" ? NewNoteAction.Cut : nna == "None" ? NewNoteAction.Continue : NewNoteAction.NoteOff;
                    if (nna != "Cut" && nna != "None" && nna != "NoteOff") Issue(issues, "sample-new-note-action", "Unknown new-note action uses note-off.", at);
                    string interpolation = Text(node, "InterpolationMode", "Cubic");
                    sample.interpolation = interpolation == "Linear" ? SampleInterpolation.Linear : SampleInterpolation.Cubic;
                    if (interpolation != "Linear" && interpolation != "Cubic") Issue(issues, "sample-interpolation", "Unsupported " + interpolation + " interpolation uses cubic.", at);
                    if (sample.fineTuneCents != 0) Issue(issues, "sample-finetune-approximate", "Fine tuning uses 100/127 cents per signed source step; Renoise endpoint parity is uncalibrated.", at);
                    if (sample.loop != SampleLoop.Off) Issue(issues, "sample-loop-endpoint", "Source loop frame bounds are retained with an exclusive end; Renoise endpoint playback parity is uncalibrated.", at);
                    if (sample.muteGroup >= 0) Issue(issues, "sample-choke-scope", "Mute group is scoped to this instrument and note track; cross-track Renoise choke parity is unverified.", at);
                    if (sample.modulationSet < -1 || sample.modulationSet >= model.modulation.Count) { sample.modulationSet = -1; Issue(issues, "sample-modulation-reference", "Missing modulation-set reference was disconnected.", at); }
                    if (Integer(node, "DeviceChainIndex", -1) >= 0) Issue(issues, "sample-effects-unmapped", "Sample effect-chain assignment requires a replacement; imported PCM bypasses that source chain.", at);
                    foreach (string flag in new[] { "BeatSyncIsActive", "Oversample", "AutoFade", "AutoSeek", "IsAlias" })
                        if (Boolean(node, flag, false)) Issue(issues, "sample-" + flag.ToLowerInvariant(), flag == "IsAlias" ? "Slice alias is retained inactive; source slice playback is unsupported." : flag + " is unsupported and is not applied.", at);
                    if (node.Element("SliceMarkers")?.HasElements == true) Issue(issues, "sample-slices", "Slice markers are retained as source metadata; slice-trigger mapping is unsupported.", at);
                    if (layer == "Note-Off Layer") Issue(issues, "sample-release-layer", "Note-off-triggered layer is retained inactive.", at);
                    else if (layer != "Note-On Layer" && layer != "Disabled Layer") Issue(issues, "sample-layer", "Unknown layer is retained inactive: " + layer, at);
                    if (!Boolean(mapping, "MapVelocityToVolume", true)) Issue(issues, "sample-velocity-gain", "Velocity zone selection is retained, but disabling velocity-to-gain is unsupported.", at);
                    byte[] source = FindSample(archive, sourceIndex, index);
                    if (source == null)
                    {
                        sample.inactive = true;
                        if (!Boolean(node, "IsAlias", false)) Issue(issues, "sample-missing", "Embedded sample audio is missing; no external file path is followed.", at);
                    }
                    else
                    {
                        try
                        {
                            string encodedHash;
                            using (var sha = SHA256.Create()) encodedHash = BitConverter.ToString(sha.ComputeHash(source));
                            if (!encodedClips.TryGetValue(encodedHash, out sample.pcm))
                            {
                                int remainingBudget = maximumPcmBytes - checked((int)decodedBytes);
                                if (remainingBudget < 4) throw new RenoiseInstrumentAudio.PcmBudgetException();
                                // Canonical PCM WAV hash includes rate/channels/frames as well as audio values.
                                var pcm = RenoiseInstrumentAudio.Decode(source, remainingBudget);
                                byte[] wav = RenoiseInstrumentAudio.ToWave(pcm);
                                string hash;
                                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(wav)).Replace("-", "").ToLowerInvariant();
                                if (!clips.TryGetValue(hash, out sample.pcm))
                                {
                                    decodedBytes += (long)pcm.Values.Length * 4;
                                    if (decodedBytes > maximumPcmBytes) throw new RenoiseInstrumentAudio.PcmBudgetException();
                                    string path = ownedFolder.TrimEnd('/') + "/instrument-" + sourceIndex + "-pcm-" + hash + ".wav";
                                    // Never overwrite even an import-owned file. Repeated in-call PCM is deduplicated above.
                                    using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) output.Write(wav, 0, wav.Length);
                                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                                    var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                                    if (importer == null) throw new InvalidDataException("Unity did not create an audio importer.");
                                    var settings = importer.defaultSampleSettings;
                                    settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
                                    settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate; settings.preloadAudioData = true;
                                    importer.defaultSampleSettings = settings; importer.forceToMono = false; importer.loadInBackground = false;
                                    importer.SaveAndReimport();
                                    sample.pcm = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                                    if (sample.pcm == null || sample.pcm.samples != pcm.Frames || sample.pcm.frequency != pcm.Rate || sample.pcm.channels != pcm.Channels)
                                        throw new InvalidDataException("Unity PCM import changed sample geometry.");
                                    sample.pcm.LoadAudioData(); clips.Add(hash, sample.pcm);
                                }
                                encodedClips.Add(encodedHash, sample.pcm);
                            }
                            sample.regionEndFrame = sample.pcm.samples;
                            if (sample.loop != SampleLoop.Off && (sample.loopStartFrame < 0 || sample.loopEndFrame > sample.pcm.samples || sample.loopEndFrame - sample.loopStartFrame < 2))
                            {
                                sample.loop = SampleLoop.Off; Issue(issues, "sample-loop-bounds", "Invalid or shorter-than-two-frame loop was disabled; raw bounds remain in provenance.", at);
                            }
                        }
                        catch (InvalidDataException error) { sample.pcm = null; sample.inactive = true; Issue(issues, "sample-audio", error.Message, at); }
                        catch (OverflowException) { sample.pcm = null; sample.inactive = true; Issue(issues, "sample-audio", "Sample size or predictor exceeds numeric safety limits.", at); }
                    }
                    if (sample.pcm == null) sample.loop = SampleLoop.Off;
                    if (sample.volume < 0 || sample.volume > 16 || sample.pan < -1 || sample.pan > 1 || sample.muteGroup < -1 || model.sampler.volume < 0 || model.sampler.volume > 16)
                        throw new InvalidDataException("Sample level, pan or mute group outside runtime limits at " + at);
                    var zone = new Keyzone {
                        id = "zone-" + index, sample = index, baseNote = sample.baseNote,
                        noteMin = Integer(mapping, "NoteStart", 0), noteMax = Integer(mapping, "NoteEnd", 119),
                        velocityMin = Integer(mapping, "VelocityStart", 0), velocityMax = Integer(mapping, "VelocityEnd", 127),
                        keyTracking = Boolean(mapping, "MapKeyToPitch", true), inactive = sample.inactive,
                        provenance = mapping?.ToString(SaveOptions.DisableFormatting) ?? ""
                    };
                    if (zone.noteMin < 0 || zone.noteMax > 119 || zone.noteMin > zone.noteMax || zone.baseNote < 0 || zone.baseNote > 119 || zone.velocityMin < 0 || zone.velocityMax > 127 || zone.velocityMin > zone.velocityMax)
                        throw new InvalidDataException("Invalid note/velocity mapping at " + at);
                    if (exclusive && model.sampler.zones.Any(z => !z.inactive && z.noteMin <= zone.noteMax && z.noteMax >= zone.noteMin && z.velocityMin <= zone.velocityMax && z.velocityMax >= zone.velocityMin))
                        zone.inactive = sample.inactive = true;
                    model.sampler.samples.Add(sample); model.sampler.zones.Add(zone);
                }
                foreach (var issue in issues) if (issue.Location != null && issue.Location.StartsWith(location, StringComparison.Ordinal)) model.diagnostics.Add(issue.Code + ": " + issue.Message);
                return result;
            }
            catch { UnityEngine.Object.DestroyImmediate(result); throw; }
        }

        static byte[] FindSample(RenoiseArchive archive, int instrument, int sample)
        {
            byte[] found = null;
            foreach (var pair in archive.Samples)
            {
                string path = pair.Key.Replace('\\', '/');
                var match = Regex.Match(path, archive.IsSong ? @"^SampleData/Instrument([0-9]+)(?:[^/]*)/Sample([0-9]+)(?:[^/]*)$" : @"^SampleData/Sample([0-9]+)(?:[^/]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success) continue;
                if (archive.IsSong && ParseOrdinal(match.Groups[1].Value) != instrument) continue;
                if (ParseOrdinal(match.Groups[archive.IsSong ? 2 : 1].Value) != sample) continue;
                if (found != null) throw new InvalidDataException("Ambiguous embedded sample ordinal " + sample);
                found = pair.Value;
            }
            return found;
        }
        static int ParseOrdinal(string s) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : -1;

        static void ValidateFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets/", StringComparison.Ordinal) || folder.Contains("\\") || folder.Split('/').Any(x => x == ".." || x == "." || x.Length == 0))
                throw new ArgumentException("Use an existing new importer-owned folder below Assets.");
            string full = Path.GetFullPath(folder), assets = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
            if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full) ||
                full.StartsWith(Path.GetFullPath("Assets/ZTracker") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(full, Path.GetFullPath("Assets/ZTracker"), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Importer destination must be owned and outside Assets/ZTracker.");
            for (var part = new DirectoryInfo(full); part != null && part.FullName.StartsWith(assets, StringComparison.OrdinalIgnoreCase); part = part.Parent)
                if ((part.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Importer destination cannot traverse a link.");
        }

        static void ReadModulation(XElement root, InstrumentData model, List<RenoiseIssue> issues, string location)
        {
            var nodes = root.Element("ModulationSets")?.Elements("ModulationSet").ToArray() ?? Array.Empty<XElement>();
            if (nodes.Length > 4096) throw new InvalidDataException("Too many sample modulation sets.");
            for (int index = 0; index < nodes.Length; index++)
            {
                var set = new ModulationSet { id = "modulation-" + index, name = Text(nodes[index], "Name", "Modulation " + (index + 1)) };
                model.modulation.Add(set); string at = location + "/modulation[" + index + "]";
                if (Integer(nodes[index], "FilterType", 0) != 0) Issue(issues, "sample-filter", "Source sample filter is not converted; choose a ZTracker filter.", at);
                int deviceIndex = 0;
                foreach (var node in nodes[index].Element("Devices")?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    string deviceAt = at + "/device[" + deviceIndex++ + "]";
                    if (deviceIndex > 64) throw new InvalidDataException("More than 64 modulation devices in a sample set.");
                    bool enabled = Number(node.Element("IsActive"), "Value", 1) != 0;
                    var raw = new ModulationDevice { id = "modulation-" + index + "-" + deviceIndex, enabled = false, kind = ModulationDeviceKind.Multipoint, rawSource = node.ToString(SaveOptions.DisableFormatting) };
                    set.devices.Add(raw);
                    if (!enabled) continue;
                    if (node.Name.LocalName != "SampleEnvelopeModulationDevice" || Text(node, "Target", "Volume") != "Volume" || Text(node, "Operator", "*") != "*" || Boolean(node, "Bipolar", false) || Boolean(node, "TempoSynced", false))
                    {
                        Issue(issues, "sample-modulation-device", "Only unsynced, unipolar, multiplying volume point envelopes are converted; " + node.Name.LocalName + " is retained inactive.", deviceAt); continue;
                    }
                    var envelope = node.Element("Nodes");
                    if (Text(envelope, "PlayMode", "Curves") != "Lines" || Number(envelope, "ValueQuantum", 0) != 0)
                    {
                        Issue(issues, "sample-envelope-interpolation", "Only unquantized straight-line volume envelopes are converted; this envelope is retained inactive.", deviceAt); continue;
                    }
                    bool compatible = true; double previous = -1;
                    foreach (var point in envelope?.Element("Points")?.Elements("Point") ?? Enumerable.Empty<XElement>())
                    {
                        var values = point.Value.Split(',');
                        if (values.Length < 2 || values.Length > 3 || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var time) || !float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                            double.IsNaN(time) || double.IsInfinity(time) || time < 0 || time <= previous || float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1 ||
                            (values.Length == 3 && (!double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var curve) || curve != 0))) { compatible = false; break; }
                        raw.points.Add(new ModulationPoint { time = time / 1000, value = value }); previous = time;
                        if (raw.points.Count > 4096) throw new InvalidDataException("Too many envelope points.");
                    }
                    double length = Integer(envelope, "Length", 72) - 1;
                    if (length < previous || length < 0) compatible = false;
                    // Renoise holds the last authored value to the declared length, which also bounds sustain/loop markers.
                    if (compatible && raw.points.Count > 0 && length > previous) raw.points.Add(new ModulationPoint { time = length / 1000, value = raw.points[raw.points.Count - 1].value });
                    raw.sustainEnabled = Boolean(node, "SustainIsActive", false); raw.sustainPosition = Integer(node, "SustainPos", 0) / 1000.0;
                    raw.loopStart = Integer(node, "LoopStart", 0) / 1000.0; raw.loopEnd = Integer(node, "LoopEnd", 71) / 1000.0;
                    if (!Enum.TryParse(Text(node, "LoopMode", "Off"), out raw.loop)) compatible = false;
                    raw.loopEnabled = raw.loop != SampleLoop.Off;
                    if (raw.sustainEnabled && (raw.sustainPosition < 0 || raw.sustainPosition > length / 1000) || raw.loopEnabled && (raw.loopStart < 0 || raw.loopStart >= raw.loopEnd || raw.loopEnd > length / 1000)) compatible = false;
                    if (!compatible || raw.points.Count == 0)
                    {
                        raw.points.Clear(); raw.sustainEnabled = raw.loopEnabled = false; raw.sustainPosition = 0; raw.loopStart = 0; raw.loopEnd = 1; raw.loop = SampleLoop.Off;
                        Issue(issues, "sample-envelope-shape", "Malformed, curved or out-of-bounds volume envelope is retained inactive.", deviceAt); continue;
                    }
                    raw.enabled = true;
                    Issue(issues, "sample-envelope-approximate", "Volume envelope uses milliseconds and straight segments; source release-decay law and control-rate parity are uncalibrated (target release remains 50 ms).", deviceAt);
                }
            }
        }

        static void FlagInstrument(XElement instrument, XElement root, List<RenoiseIssue> issues, string location)
        {
            var globals = instrument.Element("GlobalProperties");
            if (Boolean(globals, "Monophonic", false) || Integer(globals, "MonophonicGlide", 0) != 0) Issue(issues, "instrument-mono", "Sampler monophonic/glide behavior is not converted.", location);
            if (Text(globals, "Scale", "None") != "None" || Text(globals, "Quantize", "None") != "None" || Boolean(globals, "MtsEspTuning", false) || globals?.Element("Tuning")?.Elements().Any(x => x.Value != "0" && x.Value != "0.0") == true)
                Issue(issues, "instrument-tuning", "Scale, quantize or custom tuning behavior is retained only in source metadata.", location);
            if (instrument.Descendants("Phrase").Any() || instrument.Descendants("Script").Any()) Issue(issues, "instrument-phrases", "Phrases and scripts are retained but never executed by the importer.", location);
            if (instrument.Descendants("PluginIdentifier").Any(x => !string.IsNullOrWhiteSpace(x.Value))) Issue(issues, "instrument-plugin", "Plugin synthesis requires an explicitly chosen replacement instrument.", location);
            if (root.Element("DeviceChains")?.HasElements == true) Issue(issues, "instrument-sample-chains", "Instrument sample effect chains are retained for manual replacement; no routing is inferred.", location);
            if (globals?.Descendants("Mapping").Any() == true || globals?.Descendants("Mappings").Any(x => x.HasElements) == true)
                Issue(issues, "instrument-macros", "Instrument macro destinations are retained in provenance and require manual mapping.", location);
            if (instrument.Element("VolumeEnvelope") != null || instrument.Element("Envelopes") != null || instrument.Element("SampleEnvelopes") != null)
                Issue(issues, "instrument-legacy-envelope", "Legacy envelope format is retained only in source metadata.", location);
        }
        internal static void Issue(List<RenoiseIssue> issues, string code, string message, string location)
        {
            var existing = issues.FirstOrDefault(x => x.Code == code && x.Message == message && x.Location == location);
            if (existing != null) existing.Count++; else issues.Add(new RenoiseIssue { Code = code, Message = message, Location = location, Count = 1 });
        }
        static string Text(XElement e, string key, string fallback) => e?.Element(key)?.Value ?? fallback;
        static int Integer(XElement e, string key, int fallback)
        {
            string s = e?.Element(key)?.Value; if (s == null) return fallback;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) throw new InvalidDataException("Invalid integer " + key);
            return value;
        }
        static float Number(XElement e, string key, float fallback)
        {
            string s = e?.Element(key)?.Value; if (s == null) return fallback;
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Invalid number " + key);
            return value;
        }
        static bool Boolean(XElement e, string key, bool fallback)
        {
            string s = e?.Element(key)?.Value; if (s == null) return fallback;
            if (s == "true" || s == "1") return true; if (s == "false" || s == "0") return false;
            throw new InvalidDataException("Invalid boolean " + key);
        }
    }
}
