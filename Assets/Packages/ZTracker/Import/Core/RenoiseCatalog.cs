using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Laubrary.ZTracker.Renoise
{
    /// <summary>Source evidence only. Suggestions never instantiate or silently substitute a synthesizer.</summary>
    [Serializable]
    public sealed class RenoiseSourceEntry
    {
        public string Key;
        public string Name;
        public string Kind;
        public string PluginIdentifier = "";
        public string Fingerprint = "";
        public string Suggestion = "";
        public string Reason = "";
        public string Status = "Unmapped";
        public string Location = "";
        public string DeviceType = "";
        public string ParentKey = "";
        public int InstrumentIndex = -1;
        public int TrackIndex = -1;
        public int ChainIndex = -1;
        public int DeviceIndex = -1;
        public int NestingDepth;
    }

    /// <summary>Deterministic identities independent of XML formatting and editor view state.</summary>
    public static class RenoiseIdentity
    {
        public static string Hash(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static string Hash(string text) { return Hash(Encoding.UTF8.GetBytes(text ?? "")); }

        public static string Fingerprint(XElement element)
        {
            return Hash(element == null ? "" : Canonical(element));
        }

        private static string Canonical(XElement element)
        {
            // Length-prefixed tokens avoid ambiguities; sorting attributes ignores irrelevant serialization order.
            var text = new StringBuilder();
            Canonical(element, text);
            return text.ToString();
        }

        private static void Token(StringBuilder text, string value)
        {
            text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        }

        private static void Canonical(XElement element, StringBuilder text)
        {
            Token(text, element.Name.ToString());
            foreach (var attribute in element.Attributes().OrderBy(a => a.Name.ToString(), StringComparer.Ordinal))
            {
                Token(text, attribute.Name.ToString()); Token(text, attribute.Value);
            }
            text.Append('{');
            if (!element.HasElements) Token(text, element.Value.Trim());
            else foreach (var child in element.Elements()) Canonical(child, text);
            text.Append('}');
        }

        public static XElement PluginDevice(XElement instrumentOrDevice)
        {
            if (instrumentOrDevice == null) return null;
            if (instrumentOrDevice.Element("PluginIdentifier") != null || instrumentOrDevice.Element("PluginDisplayName") != null)
                return instrumentOrDevice;
            return RenoiseXml.Element(instrumentOrDevice, "PluginGenerator/PluginDevice");
        }

        public static string PluginIdentifier(XElement instrumentOrDevice)
        {
            var plugin = PluginDevice(instrumentOrDevice);
            if (plugin == null) return "";
            var id = RenoiseXml.Text(plugin, "PluginIdentifier").Trim();
            return id.Length == 0 ? "" : RenoiseXml.Text(plugin, "PluginType", "Unknown") + ":" + id;
        }

        public static string PluginName(XElement instrumentOrDevice)
        {
            var plugin = PluginDevice(instrumentOrDevice);
            if (plugin == null) return "";
            string name = RenoiseXml.Text(plugin, "PluginDisplayName").Trim();
            if (name.Length == 0) name = RenoiseXml.Text(plugin, "PluginShortDisplayName").Trim();
            return name.Length == 0 ? PluginIdentifier(plugin) : name;
        }

        public static string PatchFingerprint(XElement instrumentOrDevice)
        {
            var plugin = PluginDevice(instrumentOrDevice);
            if (plugin == null) return "";
            var state = new XElement("State");
            // Include program and both actual state forms; omit display names, plugin window position and selection.
            foreach (string name in new[] { "ActiveProgram", "ParameterChunkType", "ParameterChunk", "Parameters" })
            {
                var value = plugin.Element(name);
                if (value != null) state.Add(new XElement(value));
            }
            var chunk = state.Element("ParameterChunk");
            if (chunk != null) chunk.Value = string.Concat(chunk.Value.Where(c => !char.IsWhiteSpace(c)));
            return Fingerprint(state);
        }

        public static string InstrumentKey(XElement instrument, int slot)
        {
            var plugin = PluginDevice(instrument);
            string id = PluginIdentifier(instrument);
            if (plugin != null && (id.Length > 0 || PluginName(plugin).Length > 0))
                return "instrument:plugin:" + Hash((id.Length == 0 ? "name:" + PluginName(plugin) : id) + "|" + PatchFingerprint(plugin));
            string name = RenoiseXml.Text(instrument, "Name").Trim();
            var samples = RenoiseXml.Element(instrument, "SampleGenerator/Samples");
            if (samples != null && samples.HasElements)
            {
                // The source's name/keymap identifies the role; sound-design edits do not change the identity.
                var map = new XElement("SampleMap", samples.Elements().Select(s => new XElement("Sample",
                    new XElement("Name", RenoiseXml.Text(s, "Name")),
                    s.Element("Mapping") == null ? null : new XElement(s.Element("Mapping")))));
                return "instrument:sample:" + Hash(name + "|" + Fingerprint(map));
            }
            return "instrument:empty:" + Hash(name.Length == 0 ? "slot:" + slot.ToString(CultureInfo.InvariantCulture) : name);
        }

        public static string DeviceType(XElement device)
        {
            return ((string)device.Attribute("type") ?? device.Name.LocalName) +
                (PluginIdentifier(device).Length == 0 ? "" : "|" + PluginIdentifier(device));
        }

        public static string ChainFingerprint(XElement chain)
        {
            var devices = chain == null ? null : chain.Element("Devices");
            return Hash(devices == null ? "" : string.Join("\n", devices.Elements().Select(DeviceType)));
        }

        public static string ChainKey(XElement chain, string fallbackScope)
        {
            string name = RenoiseXml.Text(chain, "Name").Trim();
            return "chain:" + Hash((fallbackScope ?? "") + "|" + (name.Length == 0 ? "types:" + ChainFingerprint(chain) : "name:" + name));
        }

        public static string DeviceKey(XElement device, int occurrence)
        {
            return "device:" + Hash(DeviceType(device)) + ":" + occurrence.ToString(CultureInfo.InvariantCulture);
        }
    }

    public sealed class RenoiseCatalog
    {
        public List<RenoiseSourceEntry> Entries { get; private set; } = new List<RenoiseSourceEntry>();
        public List<RenoiseIssue> Issues { get; private set; } = new List<RenoiseIssue>();
        private readonly HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);

        public static RenoiseCatalog Create(RenoiseArchive archive)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            var result = new RenoiseCatalog();
            result.Issues.AddRange(archive.Issues);
            var root = archive.Xml.Root;
            var instruments = archive.IsSong ? RenoiseXml.Element(root, "Instruments")?.Elements().ToArray() ?? new XElement[0] : new[] { root };
            var instrumentKeys = instruments.Select((instrument, index) => RenoiseIdentity.InstrumentKey(instrument, index)).ToArray();
            var duplicateKeys = new HashSet<string>(instrumentKeys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.Ordinal);
            for (int i = 0; i < instruments.Length; i++)
            {
                string key = instrumentKeys[i];
                // Distinct labels give identical plugin patches distinct compositional roles without slot dependence.
                if (duplicateKeys.Contains(key)) key += ":name:" + RenoiseIdentity.Hash(RenoiseXml.Text(instruments[i], "Name"));
                result.AddInstrument(instruments[i], i, key);
            }
            var tracks = archive.IsSong ? RenoiseXml.Element(root, "Tracks")?.Elements().ToArray() ?? new XElement[0] : new XElement[0];
            for (int i = 0; i < tracks.Length; i++)
            {
                var chain = tracks[i].Element("FilterDevices");
                if (chain == null) continue;
                string track = RenoiseXml.Text(tracks[i], "Name", "Track " + i);
                result.AddChain(chain, "track:" + track, track, -1, i, 0, "Tracks/" + i + "/FilterDevices");
            }
            result.AddCapabilities(root);
            return result;
        }

        private string Unique(string key, string location)
        {
            if (keys.Add(key)) return key;
            // Ambiguous duplicates cannot be matched reliably after reordering; expose that fact rather than merge.
            AddIssue("AmbiguousIdentity", "Identical source identities need their source position to distinguish them; review mappings if these duplicates are reordered.", location);
            string duplicate = key + ":at:" + location;
            if (!keys.Add(duplicate)) throw new InvalidOperationException("Duplicate catalog address: " + location);
            return duplicate;
        }

        private void AddInstrument(XElement instrument, int index, string key)
        {
            string location = "Instruments/" + index;
            string name = RenoiseXml.Text(instrument, "Name", "Instrument " + index);
            var plugin = RenoiseIdentity.PluginDevice(instrument);
            bool hasPlugin = plugin != null && (RenoiseIdentity.PluginIdentifier(plugin).Length > 0 || RenoiseIdentity.PluginName(plugin).Length > 0);
            bool hasSamples = RenoiseXml.Element(instrument, "SampleGenerator/Samples")?.HasElements ?? false;
            var entry = new RenoiseSourceEntry
            {
                Key = Unique(key, location),
                Kind = "Instrument", Name = name, InstrumentIndex = index, Location = location,
                PluginIdentifier = RenoiseIdentity.PluginIdentifier(instrument),
                Fingerprint = hasPlugin ? RenoiseIdentity.PatchFingerprint(plugin) : RenoiseIdentity.Fingerprint(instrument),
                Status = hasSamples && !hasPlugin ? "Approximate" : "Unmapped",
                Suggestion = hasSamples && !hasPlugin ? "Sampler" : SuggestPlugin(RenoiseIdentity.PluginName(plugin)),
                Reason = hasSamples && !hasPlugin ? "Import the recorded samples and supported key/velocity mappings; review playback features." : "Choose a ZTracker instrument for this source; plugin state is retained, not executed."
            };
            if (!hasSamples && !hasPlugin) { entry.Suggestion = "Choose instrument or mark dealt with"; entry.Reason = "No directly importable sample or identified plugin generator was found."; }
            Entries.Add(entry);
            if (hasPlugin)
            {
                Entries.Add(new RenoiseSourceEntry
                {
                    Key = entry.Key + ":plugin", ParentKey = entry.Key, Kind = "Plugin", Name = RenoiseIdentity.PluginName(plugin),
                    InstrumentIndex = index, PluginIdentifier = entry.PluginIdentifier, Fingerprint = entry.Fingerprint,
                    Location = location + "/PluginGenerator/PluginDevice", Suggestion = entry.Suggestion,
                    Reason = "Plugin identifier and patch-state fingerprint identify this generator; sound recreation is manual."
                });
                AddIssue("PluginSubstitution", "Plugin synthesis needs an explicit ZTracker replacement; saved state is retained.", location);
            }
            if (hasPlugin && hasSamples) AddIssue("MixedGenerators", "The source contains plugin and sample generators; one target instrument may not reproduce both.", location);
            if (RenoiseXml.Int(instrument, "PluginGenerator/AliasInstrumentIndex", -1) >= 0 || RenoiseXml.Text(instrument, "PluginGenerator/AliasFxIndices", "-1,-1") != "-1,-1")
                AddIssue("PluginAlias", "This instrument routes to another plugin instance; choose its replacement explicitly and review output routing.", location);
            var chains = RenoiseXml.Element(instrument, "SampleGenerator/DeviceChains");
            if (chains != null)
            {
                int chainIndex = 0;
                foreach (var chain in chains.Elements())
                {
                    AddChain(chain, entry.Key, name, index, -1, chainIndex, location + "/SampleGenerator/DeviceChains/" + chainIndex);
                    chainIndex++;
                }
            }
        }

        private static string SuggestPlugin(string name)
        {
            name = (name ?? "").ToLowerInvariant();
            if (name.Contains("opn") || name.Contains("dexed") || name.Contains("fm")) return "FM instrument";
            if (name.Contains("redux") || name.Contains("sampl")) return "Sampler (export an XRNI for sample import)";
            return "Synth instrument";
        }

        private void AddChain(XElement chain, string ownerKey, string ownerName, int instrument, int track, int chainIndex, string location, int nestingDepth = 0)
        {
            var devices = chain.Element("Devices");
            if (devices == null || !devices.HasElements) return;
            string name = RenoiseXml.Text(chain, "Name").Trim();
            var entry = new RenoiseSourceEntry
            {
                Key = Unique(RenoiseIdentity.ChainKey(chain, ownerKey), location), ParentKey = ownerKey,
                Kind = "EffectChain", Name = name.Length > 0 ? name : ownerName + " effects",
                InstrumentIndex = instrument, TrackIndex = track, ChainIndex = chainIndex, Location = location,
                NestingDepth = nestingDepth,
                Fingerprint = RenoiseIdentity.ChainFingerprint(chain), Suggestion = "Recreate supported effects in source order",
                Reason = name.Length > 0 ? "Matched by the chain's saved name within its owner." : "Matched by device types and plugin identifiers in order within its owner."
            };
            Entries.Add(entry);
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            int index = 0;
            foreach (var device in devices.Elements())
            {
                string type = RenoiseIdentity.DeviceType(device);
                int occurrence;
                occurrences.TryGetValue(type, out occurrence);
                occurrences[type] = occurrence + 1;
                string customName = RenoiseXml.Text(device, "CustomDeviceName").Trim();
                string pluginName = RenoiseIdentity.PluginName(device);
                var row = new RenoiseSourceEntry
                {
                    Key = entry.Key + ":" + RenoiseIdentity.DeviceKey(device, occurrence), ParentKey = entry.Key,
                    Kind = "Device", Name = pluginName.Length > 0 ? pluginName : customName.Length > 0 ? customName : device.Name.LocalName,
                    InstrumentIndex = instrument, TrackIndex = track, ChainIndex = chainIndex, DeviceIndex = index,
                    NestingDepth = nestingDepth,
                    Location = location + "/Devices/" + index, DeviceType = (string)device.Attribute("type") ?? device.Name.LocalName,
                    PluginIdentifier = RenoiseIdentity.PluginIdentifier(device), Fingerprint = RenoiseIdentity.Fingerprint(device),
                    Suggestion = "Recreate manually or mark dealt with", Reason = "Only explicitly supported device parameters are converted; original device XML remains in the source archive."
                };
                Entries.Add(row);
                // Splitter/subchain contents are visible too, but never mistaken for a top-level device address.
                foreach (var nested in device.Elements().Where(e => e.Element("Devices") != null))
                    AddChain(nested, row.Key, row.Name, instrument, track, chainIndex, row.Location + "/" + nested.Name.LocalName, nestingDepth + 1);
                index++;
            }
        }

        private void AddCapabilities(XElement root)
        {
            foreach (var element in root.Descendants())
            {
                switch (element.Name.LocalName)
                {
                    case "ShuffleIsActive":
                        if (element.Value == "true" || element.Value == "1") AddIssue("Groove", "Active groove timing is not reproduced; disable groove in the source for accurate timing.", "GlobalSongData");
                        break;
                    case "Phrase": AddIssue("Phrase", "Instrument phrases are retained but not expanded into song notes.", "Instruments"); break;
                    case "ScriptDevice": AddIssue("Script", "Imported scripts are never executed.", "Instruments"); break;
                    case "MidiGenerator":
                        if (RenoiseXml.Text(element, "DeviceName").Length > 0) AddIssue("ExternalMidi", "External MIDI instruments need an explicit target replacement.", "Instruments");
                        break;
                }
            }
        }

        private void AddIssue(string code, string message, string location)
        {
            // One row per cause, with the first useful location and a count; no per-note diagnostic spam.
            var existing = Issues.Find(i => i.Code == code && i.Message == message);
            if (existing == null) Issues.Add(new RenoiseIssue(code, message, location));
            else existing.Count++;
        }
    }
}
