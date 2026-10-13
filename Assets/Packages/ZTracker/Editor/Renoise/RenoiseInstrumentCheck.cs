using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;

namespace Laubrary.ZTracker.Renoise
{
    /// <summary>Scoped disposable asset checks. Run directly on the editor main thread; never invoke Test Runner.</summary>
    public static class RenoiseInstrumentCheck
    {
        public static string Run()
        {
            string folder = "Assets/__RenoiseInstrumentCheck_" + Guid.NewGuid().ToString("N");
            string absolute = Path.GetFullPath(folder);
            var created = new List<ZTrackerInstrument>(); var report = new StringBuilder(); int passed = 0, failed = 0;
            void Check(string name, Action action)
            {
                try { action(); passed++; report.AppendLine("PASS " + name); }
                catch (Exception error) { failed++; report.AppendLine("FAIL " + name + ": " + error.Message); }
            }
            try
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
                var issues = new List<RenoiseIssue>(); var archive = Fixture();
                var instrument = RenoiseInstrumentConverter.Convert(archive, archive.Xml.Root, 0, folder, issues); created.Add(instrument);
                Check("WAV/FLAC PCM ownership and deduplication", () => {
                    var samples = instrument.model.sampler.samples;
                    Need(samples.Count == 3 && samples[0].pcm != null && samples[0].pcm == samples[1].pcm, "Identical WAV/FLAC PCM did not share one clip.");
                    Need(samples[0].pcm.samples == 96 && samples[0].pcm.frequency == 22050 && samples[0].pcm.channels == 1, "PCM geometry changed.");
                    Need(Directory.GetFiles(absolute, "*.wav").Length == 1, "Duplicate PCM source files created.");
                });
                Check("sample levels, tuning, inclusive zones and loops", () => {
                    var sample = instrument.model.sampler.samples[0]; var zone = instrument.model.sampler.zones[0];
                    Need(sample.volume == .5f && sample.pan == -.5f && sample.transpose == 2 && Math.Abs(sample.fineTuneCents - 50.3937f) < .0001f, "Levels/tuning were not converted.");
                    Need(zone.baseNote == 48 && zone.noteMin == 12 && zone.noteMax == 84 && zone.velocityMin == 20 && zone.velocityMax == 100 && !zone.keyTracking, "Zone mapping changed.");
                    Need(sample.loop == SampleLoop.PingPong && sample.loopStartFrame == 3 && sample.loopEndFrame == 90 && sample.releaseExitsLoop && sample.nna == NewNoteAction.Continue && sample.oneShot, "Loop/playback flags changed.");
                });
                Check("linear volume envelope and unsupported layer reporting", () => {
                    var device = instrument.model.modulation[0].devices[0];
                    Need(device.enabled && device.points.Count == 3 && device.points[1].time == .02 && device.sustainEnabled && device.sustainPosition == .02, "Envelope was lost.");
                    Need(device.loopEnabled && device.loop == SampleLoop.Forward && device.loopEnd == .03, "Envelope loop changed.");
                    Need(instrument.model.sampler.samples[2].inactive && instrument.model.sampler.zones[2].inactive, "Release layer became a note-on layer.");
                    Need(issues.Any(x => x.Code == "sample-release-layer") && issues.Any(x => x.Code == "sample-missing") && issues.Any(x => x.Code == "sample-envelope-approximate"), "Approximation/missing audio reports absent.");
                    Need(ZTrackerModelValidation.Validate(instrument.model) == null, "Converted instrument does not validate.");
                });
                Check("AudioClip references and PCM survive asset unload/reload", () => {
                    string path = folder + "/Instrument.asset";
                    AssetDatabase.CreateAsset(instrument, path); EditorUtility.SetDirty(instrument); AssetDatabase.SaveAssetIfDirty(instrument);
                    string clipPath = AssetDatabase.GetAssetPath(instrument.model.sampler.samples[0].pcm), clipGuid = AssetDatabase.AssetPathToGUID(clipPath);
                    Resources.UnloadAsset(instrument); created.Remove(instrument); instrument = null;
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    instrument = AssetDatabase.LoadAssetAtPath<ZTrackerInstrument>(path);
                    var clip = instrument.model.sampler.samples[0].pcm;
                    Need(clip != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)) == clipGuid, "Persisted clip reference changed.");
                    clip.LoadAudioData(); var pcm = new float[clip.samples];
                    Need(clip.GetData(pcm, 0), "Persisted clip has no PCM.");
                    // Unity's PCM AudioImporter quantizes the float WAV to its native sample representation.
                    // The measured maximum deviation is one signed-16-bit step, including negative samples.
                    for (int frame = 0; frame < pcm.Length; frame++) Need(Math.Abs(pcm[frame] - ((frame * 67) % 2000 - 1000) / 32768f) <= 1f / 32768f, "Persisted PCM exceeds one 16-bit quantization step at frame " + frame);
                    Need(instrument.model.modulation[0].devices[0].enabled && instrument.model.sampler.samples[0].loopEndFrame == 90, "Serialized settings disappeared.");
                });
                Check("unsupported cycle alternatives stay inactive", () => {
                    var copy = Fixture(); copy.Xml.Root.Element("SampleGenerator").Add(new XElement("KeyzoneOverlappingMode", "Cycle"));
                    var converted = RenoiseInstrumentConverter.Convert(copy, copy.Xml.Root, 1, folder, new List<RenoiseIssue>()); created.Add(converted);
                    Need(!converted.model.sampler.zones[0].inactive && converted.model.sampler.zones[1].inactive, "Cycle alternatives were layered simultaneously.");
                });
                Check("unsupported modulation remains present and disabled", () => {
                    var copy = Fixture(); copy.Xml.Descendants("SampleEnvelopeModulationDevice").First().Element("TempoSynced").Value = "true";
                    var converted = RenoiseInstrumentConverter.Convert(copy, copy.Xml.Root, 2, folder, new List<RenoiseIssue>()); created.Add(converted);
                    Need(!converted.model.modulation[0].devices[0].enabled && converted.model.modulation[0].devices[0].rawSource.Contains("TempoSynced"), "Unsupported modulation lost or activated.");
                });
                Check("duplicate source ordinals refuse ambiguity", () => {
                    var copy = Fixture(); copy.Samples.Add("SampleData/Sample00 (other).wav", RenoiseInstrumentAudioCheck.IntegerWave(16));
                    Refuse(() => RenoiseInstrumentConverter.Convert(copy, copy.Xml.Root, 3, folder, new List<RenoiseIssue>()));
                });
                Check("invalid zones refuse without touching existing data", () => {
                    var copy = Fixture(); copy.Xml.Descendants("NoteStart").First().Value = "-1";
                    Refuse(() => RenoiseInstrumentConverter.Convert(copy, copy.Xml.Root, 4, folder, new List<RenoiseIssue>()));
                });
                Check("protected owner folder and traversal are refused", () => {
                    Refuse(() => RenoiseInstrumentConverter.Convert(archive, archive.Xml.Root, 5, "Assets/ZTracker", new List<RenoiseIssue>()));
                    Refuse(() => RenoiseInstrumentConverter.Convert(archive, archive.Xml.Root, 5, folder + "/../ZTracker", new List<RenoiseIssue>()));
                });
                Check("repeated write refuses overwrite", () => {
                    Refuse(() => RenoiseInstrumentConverter.Convert(archive, archive.Xml.Root, 0, folder, new List<RenoiseIssue>()));
                });
                Check("remaining song PCM budget is checked before asset writing", () => {
                    int before = Directory.GetFiles(absolute, "*.wav").Length;
                    Refuse(() => RenoiseInstrumentConverter.Convert(archive, archive.Xml.Root, 9, folder, new List<RenoiseIssue>(), 4));
                    Need(Directory.GetFiles(absolute, "*.wav").Length == before, "Over-budget sample wrote a clip before refusing.");
                    Refuse(() => RenoiseInstrumentConverter.Convert(archive, archive.Xml.Root, 9, folder, new List<RenoiseIssue>(), 0));
                });
            }
            finally
            {
                foreach (var item in created) if (item != null && !EditorUtility.IsPersistent(item)) UnityEngine.Object.DestroyImmediate(item);
                if (!absolute.StartsWith(Path.GetFullPath("Assets") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("__RenoiseInstrumentCheck_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refused cleanup outside the isolated fixture folder.");
                if (AssetDatabase.IsValidFolder(folder) && !AssetDatabase.DeleteAsset(folder)) throw new IOException("Unable to delete instrument check fixture folder: " + folder);
            }
            return report.Append("Renoise instrument groups passed=").Append(passed).Append(" failed=").Append(failed).ToString();
        }

        public static RenoiseArchive Fixture()
        {
            var xml = XDocument.Parse(@"<RenoiseInstrument doc_version='34'><Name>Synthetic import check</Name><GlobalProperties><Volume>1</Volume></GlobalProperties><SampleGenerator><Samples>
<Sample><Name>WAV ramp</Name><FileName>C:\do-not-read\secret.flac</FileName><Volume>0.5</Volume><Panning>0.25</Panning><Transpose>2</Transpose><Finetune>64</Finetune><LoopMode>PingPong</LoopMode><LoopStart>3</LoopStart><LoopEnd>90</LoopEnd><LoopRelease>true</LoopRelease><OneShotTrigger>true</OneShotTrigger><NewNoteAction>None</NewNoteAction><ModulationSetIndex>0</ModulationSetIndex><Mapping><BaseNote>48</BaseNote><NoteStart>12</NoteStart><NoteEnd>84</NoteEnd><VelocityStart>20</VelocityStart><VelocityEnd>100</VelocityEnd><MapKeyToPitch>false</MapKeyToPitch></Mapping></Sample>
<Sample><Name>FLAC same ramp</Name><Mapping><BaseNote>48</BaseNote></Mapping></Sample>
<Sample><Name>Missing release sample</Name><LoopMode>Forward</LoopMode><LoopEnd>20</LoopEnd><Mapping><Layer>Note-Off Layer</Layer></Mapping></Sample>
</Samples><ModulationSets><ModulationSet><Name>Volume</Name><Devices><SampleEnvelopeModulationDevice><Target>Volume</Target><Operator>*</Operator><TempoSynced>false</TempoSynced><SustainIsActive>true</SustainIsActive><SustainPos>20</SustainPos><LoopMode>Forward</LoopMode><LoopStart>10</LoopStart><LoopEnd>30</LoopEnd><Nodes><PlayMode>Lines</PlayMode><Length>41</Length><Points><Point>0,0,0</Point><Point>20,1,0</Point><Point>40,0,0</Point></Points></Nodes></SampleEnvelopeModulationDevice></Devices></ModulationSet></ModulationSets></SampleGenerator></RenoiseInstrument>");
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    Add(zip, "Instrument.xml", Encoding.UTF8.GetBytes(xml.ToString()));
                    Add(zip, "SampleData/Sample00 (wav).flac", RenoiseInstrumentAudio.ToWave(RenoiseInstrumentAudio.Decode(RenoiseInstrumentAudioCheck.FlacFixture())));
                    Add(zip, "SampleData/Sample01 (flac).wav", RenoiseInstrumentAudioCheck.FlacFixture());
                }
                return RenoiseArchive.Read(stream.ToArray());
            }
        }
        static void Add(ZipArchive zip, string name, byte[] bytes) { using (var stream = zip.CreateEntry(name).Open()) stream.Write(bytes, 0, bytes.Length); }
        static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Refuse(Action action)
        {
            try { action(); } catch (InvalidDataException) { return; } catch (IOException) { return; } catch (ArgumentException) { return; } catch (FormatException) { return; }
            throw new InvalidOperationException("Expected invalid import to be refused.");
        }
    }
}
