using System;
using System.Collections.Generic;
using System.IO;
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
    public sealed class RenoiseImportPlan
    {
        public RenoiseArchive archive;
        public string path, identity, blockedReason;
        public SongData song;
        public List<RenoiseSongDevice> devices = new List<RenoiseSongDevice>();
        public List<RenoiseMapping> mappings;
        public List<RenoiseIssue> issues = new List<RenoiseIssue>();
        public List<RenoisePatternStamp> patterns = new List<RenoisePatternStamp>();
        public List<string> changes = new List<string>();
    }

    public static class RenoiseImportService
    {
        public static RenoiseImportLink Find(Object asset)
        {
            if (asset == null) return null;
            return AssetDatabase.FindAssets("t:RenoiseImportLink").Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<RenoiseImportLink>)
                .FirstOrDefault(x => x != null && (x.song == asset || (asset is ZTrackerInstrument instrument && x.instruments.Contains(instrument))));
        }

        public static RenoiseImportPlan Inspect(string path, RenoiseImportLink previous = null)
        {
            if (previous != null) ValidateLink(previous);
            var archive = RenoiseArchive.Read(path);
            if (previous != null && previous.sourceKind != (archive.IsSong ? "XRNS" : "XRNI"))
                throw new InvalidOperationException("A linked song cannot become an instrument, or vice versa. Create a separate import for that source.");
            var plan = new RenoiseImportPlan { archive = archive, path = Path.GetFullPath(path), identity = previous != null ? previous.sourceIdentity : Guid.NewGuid().ToString("N") };
            plan.issues.AddRange(archive.Issues);
            if (archive.IsSong)
            {
                try
                {
                    var conversion = RenoiseSongConverter.Convert(archive, plan.identity);
                    plan.song = conversion.Song;
                    plan.devices = conversion.Devices;
                    plan.issues.AddRange(conversion.Issues);
                }
                catch (FormatException error)
                {
                    plan.blockedReason = error.Message;
                    plan.issues.Add(new RenoiseIssue("INSPECTION_ONLY", "Import is unavailable: " + error.Message, "Song"));
                }
            }
            var catalog = RenoiseCatalog.Create(archive);
            if (catalog.Entries.Count > 16384) throw new InvalidDataException("The source has more than 16,384 mapping entries; split it before importing.");
            // A sampler's display label is not sound-state evidence. This enables safe rename matching.
            var sourceInstruments = SourceInstruments(archive).ToArray();
            foreach (var entry in catalog.Entries.Where(e => e.Kind == "Instrument" && string.IsNullOrEmpty(e.PluginIdentifier)))
            {
                var state = new XElement(sourceInstruments[entry.InstrumentIndex]); state.Element("Name")?.Remove();
                entry.Fingerprint = RenoiseIdentity.Fingerprint(state);
            }
            plan.issues.AddRange(catalog.Issues.Where(x => !archive.Issues.Contains(x)));
            plan.mappings = Reconcile(catalog.Entries, previous?.mappings, plan.changes);
            if (previous != null) PreserveTargetIssues(previous, plan);
            var remappedTracks = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var m in plan.mappings.Where(x => !x.removed && x.source.TrackIndex >= 0 && x.source.NestingDepth == 0))
            {
                var oldTrack = previous?.song?.model?.tracks.FirstOrDefault(t => t.id == m.targetTrackId);
                if (m.source.Kind == "EffectChain" && oldTrack != null && m.chainSnapshot != null && m.chainOwner == null && !m.bypass && JsonUtility.ToJson(oldTrack.devices) != JsonUtility.ToJson(m.chainSnapshot))
                { m.chainOwner = previous.song; m.chainId = oldTrack.id; m.explicitTarget = true; plan.changes.Add(m.source.Name + ": local chain edits retained as a replacement."); }
                // Snapshot a self-reference before the source song's tracks are replaced/reordered.
                if (m.source.Kind == "EffectChain" && m.chainOwner == previous?.song && m.chainOwner != null)
                {
                    var chosen = ResolveChain(m);
                    if (chosen != null) m.chainSnapshot = ZTrackerMigration.Copy(chosen);
                }
                if (plan.song != null && m.source.TrackIndex < plan.song.tracks.Count)
                {
                    string nextTrack = plan.song.tracks[m.source.TrackIndex].id;
                    if (!string.IsNullOrEmpty(m.targetTrackId)) remappedTracks[m.targetTrackId] = nextTrack;
                    m.targetTrackId = nextTrack;
                }
            }
            foreach (var m in plan.mappings.Where(m => !m.removed && m.chainOwner != null && m.chainOwner == previous?.song))
                m.chainId = remappedTracks.TryGetValue(m.chainId ?? "", out var next) ? next : "";
            if (plan.song != null) PopulateUsage(plan.song, plan.mappings);
            var patterns = archive.Xml.Root?.Element("PatternPool")?.Element("Patterns")?.Elements("Pattern") ?? Enumerable.Empty<XElement>();
            int index = 0;
            foreach (var pattern in patterns) plan.patterns.Add(new RenoisePatternStamp { index = index++, fingerprint = RenoiseIdentity.Fingerprint(pattern) });
            if (previous != null)
            {
                int changed = plan.patterns.Count(p => previous.patterns.Any(x => x.index == p.index && x.fingerprint != p.fingerprint));
                int added = plan.patterns.Count(p => previous.patterns.All(x => x.index != p.index));
                int removed = previous.patterns.Count(p => plan.patterns.All(x => x.index != p.index));
                plan.changes.Add($"Patterns: {changed} changed, {added} added, {removed} removed.");
                if (previous.sourceHash == archive.Hash) plan.changes.Add("Source bytes unchanged.");
                plan.changes.Add("Arrangement follows the source; existing mapped instruments keep their ZTracker sound design.");
            }
            SetInitialStatuses(plan);
            return plan;
        }

        internal static List<RenoiseMapping> Reconcile(List<RenoiseSourceEntry> entries, List<RenoiseMapping> old, List<string> changes)
        {
            old ??= new List<RenoiseMapping>();
            old = old.Where(x => x != null && x.source != null).ToList();
            var used = new HashSet<RenoiseMapping>();
            var result = new List<RenoiseMapping>();
            var assigned = new Dictionary<RenoiseSourceEntry, RenoiseMapping>();
            var oldParent = new Dictionary<string, string>(StringComparer.Ordinal);
            // Reserve exact matches first, so a fallback cannot steal an identity appearing later in the list.
            foreach (var entry in entries)
            {
                if (AmbiguousAncestor(entry, entries)) continue;
                string family = IdentityFamily(entry.Key);
                if (entries.Count(e => e.Kind == entry.Kind && IdentityFamily(e.Key) == family) > 1) continue;
                if (entry.Kind == "Plugin" && entries.Count(e => e.Kind == "Instrument" && IdentityFamily(e.Key) == IdentityFamily(entry.ParentKey)) > 1) continue;
                var exact = old.Where(x => !used.Contains(x) && x.source.Key == entry.Key && x.source.Kind == entry.Kind).ToList();
                if (exact.Count != 1) continue;
                if (entry.Kind == "Device" && exact[0].source.Fingerprint != entry.Fingerprint && entries.Count(e => e.Kind == "Device" && e.ParentKey == entry.ParentKey && e.DeviceType == entry.DeviceType) > 1) continue;
                assigned[entry] = exact[0]; used.Add(exact[0]);
            }
            foreach (var entry in entries)
            {
                assigned.TryGetValue(entry, out var prior);
                string match = "Exact source identity";
                bool parentUnmatched = !string.IsNullOrEmpty(entry.ParentKey) && entries.Any(e => e.Key == entry.ParentKey) && !oldParent.ContainsKey(entry.ParentKey);
                if (prior == null && !parentUnmatched)
                {
                    string parent = oldParent.TryGetValue(entry.ParentKey ?? "", out var previousParent) ? previousParent : entry.ParentKey;
                    // Child rows follow their matched parent, including plugin patch/name changes.
                    if (entry.Kind == "Plugin")
                        prior = UniquePrior(entry, old, entries, used, (a, b) => a.ParentKey == parent && b.ParentKey == entry.ParentKey && a.PluginIdentifier == b.PluginIdentifier);
                    if (prior == null && entry.Kind == "Instrument")
                        prior = UniquePrior(entry, old, entries, used, (a, b) => a.PluginIdentifier == b.PluginIdentifier && a.Name == b.Name);
                    if (prior == null && entry.Kind == "Instrument" && !string.IsNullOrEmpty(entry.Fingerprint))
                        prior = UniquePrior(entry, old, entries, used, (a, b) => a.PluginIdentifier == b.PluginIdentifier && a.Fingerprint == b.Fingerprint);
                    if (prior == null && entry.Kind == "Instrument" && !string.IsNullOrEmpty(entry.PluginIdentifier))
                        prior = UniquePrior(entry, old, entries, used, (a, b) => a.PluginIdentifier == b.PluginIdentifier);
                    if (prior == null && entry.Kind == "EffectChain")
                        prior = UniquePrior(entry, old, entries, used, (a, b) => a.ParentKey == parent && b.ParentKey == entry.ParentKey && a.NestingDepth == b.NestingDepth);
                    if (prior == null && entry.Kind == "EffectChain" && !string.IsNullOrEmpty(entry.Fingerprint))
                        prior = UniquePrior(entry, old, entries, used, (a, b) => a.Fingerprint == b.Fingerprint && a.NestingDepth == b.NestingDepth && (a.TrackIndex >= 0) == (b.TrackIndex >= 0));
                    if (prior == null && entry.Kind == "Device")
                    {
                        prior = UniquePrior(entry, old, entries, used, (a, b) => a.ParentKey == parent && b.ParentKey == entry.ParentKey && a.DeviceType == b.DeviceType && a.Fingerprint == b.Fingerprint);
                        if (prior == null) prior = UniquePrior(entry, old, entries, used, (a, b) => a.ParentKey == parent && b.ParentKey == entry.ParentKey && a.DeviceType == b.DeviceType && a.PluginIdentifier == b.PluginIdentifier);
                    }
                    if (prior != null) match = "Unique source role or unchanged state; identity details changed";
                }
                RenoiseMapping mapping;
                if (prior != null)
                {
                    mapping = CopyMapping(prior); used.Add(prior);
                    if (prior.source.Fingerprint != entry.Fingerprint) changes.Add(entry.Name + ": source settings changed; target choice retained.");
                    if (prior.source.Name != entry.Name) changes.Add(prior.source.Name + " renamed to " + entry.Name + "; target choice retained.");
                    oldParent[entry.Key] = prior.source.Key;
                }
                else { mapping = new RenoiseMapping(); changes.Add(entry.Kind + " added: " + entry.Name); match = "New source identity"; }
                mapping.source = entry; mapping.removed = false; mapping.matchedBy = match;
                mapping.notes = mapping.rows = 0; mapping.lowest = mapping.highest = -1; mapping.tracks.Clear(); mapping.uses.Clear();
                result.Add(mapping);
            }
            foreach (var previous in old.Where(x => !used.Contains(x)))
            {
                var retained = CopyMapping(previous); retained.removed = true; result.Add(retained);
                if (!previous.removed) changes.Add(previous.source.Kind + " removed: " + previous.source.Name + "; choice retained in history.");
            }
            return result;
        }

        static string IdentityFamily(string key)
        {
            int at = (key ?? "").IndexOf(":at:", StringComparison.Ordinal);
            return at < 0 ? key : key.Substring(0, at);
        }

        static bool AmbiguousAncestor(RenoiseSourceEntry entry, List<RenoiseSourceEntry> entries)
        {
            for (var current = entry; current != null; current = entries.FirstOrDefault(e => e.Key == current.ParentKey))
            {
                string family = IdentityFamily(current.Key);
                if (entries.Count(e => e.Kind == current.Kind && IdentityFamily(e.Key) == family) > 1) return true;
                if (string.IsNullOrEmpty(current.ParentKey)) break;
            }
            return false;
        }

        static void PreserveTargetIssues(RenoiseImportLink previous, RenoiseImportPlan plan)
        {
            foreach (var mapping in plan.mappings.Where(m => !m.removed && m.source.Kind == "Instrument" && m.instrument != null))
            {
                foreach (var prior in previous.mappings.Where(m => !m.removed && m.source.Kind == "Instrument" && m.instrument == mapping.instrument))
                {
                    string oldPrefix = "instrument[" + prior.source.InstrumentIndex + "]";
                    string newPrefix = "instrument[" + mapping.source.InstrumentIndex + "]";
                    foreach (var issue in previous.issues.Where(i => i.Code != "sample-no-active" && i.Location != null && i.Location.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        string location = newPrefix + issue.Location.Substring(oldPrefix.Length);
                        if (!plan.issues.Any(i => i.Code == issue.Code && i.Message == issue.Message && i.Location == location))
                            plan.issues.Add(new RenoiseIssue(issue.Code, issue.Message, location, issue.Count));
                    }
                }
            }
        }

        static RenoiseMapping UniquePrior(RenoiseSourceEntry entry, List<RenoiseMapping> old, List<RenoiseSourceEntry> entries,
            HashSet<RenoiseMapping> used, Func<RenoiseSourceEntry, RenoiseSourceEntry, bool> same)
        {
            var candidates = old.Where(m => !m.removed && !used.Contains(m) && m.source.Kind == entry.Kind && same(m.source, entry)).ToList();
            if (candidates.Count != 1) return null;
            return entries.Count(e => e.Kind == entry.Kind && same(candidates[0].source, e)) == 1 ? candidates[0] : null;
        }

        static RenoiseMapping CopyMapping(RenoiseMapping m) => new RenoiseMapping
        {
            source = m.source, status = m.status, dealtWith = m.dealtWith, removed = m.removed, explicitTarget = m.explicitTarget,
            bypass = m.bypass, detail = m.detail, matchedBy = m.matchedBy, instrument = m.instrument, chainOwner = m.chainOwner,
            chainId = m.chainId, chainSnapshot = m.chainSnapshot == null ? null : ZTrackerMigration.Copy(m.chainSnapshot), targetTrackId = m.targetTrackId
        };

        static void SetInitialStatuses(RenoiseImportPlan plan)
        {
            foreach (var m in plan.mappings.Where(x => !x.removed))
            {
                bool instrument = m.source.Kind == "Instrument" || m.source.Kind == "Plugin";
                if (instrument)
                {
                    m.status = m.instrument != null ? (m.explicitTarget || m.status == RenoiseMappingStatus.Approximate ? RenoiseMappingStatus.Approximate : RenoiseMappingStatus.Mapped) : RenoiseMappingStatus.Unmapped;
                    m.detail = m.instrument != null ? "Existing target sound is retained." : "Silent until a sampler can be imported or a replacement is chosen.";
                    if (m.instrument != null && !HasPlayableSampler(m.instrument)) { m.status = RenoiseMappingStatus.Unmapped; m.detail = "Sampler has no active playable sample zones; choose or edit its target."; }
                }
                else if (m.chainOwner != null || m.bypass) { m.status = RenoiseMappingStatus.Approximate; m.detail = m.bypass ? "Explicit dry replacement." : "Chosen ZTracker chain replaces the source chain."; }
                else
                {
                    var track = plan.song != null && m.source.TrackIndex >= 0 && m.source.TrackIndex < plan.song.tracks.Count ? plan.song.tracks[m.source.TrackIndex] : null;
                    var source = plan.devices.FirstOrDefault(d => d.TrackIndex == m.source.TrackIndex && d.DeviceIndex == m.source.DeviceIndex);
                    bool supported = m.source.NestingDepth == 0 && (m.source.Kind == "Device" ? source != null && source.Mapped :
                        track != null && plan.devices.Where(d => d.TrackIndex == m.source.TrackIndex).All(d => d.Mapped));
                    m.status = supported ? RenoiseMappingStatus.Mapped : RenoiseMappingStatus.Unmapped;
                    m.detail = supported ? "Converted by the restricted native profile." : "Preserved source; unsupported audio/control is inactive.";
                }
            }
        }

        public static void PopulateUsage(SongData song, List<RenoiseMapping> mappings)
        {
            var instrumentRows = mappings.Where(m => !m.removed && m.source.InstrumentIndex >= 0).GroupBy(m => m.source.InstrumentIndex).ToDictionary(g => g.Key, g => g.ToList());
            var trackRows = mappings.Where(m => !m.removed && m.source.InstrumentIndex < 0 && m.source.TrackIndex >= 0).GroupBy(m => m.source.TrackIndex).ToDictionary(g => g.Key, g => g.ToList());
            var selected = new Dictionary<string, int>();
            long total = 0, visited = 0;
            for (int seq = 0; seq < song.sequence.Count; seq++)
            {
                var slot = song.sequence[seq]; int pi = song.patterns.FindIndex(p => p.id == slot.patternId); if (pi < 0) continue;
                var pattern = song.patterns[pi];
                for (int ti = 0; ti < song.tracks.Count; ti++)
                {
                    var track = song.tracks[ti]; var pt = pattern.tracks.Find(t => t.trackId == track.id); if (pt == null) continue;
                    var usedRows = new HashSet<string>();
                    foreach (var line in pt.lines.OrderBy(l => l.line)) foreach (var note in line.notes.OrderBy(n => n.column))
                    {
                        if (++visited > 8000000) throw new FormatException("Usage analysis exceeds eight million source cells; split the song before importing.");
                        if (note.inactive || slot.mutedTrackIds.Contains(track.id) || track.triggerMute) continue;
                        string column = ti + "/" + note.column;
                        if (note.instrumentPresent) selected[column] = note.instrument;
                        if (note.note != NoteKind.Note || !selected.TryGetValue(column, out int instrument)) continue;
                        total++;
                        instrumentRows.TryGetValue(instrument, out var instrumentTargets);
                        trackRows.TryGetValue(ti, out var trackTargets);
                        var targets = (instrumentTargets ?? Enumerable.Empty<RenoiseMapping>()).Concat(trackTargets ?? Enumerable.Empty<RenoiseMapping>());
                        foreach (var m in targets)
                        {
                            m.notes++; if (usedRows.Add(m.source.Key + "/" + line.line)) m.rows++;
                            m.lowest = m.lowest < 0 ? note.pitch : Math.Min(m.lowest, note.pitch); m.highest = Math.Max(m.highest, note.pitch);
                            if (!m.tracks.Contains(track.name)) m.tracks.Add(track.name);
                            var use = m.uses.FirstOrDefault(x => x.sequence == seq && x.track == ti);
                            if (use == null && m.uses.Count < 1024) { use = new RenoiseUse { sequence = seq, pattern = pi, track = ti, firstRow = line.line, lastRow = line.line, name = pattern.name + " / " + track.name }; m.uses.Add(use); }
                            if (use != null) use.lastRow = Math.Max(use.lastRow, line.line);
                        }
                    }
                }
            }
            foreach (var m in mappings)
            {
                m.songShare = total > 0 ? (float)(m.notes / (double)total) : 0;
                if (m.source.TrackIndex >= 0 && m.source.TrackIndex < song.tracks.Count && m.tracks.Count == 0) m.tracks.Add(song.tracks[m.source.TrackIndex].name);
            }
        }

        /// <summary>Commit an inspected plan. A failed import rolls back only its freshly created revision folder.</summary>
        public static RenoiseImportLink Apply(RenoiseImportPlan plan, string destination, RenoiseImportLink link = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!string.IsNullOrEmpty(plan.blockedReason)) throw new InvalidOperationException("Inspection only: " + plan.blockedReason);
            if (plan.archive == null || plan.mappings == null || (plan.archive.IsSong && plan.song == null)) throw new InvalidOperationException("The import plan is incomplete.");
            if (link != null)
            {
                ValidateLink(link);
                if (link.sourceKind != (plan.archive.IsSong ? "XRNS" : "XRNI") || link.sourceIdentity != plan.identity)
                    throw new InvalidOperationException("This inspected plan belongs to a different linked source.");
            }
            bool fresh = link == null;
            string folder = fresh ? ValidateNewFolder(destination) : link.ownedFolder;
            string revision = folder + "/Revision-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var created = new List<Object>();
            var originalSong = link != null ? link.song : null;
            string originalLinkJson = link != null ? EditorJsonUtility.ToJson(link) : null;
            string originalSongJson = originalSong != null ? EditorJsonUtility.ToJson(originalSong) : null;
            string createdSnapshot = null;
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(fresh ? "Import Renoise" : "Reimport Renoise");
            try
            {
                EnsureFolder(folder); EnsureFolder(revision);
                if (fresh) { link = ScriptableObject.CreateInstance<RenoiseImportLink>(); link.ownedFolder = folder; link.sourceIdentity = plan.identity; created.Add(link); }
                else { Undo.RegisterCompleteObjectUndo(link, "Reimport Renoise"); if (link.song != null) Undo.RegisterCompleteObjectUndo(link.song, "Reimport Renoise"); }
                var sourceInstruments = SourceInstruments(plan.archive).ToList();
                var targets = new List<ZTrackerInstrument>();
                var countedClips = new HashSet<AudioClip>();
                long pcmBytes = 0;
                // Count every retained target first, even if its slot appears after a new sampler.
                foreach (var retained in plan.mappings.Where(m => !m.removed && m.source.Kind == "Instrument").Select(m => m.instrument).Where(i => i != null).Distinct())
                { ValidateInstrument(retained); CountPcm(retained, countedClips, ref pcmBytes); }
                for (int i = 0; i < sourceInstruments.Count; i++)
                {
                    var mapping = plan.mappings.FirstOrDefault(m => !m.removed && m.source.Kind == "Instrument" && m.source.InstrumentIndex == i);
                    var target = mapping?.instrument;
                    bool plugin = plan.mappings.Any(m => !m.removed && m.source.Kind == "Plugin" && m.source.InstrumentIndex == i);
                    if (target == null && mapping != null && !mapping.bypass && !plugin && HasSamples(sourceInstruments[i]))
                    {
                        string sampleFolder = revision + "/Instrument-" + i.ToString("000"); EnsureFolder(sampleFolder);
                        target = RenoiseInstrumentConverter.Convert(plan.archive, sourceInstruments[i], i, sampleFolder, plan.issues, (int)(RenoiseInstrumentAudio.MaximumPcmBytes - pcmBytes));
                        created.Add(target);
                        AssetDatabase.CreateAsset(target, sampleFolder + "/Instrument.asset");
                        Undo.RegisterCreatedObjectUndo(target, "Import Renoise instrument");
                        AssetDatabase.SaveAssetIfDirty(target);
                    }
                    if (target != null) { ValidateInstrument(target); CountPcm(target, countedClips, ref pcmBytes); }
                    bool playable = target != null && HasPlayableSampler(target);
                    if (target != null && !playable && !plan.issues.Any(x => x.Code == "sample-no-active" && x.Location == "instrument[" + i + "]"))
                        plan.issues.Add(new RenoiseIssue("sample-no-active", "The chosen sampler has no active playable sample zones and remains silent.", "instrument[" + i + "]"));
                    targets.Add(target);
                    foreach (var m in plan.mappings.Where(m => !m.removed && m.source.InstrumentIndex == i && (m.source.Kind == "Instrument" || m.source.Kind == "Plugin")))
                    {
                        m.instrument = target;
                        m.status = target == null ? RenoiseMappingStatus.Unmapped : (m.explicitTarget || plugin || m.status == RenoiseMappingStatus.Approximate ? RenoiseMappingStatus.Approximate : RenoiseMappingStatus.Mapped);
                        m.detail = target == null ? "Silent; choose a replacement instrument." : (m.explicitTarget ? "Chosen replacement; source settings do not overwrite it." : "Imported sampler; later sound edits stay in ZTracker.");
                        if (target != null && plan.issues.Any(x => x.Location != null && x.Location.StartsWith("instrument[" + i + "]", StringComparison.OrdinalIgnoreCase))) m.status = RenoiseMappingStatus.Approximate;
                        if (target != null && !playable) { m.status = RenoiseMappingStatus.Unmapped; m.detail = "Sampler has no active playable sample zones; choose or edit its target."; }
                    }
                }
                if (plan.song != null)
                {
                    while (targets.Count < plan.song.instruments.Count) targets.Add(null);
                    plan.song.instruments = targets;
                    ApplyChainChoices(plan.song, plan.mappings, plan.issues, link.song);
                    foreach (var m in plan.mappings.Where(m => !m.removed && m.source.Kind == "EffectChain" && m.source.NestingDepth == 0))
                    {
                        var t = plan.song.tracks.FirstOrDefault(t => t.id == m.targetTrackId);
                        if (t != null) m.chainSnapshot = ZTrackerMigration.Copy(t.devices);
                    }
                    plan.song.provenance = "Renoise " + plan.identity + " | " + plan.path + " | SHA-256 " + plan.archive.Hash;
                    if (link.song == null)
                    {
                        link.song = ScriptableObject.CreateInstance<ZTrackerSong>(); created.Add(link.song);
                        link.song.schemaVersion = 1; link.song.model = plan.song;
                        AssetDatabase.CreateAsset(link.song, folder + "/Song.asset"); Undo.RegisterCreatedObjectUndo(link.song, "Import Renoise song");
                    }
                    else { link.song.schemaVersion = 1; link.song.model = plan.song; }
                    EditorUtility.SetDirty(link.song); AssetDatabase.SaveAssetIfDirty(link.song);
                }
                link.instruments = targets; link.sourcePath = plan.path; link.sourceHash = plan.archive.Hash;
                link.sourceName = Path.GetFileName(plan.path); link.sourceKind = plan.archive.IsSong ? "XRNS" : "XRNI";
                link.mappings = plan.mappings; link.issues = GroupIssues(plan.issues); link.patterns = plan.patterns; link.changes = plan.changes;
                // The immutable snapshot is editor-only and never included in a player by an asset reference.
                string sourceFolder = folder + "/Source~"; Directory.CreateDirectory(sourceFolder);
                link.archivePath = sourceFolder + "/" + plan.archive.Hash + (plan.archive.IsSong ? ".xrns" : ".xrni");
                if (!File.Exists(link.archivePath))
                {
                    using (var output = new FileStream(link.archivePath, FileMode.CreateNew, FileAccess.Write))
                    { createdSnapshot = link.archivePath; output.Write(plan.archive.OriginalBytes, 0, plan.archive.OriginalBytes.Length); }
                }
                if (fresh) { AssetDatabase.CreateAsset(link, folder + "/Import.asset"); Undo.RegisterCreatedObjectUndo(link, "Import Renoise link"); }
                link.ownedAssets = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Where(x => !x.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).Select(x => x.Replace('\\', '/')).ToList();
                EditorUtility.SetDirty(link); AssetDatabase.SaveAssetIfDirty(link);
                Undo.CollapseUndoOperations(group);
                return link;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                if (!fresh && link != null && originalLinkJson != null) EditorJsonUtility.FromJsonOverwrite(originalLinkJson, link);
                if (originalSong != null && originalSongJson != null) EditorJsonUtility.FromJsonOverwrite(originalSongJson, originalSong);
                foreach (var item in created) if (item != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(item))) Object.DestroyImmediate(item);
                if (createdSnapshot != null && File.Exists(createdSnapshot)) File.Delete(createdSnapshot);
                string rollback = fresh ? folder : revision;
                if (AssetDatabase.IsValidFolder(rollback)) AssetDatabase.DeleteAsset(rollback);
                if (!fresh && link != null) { EditorUtility.SetDirty(link); AssetDatabase.SaveAssetIfDirty(link); if (link.song != null) { EditorUtility.SetDirty(link.song); AssetDatabase.SaveAssetIfDirty(link.song); } }
                throw;
            }
        }

        public static List<RenoiseIssue> GroupIssues(IEnumerable<RenoiseIssue> issues) => issues.GroupBy(x => x.Code + "\n" + x.Message)
            .Select(g => new RenoiseIssue { Code = g.First().Code, Message = g.First().Message, Count = g.Sum(x => Math.Max(1, x.Count)), Location = string.Join("; ", g.Select(x => x.Location).Where(x => !string.IsNullOrEmpty(x)).Distinct().Take(8)) }).ToList();

        internal static IEnumerable<XElement> SourceInstruments(RenoiseArchive archive) => archive.IsSong ? archive.Xml.Root.Element("Instruments")?.Elements("Instrument") ?? Enumerable.Empty<XElement>() : new[] { archive.Xml.Root };
        static bool HasSamples(XElement instrument) => (instrument.Element("SampleGenerator")?.Element("Samples") ?? instrument.Element("Samples"))?.Elements("Sample").Any() == true;

        public static AudioEffectChainData ResolveChain(RenoiseMapping mapping)
        {
            if (mapping.bypass) return new AudioEffectChainData();
            if (mapping.chainOwner is ZTrackerSong song && song.schemaVersion == 1) return song.model?.tracks.FirstOrDefault(t => t.id == mapping.chainId)?.devices;
            if (mapping.chainOwner is ZTrackerInstrument instrument && instrument.schemaVersion == 1 && int.TryParse(mapping.chainId, out int index) && index >= 0 && index < instrument.model.fxChains.Count) return instrument.model.fxChains[index];
            return null;
        }

        internal static void ApplyChainChoices(SongData song, List<RenoiseMapping> mappings, List<RenoiseIssue> issues, ZTrackerSong destinationOwner = null)
        {
            foreach (var m in mappings.Where(x => !x.removed && x.source.Kind == "EffectChain" && (x.chainOwner != null || x.bypass)))
            {
                if (m.source.NestingDepth != 0) continue;
                var track = song.tracks.FirstOrDefault(t => t.id == m.targetTrackId);
                if (track == null) continue;
                var chain = destinationOwner != null && m.chainOwner == destinationOwner && m.chainSnapshot != null && !m.bypass ? m.chainSnapshot : ResolveChain(m);
                if (chain == null) { m.status = RenoiseMappingStatus.Unmapped; m.detail = "Chosen chain is missing; source conversion retained."; continue; }
                track.devices = ZTrackerMigration.Copy(chain);
                // Source device addresses cannot target a different topology by numerical coincidence.
                foreach (var d in track.sourceDevices) if (d.kind == SourceDeviceKind.AudioChain) { d.kind = SourceDeviceKind.Unsupported; d.parameters.Clear(); }
                foreach (var pt in song.patterns.SelectMany(p => p.tracks).Where(t => t.trackId == track.id))
                    foreach (var lane in pt.automation.Where(l => l.target.kind == ParameterKind.Device)) { lane.enabled = false; lane.unsupported = true; lane.diagnostic = "Source chain replaced; rebind this lane explicitly."; }
                if (m.bypass) track.sends.Clear();
                else foreach (var send in track.sends) send.devicePosition = Math.Min(send.devicePosition, track.devices.nodes.Count);
                m.chainSnapshot = ZTrackerMigration.Copy(chain); m.status = RenoiseMappingStatus.Approximate;
                issues.Add(new RenoiseIssue { Code = "CHAIN_REPLACED", Location = m.source.Location, Message = m.bypass ? "Dry replacement disables source effects, sends and device automation." : "Chosen chain replaces source effects; source device automation is inactive and send positions are bounded to the replacement." });
            }
        }

        public static void SetInstrument(RenoiseImportLink link, RenoiseMapping selected, ZTrackerInstrument target)
        {
            ValidateLink(link);
            if (selected == null || selected.removed || !link.mappings.Contains(selected)) throw new InvalidOperationException("Select a current mapping from this import.");
            if (target != null) ValidateInstrument(target);
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(link, "Map Renoise instrument");
            if (link.song != null) Undo.RegisterCompleteObjectUndo(link.song, "Map Renoise instrument");
            int index = selected.source.InstrumentIndex;
            foreach (var row in link.mappings.Where(m => !m.removed && m.source.InstrumentIndex == index && (m.source.Kind == "Instrument" || m.source.Kind == "Plugin")))
            {
                row.instrument = target; row.explicitTarget = true; row.bypass = target == null;
                row.status = target != null && HasPlayableSampler(target) ? RenoiseMappingStatus.Approximate : RenoiseMappingStatus.Unmapped;
                row.detail = target == null ? "Explicitly silent until mapped." : !HasPlayableSampler(target) ? "Sampler has no active playable sample zones; choose or edit its target." : "Chosen replacement; its sound design stays in ZTracker.";
            }
            if (index >= 0 && index < link.instruments.Count) link.instruments[index] = target;
            if (link.song != null && index >= 0 && index < link.song.model.instruments.Count) { link.song.model.instruments[index] = target; EditorUtility.SetDirty(link.song); }
            EditorUtility.SetDirty(link); Undo.CollapseUndoOperations(group);
        }

        static string ValidateNewFolder(string value)
        {
            string canonical = CanonicalOwnedFolder(value), full = Path.GetFullPath(canonical);
            if (Directory.Exists(full) || File.Exists(full)) throw new IOException("Import destination already exists; choose a new folder.");
            return canonical;
        }

        internal static void ValidateLink(RenoiseImportLink link)
        {
            if (link == null || link.version != 1) throw new InvalidOperationException("This import link is missing or uses an unsupported schema version.");
            string folder = CanonicalOwnedFolder(link.ownedFolder);
            string actual = AssetDatabase.GetAssetPath(link).Replace('\\', '/');
            if (string.IsNullOrEmpty(actual) || !string.Equals(Path.GetDirectoryName(actual)?.Replace('\\', '/'), folder, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The import link is not stored in its declared owned folder. Move or repair the complete import before writing.");
            if (string.IsNullOrEmpty(link.sourceIdentity) || (link.sourceKind != "XRNS" && link.sourceKind != "XRNI"))
                throw new InvalidOperationException("The import link has invalid source identity or kind.");
            if (link.song != null)
            {
                if (link.sourceKind != "XRNS" || link.song.schemaVersion != ZTrackerMigration.CurrentVersion || link.song.model == null)
                    throw new InvalidOperationException("The linked song uses an incompatible schema or source kind.");
                if (!string.Equals(AssetDatabase.GetAssetPath(link.song).Replace('\\', '/'), folder + "/Song.asset", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The linked song is outside this import's owned song location.");
            }
            if (!string.IsNullOrEmpty(link.archivePath))
            {
                string snapshot = Path.GetFullPath(link.archivePath), source = Path.GetFullPath(folder + "/Source~") + Path.DirectorySeparatorChar;
                if (!snapshot.StartsWith(source, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Source snapshot is outside the owned import folder.");
                RejectReparsePoints(Path.GetDirectoryName(snapshot));
            }
        }

        static string CanonicalOwnedFolder(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("An owned import folder is required.");
            string full = Path.GetFullPath(value.TrimEnd('/', '\\')), assets = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
            if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a new folder under the project's Assets.");
            string relative = full.Substring(assets.Length).Replace('\\', '/');
            if (new[] { "Packages", "ZTracker", "ZoundsData" }.Any(p => relative.Equals(p, StringComparison.OrdinalIgnoreCase) || relative.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Import folders must be outside package source and existing owner-data folders.");
            RejectReparsePoints(full);
            return "Assets/" + relative;
        }

        static void RejectReparsePoints(string full)
        {
            string assets = Path.GetFullPath("Assets");
            for (var directory = new DirectoryInfo(full); directory != null && directory.FullName.Length >= assets.Length; directory = directory.Parent)
            {
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Import ownership cannot pass through a directory link or junction.");
                if (string.Equals(directory.FullName, assets, StringComparison.OrdinalIgnoreCase)) break;
            }
        }

        static void ValidateInstrument(ZTrackerInstrument instrument)
        {
            if (instrument.schemaVersion != ZTrackerMigration.CurrentVersion || instrument.model == null)
                throw new InvalidOperationException("Choose a ZTracker instrument with the current supported schema; newer schemas are never downgraded by import.");
        }

        static void CountPcm(ZTrackerInstrument instrument, HashSet<AudioClip> counted, ref long bytes)
        {
            if (instrument.model?.sampler?.samples == null) return;
            foreach (var sample in instrument.model.sampler.samples)
            {
                if (sample?.pcm == null || !counted.Add(sample.pcm)) continue;
                bytes += (long)sample.pcm.samples * sample.pcm.channels * sizeof(float);
                if (bytes > RenoiseInstrumentAudio.MaximumPcmBytes) throw new IOException("Imported decoded audio exceeds ZTracker's 256 MiB song budget.");
            }
        }

        static bool HasPlayableSampler(ZTrackerInstrument instrument)
        {
            var model = instrument.model;
            if (model == null) return false;
            if (model.family != InstrumentFamily.Sampler && model.family != InstrumentFamily.DrumKit) return true;
            return model.sampler != null && model.sampler.zones.Any(z => !z.inactive && z.sample >= 0 && z.sample < model.sampler.samples.Count && !model.sampler.samples[z.sample].inactive && model.sampler.samples[z.sample].pcm != null);
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (parent.Length == 0 || parent == path) throw new ArgumentException("Invalid asset folder.");
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
