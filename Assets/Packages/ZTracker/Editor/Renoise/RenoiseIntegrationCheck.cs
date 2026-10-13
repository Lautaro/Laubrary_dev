using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Laubrary.Audio;
using Laubrary.ZTracker.Model;
using Laubrary.ZTracker.Renoise;
using Laubrary.Zounds;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>Invoke directly in the editor. Owns only unique synthetic assets; never opens a window or audio renderer.</summary>
    public static class RenoiseIntegrationCheck
    {
        public static string Run()
        {
            string token = Guid.NewGuid().ToString("N");
            string root = "Assets/__RenoiseImportCheck_" + token;
            string sourceFile = Path.Combine(Path.GetTempPath(), "RenoiseIntegration_" + token + ".xrns");
            var output = new StringBuilder(); int passed = 0, failed = 0;
            var objects = new List<Object>();
            Action<string, Action> check = (name, action) => { try { action(); passed++; output.AppendLine("PASS " + name); } catch (Exception ex) { failed++; output.AppendLine("FAIL " + name + ": " + ex.Message); } };
            RenoiseImportLink link = null; ZTrackerInstrument target = null, other = null;
            try
            {
                check("usage follows instrument-only context across repeated patterns", () =>
                {
                    var song = RenoiseSongConverter.Convert(Source()).Song;
                    var mappings = new List<RenoiseMapping> { new RenoiseMapping { source = new RenoiseSourceEntry { Key = "a", Kind = "Instrument", InstrumentIndex = 0 } }, new RenoiseMapping { source = new RenoiseSourceEntry { Key = "b", Kind = "Instrument", InstrumentIndex = 1 } } };
                    RenoiseImportService.PopulateUsage(song, mappings);
                    Assert(mappings[0].notes == 4 && mappings[0].rows == 4 && mappings[0].lowest == 48 && mappings[0].highest == 52 && Math.Abs(mappings[0].songShare - 2f / 3) < .0001f, "Instrument0 usage incorrect");
                    Assert(mappings[1].notes == 2 && mappings[0].uses.Count == 2 && mappings[0].tracks.Single() == "Notes", "Occurrence usage lost");
                });
                check("clean audition copies selected notes and carried context without source edits", () =>
                {
                    var source = RenoiseSongConverter.Convert(Source()).Song; string before = JsonUtility.ToJson(source);
                    var clean = RenoiseSongConverter.Convert(RenoiseSongCheck.CreateFixture()).Song;
                    RenoiseAudition.Fill(clean, source, 0, new RenoiseUse { sequence = 1, pattern = 0, track = 0, firstRow = 4, lastRow = 13 }, 4, 14);
                    var notes = clean.patterns[0].tracks[0].lines.SelectMany(l => l.notes).Where(n => n.note == NoteKind.Note).ToList();
                    Assert(notes.Count == 2 && notes.All(n => n.instrumentPresent && n.instrument == 0) && notes[0].pitch == 48 && notes[1].pitch == 52, "Excerpt did not isolate selected source");
                    Assert(clean.patterns[0].lineCount == 10 && JsonUtility.ToJson(source) == before, "Audition changed the source or range");
                });

                AssetDatabase.CreateFolder("Assets", root.Substring(7));
                target = NewTarget(root + "/Target.asset", "Replacement", .123f); objects.Add(target);
                other = NewTarget(root + "/Other.asset", "Other replacement", .456f); objects.Add(other);
                WriteArchive(sourceFile, Source());
                check("inspect and import preserve source bytes and leave plugins unresolved", () =>
                {
                    byte[] before = File.ReadAllBytes(sourceFile); var plan = RenoiseImportService.Inspect(sourceFile);
                    Assert(plan.song != null && plan.mappings.Any(m => m.source.Kind == "Plugin"), "Source inspection incomplete");
                    link = RenoiseImportService.Apply(plan, root + "/Imported"); objects.Add(link); objects.Add(link.song);
                    Assert(link.song.schemaVersion == 1 && link.song.model.instruments.Count == 2 && link.song.model.instruments.All(i => i == null), "Plugin slots collapsed or guessed");
                    Assert(File.ReadAllBytes(sourceFile).SequenceEqual(before) && File.ReadAllBytes(link.archivePath).SequenceEqual(before), "Source/snapshot bytes changed");
                });
                if (link == null) throw new InvalidOperationException("Import failed; dependent persistence checks cannot run.");
                check("unrelated song does not match unresolved null instrument slots", () =>
                {
                    var unrelated = ScriptableObject.CreateInstance<ZTrackerSong>();
                    try { Assert(RenoiseImportService.Find(unrelated) == null, "Unrelated song matched a null instrument slot"); }
                    finally { Object.DestroyImmediate(unrelated); }
                });
                check("instrument choice and Undo/Redo update both recipe and song", () =>
                {
                    RenoiseImportService.SetInstrument(link, Instrument(link, 0), target);
                    RenoiseImportService.SetInstrument(link, Instrument(link, 0), other);
                    Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo();
                    Assert(Instrument(link,0).instrument == target && link.song.model.instruments[0] == target, "Undo split mapping from song");
                    Undo.PerformRedo(); Assert(Instrument(link,0).instrument == other && link.song.model.instruments[0] == other, "Redo split mapping from song");
                    RenoiseImportService.SetInstrument(link, Instrument(link,0), target);
                    Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(link, "Renoise check reviewed"); Instrument(link,0).dealtWith = true; EditorUtility.SetDirty(link); Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
                });
                check("chain target follows stable track identity after local track reorder", () =>
                {
                    var mapping = link.mappings.First(m => m.source.Kind == "EffectChain" && m.source.TrackIndex == 0);
                    string id = link.song.model.tracks[0].id;
                    var moved = link.song.model.tracks[0]; link.song.model.tracks.RemoveAt(0); link.song.model.tracks.Insert(1,moved);
                    mapping.chainOwner = target; mapping.chainId = "0"; mapping.explicitTarget = true;
                    RenoiseImportService.ApplyChainChoices(link.song.model,new List<RenoiseMapping>{mapping},link.issues);
                    Assert(link.song.model.tracks.First(t=>t.id==id).devices.nodes.Single().p[0]==.37f && link.song.model.tracks[0].devices.nodes.Count==0,"Replacement hit the track at the old ordinal");
                });
                check("save unload reload preserves recipe, source link and target identity", () =>
                {
                    EditorUtility.SetDirty(link); EditorUtility.SetDirty(link.song); AssetDatabase.SaveAssetIfDirty(link); AssetDatabase.SaveAssetIfDirty(link.song);
                    string linkPath=AssetDatabase.GetAssetPath(link), songPath=AssetDatabase.GetAssetPath(link.song); var song=link.song;
                    Undo.ClearUndo(link); Undo.ClearUndo(song); Resources.UnloadAsset(link); Resources.UnloadAsset(song);
                    AssetDatabase.ImportAsset(linkPath,ImportAssetOptions.ForceSynchronousImport); AssetDatabase.ImportAsset(songPath,ImportAssetOptions.ForceSynchronousImport);
                    link=AssetDatabase.LoadAssetAtPath<RenoiseImportLink>(linkPath); objects.Add(link); objects.Add(link.song);
                    Assert(Instrument(link,0).dealtWith && Instrument(link,0).instrument==target && link.song.model.instruments[0]==target && File.Exists(link.archivePath),"Saved recipe or reference was lost");
                });
                check("changed patch and source track reorder retain replacement and reviewed mark", () =>
                {
                    string design=JsonUtility.ToJson(target); var updated=Source();
                    updated.Descendants("PluginDevice").First().SetElementValue("ParameterChunk","AQIDBA==");
                    var tracks=updated.Root.Element("Tracks"); var first=tracks.Elements().First(); first.Remove(); first.SetElementValue("Name","Renamed notes"); tracks.Elements().First().AddAfterSelf(first);
                    foreach(var pattern in updated.Root.Element("PatternPool").Element("Patterns").Elements("Pattern")) { var pts=pattern.Element("Tracks"); var p=pts.Elements().First(); p.Remove(); pts.Elements().First().AddAfterSelf(p); }
                    updated.Descendants("NumberOfLines").First().Value="32";
                    WriteArchive(sourceFile,updated);
                    var plan=RenoiseImportService.Inspect(sourceFile,link);
                    Assert(plan.mappings.First(m=>!m.removed&&m.source.Kind=="Instrument"&&m.source.InstrumentIndex==0).instrument==target,"Changed patch discarded unique replacement");
                    link=RenoiseImportService.Apply(plan,"",link);
                    Assert(Instrument(link,0).dealtWith && link.song.model.instruments[0]==target && JsonUtility.ToJson(target)==design && link.song.model.patterns[0].lineCount==32 && link.changes.Any(c=>c.Contains("changed")),"Reimport lost review, sound design or arrangement update");
                    var chain=link.mappings.FirstOrDefault(m=>!m.removed&&m.source.Kind=="EffectChain"&&m.source.TrackIndex==1);
                    Assert(chain!=null && chain.chainOwner==target && link.song.model.tracks[1].devices.nodes.Single().p[0]==.37f,"Renamed/reordered source chain lost its unique replacement");
                });
                check("explicit clear remains silent across reimport", () =>
                {
                    RenoiseImportService.SetInstrument(link,Instrument(link,0),null);
                    var cleared=Instrument(link,0); Assert(cleared.bypass || cleared.explicitTarget,"Clear did not persist a deliberate decision");
                    link=RenoiseImportService.Apply(RenoiseImportService.Inspect(sourceFile,link),"",link);
                    Assert(Instrument(link,0).instrument==null && link.song.model.instruments[0]==null && Instrument(link,0).dealtWith,"Reimport discarded clear/review decision");
                });
                check("instrument FX mapping owns a copy and survives Undo and reimport", () =>
                {
                    RenoiseImportService.SetInstrument(link, Instrument(link,0), target);
                    string original = JsonUtility.ToJson(target);
                    var fx = link.mappings.First(m => !m.removed && m.source.Kind == "EffectChain" && m.source.InstrumentIndex == 0);
                    RenoiseInstrumentChainMapping.Apply(link, fx, other, "0", false);
                    var copy = Instrument(link,0).instrument; objects.Add(copy);
                    Assert(copy != null && copy != target && AssetDatabase.GetAssetPath(copy).StartsWith(link.ownedFolder + "/",StringComparison.Ordinal) && JsonUtility.ToJson(target) == original, "FX mapping modified the chosen original or failed to own its copy");
                    Assert(copy.model.fxChains.Count == 1 && copy.model.fxChains[0].nodes.Single().p[0] == .37f && link.song.model.instruments[0] == copy, "Replacement chain or song reference missing");
                    string copyPath = AssetDatabase.GetAssetPath(copy);
                    Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo();
                    Assert(Instrument(link,0).instrument == target && link.song.model.instruments[0] == target && JsonUtility.ToJson(target) == original, "Undo did not restore the original choice");
                    Undo.PerformRedo(); copy = Instrument(link,0).instrument; objects.Add(copy);
                    Assert(copy != null && copy != target && AssetDatabase.GetAssetPath(copy) == copyPath && link.song.model.instruments[0] == copy, "Redo failed to restore owned FX copy");
                    link = RenoiseImportService.Apply(RenoiseImportService.Inspect(sourceFile,link), "", link);
                    Assert(Instrument(link,0).instrument == copy && JsonUtility.ToJson(target) == original && copy.model.fxChains[0].nodes.Single().p[0] == .37f, "Reimport lost FX copy or touched original sound design");
                });
                check("reimport cannot write through an unowned manifest folder", () =>
                {
                    string original=link.ownedFolder;
                    try
                    {
                        link.ownedFolder="Assets"; bool refused=false;
                        try { RenoiseImportService.Inspect(sourceFile,link); } catch(ArgumentException) { refused=true; } catch(InvalidOperationException) { refused=true; } catch(IOException) { refused=true; }
                        Assert(refused,"Unowned root was trusted");
                    }
                    finally { link.ownedFolder=original; }
                });
            }
            catch(Exception ex) { failed++; output.AppendLine("FAIL integration setup/dependency: "+ex.Message); }
            finally
            {
                foreach(var obj in objects.Distinct()) if(obj!=null) Undo.ClearUndo(obj);
                if(link!=null) { Undo.ClearUndo(link); if(link.song!=null)Undo.ClearUndo(link.song); }
                string full=Path.GetFullPath(root), expected=Path.GetFullPath("Assets")+Path.DirectorySeparatorChar+"__RenoiseImportCheck_";
                if(full.StartsWith(expected,StringComparison.OrdinalIgnoreCase)&&AssetDatabase.IsValidFolder(root)) AssetDatabase.DeleteAsset(root);
                if(File.Exists(sourceFile))File.Delete(sourceFile);
                output.AppendLine("Cleanup: unique synthetic import folder and source archive removed; no windows, audio renderer or listeners opened.");
            }
            output.Append("RenoiseIntegrationCheck passed="+passed+" failed="+failed); return output.ToString();
        }

        static RenoiseMapping Instrument(RenoiseImportLink link,int index)=>link.mappings.First(m=>!m.removed&&m.source.Kind=="Instrument"&&m.source.InstrumentIndex==index);
        static ZTrackerInstrument NewTarget(string path,string name,float volume)
        {
            var target=ScriptableObject.CreateInstance<ZTrackerInstrument>(); target.name=name; target.schemaVersion=1;
            target.model=new InstrumentData { id=Guid.NewGuid().ToString("N"),name=name,family=InstrumentFamily.Synth,parameters=new InstrumentParameters { type=InstrumentType.Synth,volume=volume } };
            for(int i=0;i<8;i++)target.model.macros[i]=new InstrumentMacro { name="Macro "+i };
            target.model.fxChains.Add(new AudioEffectChainData { nodes=new List<AudioEffectNodeData> {new AudioEffectNodeData { uid="replacement-gain",type=ZoundEffectType.Gain,p=new[]{.37f} } } });
            AssetDatabase.CreateAsset(target,path); EditorUtility.SetDirty(target); AssetDatabase.SaveAssetIfDirty(target); return target;
        }
        static XDocument Source()
        {
            var xml=RenoiseSongCheck.CreateFixture("<Line index='0'><NoteColumns><NoteColumn><Instrument>00</Instrument></NoteColumn></NoteColumns></Line><Line index='4'><NoteColumns><NoteColumn><Note>C-4</Note></NoteColumn></NoteColumns></Line><Line index='8'><NoteColumns><NoteColumn><Note>D-4</Note><Instrument>01</Instrument></NoteColumn></NoteColumns></Line><Line index='10'><NoteColumns><NoteColumn><Note>OFF</Note></NoteColumn></NoteColumns></Line><Line index='12'><NoteColumns><NoteColumn><Instrument>00</Instrument></NoteColumn></NoteColumns></Line><Line index='13'><NoteColumns><NoteColumn><Note>E-4</Note></NoteColumn></NoteColumns></Line>");
            var instruments=xml.Root.Element("Instruments"); instruments.RemoveNodes();
            for(int i=0;i<2;i++) instruments.Add(new XElement("Instrument",new XElement("Name","Voice "+i),new XElement("PluginGenerator",new XElement("PluginDevice",new XElement("PluginIdentifier","synthetic."+i),new XElement("PluginType","VST"),new XElement("PluginDisplayName","Synthetic "+i),new XElement("ParameterChunk","AQID")))));
            instruments.Elements().First().Add(XElement.Parse("<SampleGenerator><DeviceChains><SampleDeviceChain><Name>Source instrument FX</Name><Devices><GainerDevice><Volume><Value>0.5</Value></Volume></GainerDevice></Devices></SampleDeviceChain></DeviceChains></SampleGenerator>"));
            var tracks=xml.Root.Element("Tracks"); tracks.Elements().First().AddAfterSelf(new XElement("SequencerTrack",new XElement("Name","Empty")));
            tracks.Elements().First().Element("FilterDevices").Element("Devices").Add(new XElement("GainerDevice",new XElement("Volume",new XElement("Value","0.5"))));
            xml.Descendants("Pattern").First().Element("Tracks").Elements().First().AddAfterSelf(new XElement("PatternTrack"));
            xml.Root.Element("PatternSequence").Element("SequenceEntries").Add(new XElement("SequenceEntry",new XElement("Pattern",0)));
            return xml;
        }
        static void WriteArchive(string path,XDocument xml)
        {
            using(var file=new FileStream(path,FileMode.Create,FileAccess.Write))
            using(var zip=new ZipArchive(file,ZipArchiveMode.Create))
            using(var writer=new StreamWriter(zip.CreateEntry("Song.xml").Open(),new UTF8Encoding(false)))writer.Write(xml.ToString(SaveOptions.DisableFormatting));
        }
        static void Assert(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    }
}
