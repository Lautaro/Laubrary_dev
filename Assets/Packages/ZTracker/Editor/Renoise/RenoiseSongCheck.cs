using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Laubrary.ZTracker.Model;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>Kept structural checks. Invoke Execute directly; no Test Runner, assets, windows or audio device.</summary>
    public static class RenoiseSongCheck
    {
        public static XDocument CreateFixture(string rows = "") => Fixture(rows);
        public static string Execute()
        {
            int passed = 0, failed = 0; var report = new StringBuilder();
            Action<string, Action> check = (name, run) => { try { run(); passed++; report.AppendLine("PASS " + name); } catch (Exception ex) { failed++; report.AppendLine("FAIL " + name + ": " + ex.Message); } };
            check("sparse numeric pitch, OFF, selection and explicit zero", () =>
            {
                var song = Convert("<Line index='0'><NoteColumns><NoteColumn><Note>C-4</Note><Instrument>00</Instrument><Volume>00</Volume><Panning>40</Panning><Delay>FF</Delay></NoteColumn></NoteColumns></Line><Line index='7'><NoteColumns><NoteColumn><Note>OFF</Note></NoteColumn></NoteColumns></Line><Line index='9'><NoteColumns><NoteColumn><Instrument>00</Instrument></NoteColumn></NoteColumns></Line>");
                var rows = song.Song.patterns[0].tracks[0].lines;
                Assert(rows.Count == 3 && rows[0].notes[0].pitch == 48 && rows[0].notes[0].volume.kind == ValueKind.Value && rows[0].notes[0].volume.value == 0 && rows[0].notes[0].delay == 255 && rows[0].notes[0].delayPresent, "Explicit numerical data changed");
                Assert(rows[1].notes[0].note == NoteKind.Off && rows[2].notes[0].instrumentPresent && rows[2].notes[0].note == NoteKind.Empty, "OFF/selection lost");
            });
            check("empty interior columns and hidden inactive payload", () =>
            {
                var song = Convert("<Line index='0'><NoteColumns><NoteColumn/><NoteColumn/><NoteColumn><Note>B-9</Note><Instrument>00</Instrument></NoteColumn></NoteColumns></Line>");
                var note = song.Song.patterns[0].tracks[0].lines[0].notes.Single();
                Assert(note.column == 2 && note.pitch == 119 && note.inactive, "Hidden ordinal changed");
            });
            check("fractional BPM, independent LPB and TPL", () =>
            {
                var xml = Fixture(""); xml.Root.Element("GlobalSongData").Add(new XElement("BeatsPerMin", "123.45"), new XElement("LinesPerBeat", 8), new XElement("TicksPerLine", 7));
                var song = RenoiseSongConverter.Convert(xml).Song;
                Assert(song.bpm == 123.45 && song.linesPerBeat == 8 && song.ticksPerLine == 7, "Timing was rounded");
            });
            check("command-only rows, zero arguments and shorthand", () =>
            {
                var song = Convert("<Line index='5'><NoteColumns><NoteColumn><Volume>I0</Volume><Panning>K2</Panning></NoteColumn></NoteColumns><EffectColumns><EffectColumn/><EffectColumn><Number>0A</Number><Value>00</Value></EffectColumn></EffectColumns></Line>");
                var row = song.Song.patterns[0].tracks[0].lines.Single();
                Assert(row.effects[0].column == 1 && row.effects[0].command.valuePresent && row.effects[0].command.value == 0 && !row.effects[0].command.unsupported && row.notes[0].volume.command.identifier == "I0" && row.notes[0].pan.command.identifier == "K2", "Command presence/column changed");
            });
            check("unknown addressed devices never accidentally execute", () =>
            {
                var song = Convert("<Line index='0'><EffectColumns><EffectColumn><Number>A1</Number><Value>FF</Value></EffectColumn></EffectColumns></Line>");
                Assert(song.Song.patterns[0].tracks[0].lines[0].effects[0].command.unsupported, "Unknown device activated");
            });
            check("MIDI-context bytes cannot become global commands", () =>
            {
                var song = Convert("<Line index='0'><NoteColumns><NoteColumn><Panning>M0</Panning><Instrument>00</Instrument></NoteColumn></NoteColumns><EffectColumns><EffectColumn><Number>ZT</Number><Value>00</Value></EffectColumn></EffectColumns></Line>");
                Assert(song.Song.patterns[0].tracks[0].lines[0].effects[0].command.unsupported, "MIDI payload activated");
            });
            check("repeated sequence with independent mute lists", () =>
            {
                var xml = Fixture(""); xml.Root.Element("PatternSequence").Element("SequenceEntries").Add(XElement.Parse("<SequenceEntry><Pattern>0</Pattern><MutedTracks><MutedTrack>0</MutedTrack></MutedTracks></SequenceEntry>"));
                var song = RenoiseSongConverter.Convert(xml).Song;
                Assert(song.sequence.Count == 2 && song.sequence[0].patternId == song.sequence[1].patternId && song.sequence[0].mutedTrackIds.Count == 0 && song.sequence[1].mutedTrackIds.Single() == song.tracks[0].id, "Occurrence-specific mute lost");
            });
            check("aliases copy rows but keep destination automation", () =>
            {
                var xml = Fixture("<Line index='3'><NoteColumns><NoteColumn><Note>C-4</Note><Instrument>00</Instrument></NoteColumn></NoteColumns></Line>");
                var pool = xml.Root.Element("PatternPool").Element("Patterns");
                var clone = new XElement(pool.Element("Pattern")); var track = clone.Element("Tracks").Element("PatternTrack"); track.Element("Lines").Remove(); track.Add(new XElement("AliasPatternIndex", 0), Automation(1, "Points", "256,0.75,0")); pool.Add(clone);
                var result = RenoiseSongConverter.Convert(xml); var first = result.Song.patterns[0].tracks[0]; var second = result.Song.patterns[1].tracks[0];
                Assert(first.lines.Single().line == 3 && second.lines.Single().line == 3 && first.automation.Count == 0 && second.automation.Count == 1 && !ReferenceEquals(first.lines[0],second.lines[0]), "Alias payload crossed ownership");
            });
            check("alias cycles refuse before assets", () =>
            {
                var xml = Fixture(""); xml.Descendants("PatternTrack").First().Add(new XElement("AliasPatternIndex",0)); Reject(() => RenoiseSongConverter.Convert(xml));
            });
            check("modern subline pan automation and unknown curves", () =>
            {
                var xml = Fixture(""); var track = xml.Descendants("PatternTrack").First(); track.Add(Automation(0,"Lines","0,0,0","16383,1,0"));
                var lane = RenoiseSongConverter.Convert(xml).Song.patterns[0].tracks[0].automation.Single();
                Assert(lane.enabled && !lane.unsupported && lane.points[1].line == 63 + 255d / 256 && lane.target.parameter == "prePan", "Pan/time mapping changed");
                track.Element("Automations").Remove(); track.Add(Automation(1,"Curves","0,0,0.3","256,1,0"));
                lane = RenoiseSongConverter.Convert(xml).Song.patterns[0].tracks[0].automation.Single(); Assert(!lane.enabled && lane.unsupported && lane.rawSource.Contains("0.3"), "Unknown curve activated/lost");
            });
            check("static gain retains insertion order and unsupported source indices", () =>
            {
                var xml = Fixture(""); var devices = xml.Root.Element("Tracks").Elements().First().Element("FilterDevices").Element("Devices");
                devices.Add(new XElement("CompressorDevice"), XElement.Parse("<GainerDevice><Volume><Value>0.25</Value></Volume><Panning><Value>0.5</Value></Panning></GainerDevice>"));
                var result = RenoiseSongConverter.Convert(xml); var track = result.Song.tracks[0];
                Assert(track.devices.nodes.Count == 1 && track.devices.nodes[0].p[0] == .25f && track.sourceDevices[0].ordinal == 1 && track.sourceDevices[1].ordinal == 2 && result.Devices.First(d => d.TrackIndex == 0 && d.DeviceIndex == 2).Mapped, "Device filtering renumbered source addresses");
            });
            check("non-centered gain requires replacement", () =>
            {
                var xml = Fixture(""); xml.Root.Element("Tracks").Elements().First().Element("FilterDevices").Element("Devices").Add(XElement.Parse("<GainerDevice><Panning><Value>0.25</Value></Panning></GainerDevice>"));
                Assert(RenoiseSongConverter.Convert(xml).Song.tracks[0].devices.nodes.Count == 0, "Gainer pan was moved across the chain");
            });
            check("nested groups follow Renoise post-member order", () =>
            {
                var xml = Fixture(""); var tracks = xml.Root.Element("Tracks"); var first = tracks.Elements().First(); first.Add(new XElement("GroupNestingLevel",2), new XElement("TrackRouting",-1));
                first.AddAfterSelf(XElement.Parse("<SequencerGroupTrack><Name>Inner</Name><GroupNestingLevel>1</GroupNestingLevel><TrackRouting>-1</TrackRouting></SequencerGroupTrack>"), XElement.Parse("<SequencerGroupTrack><Name>Outer</Name><GroupNestingLevel>0</GroupNestingLevel></SequencerGroupTrack>"));
                var pt = xml.Descendants("Pattern").First().Element("Tracks"); pt.Elements().First().AddAfterSelf(new XElement("PatternGroupTrack"),new XElement("PatternGroupTrack"));
                var song = RenoiseSongConverter.Convert(xml).Song;
                Assert(song.tracks[0].parentGroupId == song.tracks[1].id && song.tracks[1].parentGroupId == song.tracks[2].id, "Group ancestry changed");
            });
            check("structural limits, malformed numbers and historical timing refuse", () =>
            {
                var xml = Fixture(""); xml.Descendants("NumberOfLines").First().Value="513"; Reject(() => RenoiseSongConverter.Convert(xml));
                xml=Fixture(""); xml.Root.SetAttributeValue("doc_version",21); Reject(() => RenoiseSongConverter.Convert(xml));
                xml=Fixture(""); xml.Root.Element("GlobalSongData").Add(new XElement("PlaybackEngineVersion",1)); Reject(() => RenoiseSongConverter.Convert(xml));
                xml=Fixture(""); xml.Root.Element("GlobalSongData").Add(new XElement("BeatsPerMin","NaN")); Reject(() => RenoiseSongConverter.Convert(xml));
            });
            check("older LPB engines retain notes but do not activate command guesses", () =>
            {
                var xml=Fixture("<Line index='0'><NoteColumns><NoteColumn><Note>C-4</Note><Instrument>00</Instrument><Volume>I2</Volume></NoteColumn></NoteColumns><EffectColumns><EffectColumn><Number>ZT</Number><Value>80</Value></EffectColumn></EffectColumns></Line>");
                foreach(int version in new[]{4,5})
                {
                    xml.Root.Element("GlobalSongData").SetElementValue("PlaybackEngineVersion",version);
                    var result=RenoiseSongConverter.Convert(xml); var row=result.Song.patterns[0].tracks[0].lines[0];
                    Assert(row.notes[0].pitch==48 && row.notes[0].volume.command.unsupported && row.effects[0].command.unsupported && result.Issues.Any(i=>i.Code=="OLDER_PLAYBACK_APPROXIMATE"), "Older semantics activated or notes lost");
                }
                xml.Root.Element("GlobalSongData").SetElementValue("PlaybackEngineVersion",7); Reject(()=>RenoiseSongConverter.Convert(xml));
            });
            check("Renoise 3.0 schema defaults and subline Linear automation", () =>
            {
                var xml=Fixture(""); xml.Root.SetAttributeValue("doc_version",54); xml.Descendants("PatternTrack").First().Add(Automation(0,"Linear","256,0.5"));
                var song=RenoiseSongConverter.Convert(xml).Song;
                Assert(song.bpm==140 && song.patterns[0].tracks[0].automation[0].enabled && song.patterns[0].tracks[0].automation[0].points[0].line==1,"Revision54 clock/automation default changed");
                xml.Root.SetAttributeValue("doc_version",63); Assert(RenoiseSongConverter.Convert(xml).Song.bpm==105,"Revision63 default tempo changed");
            });
            check("deterministic identities and grouped diagnostics", () =>
            {
                const string rows="<Line index='0'><EffectColumns><EffectColumn><Number>0Z</Number><Value>01</Value></EffectColumn></EffectColumns></Line><Line index='1'><EffectColumns><EffectColumn><Number>0Z</Number><Value>02</Value></EffectColumn></EffectColumns></Line>";
                var a=Convert(rows); var b=Convert(rows);
                Assert(a.Song.id==b.Song.id && a.Song.tracks[0].id==b.Song.tracks[0].id && a.Issues.Single(i=>i.Code=="COMMAND_UNMAPPED_0Z").Count==2, "Nondeterminism or per-row warning spam");
            });
            check("shortened pattern tails stay archived and missing instruments stay unresolved", () =>
            {
                var result=Convert("<Line index='0'><NoteColumns><NoteColumn><Note>C-4</Note><Instrument>80</Instrument></NoteColumn></NoteColumns></Line><Line index='128'><NoteColumns><NoteColumn><Note>D-4</Note><Instrument>00</Instrument></NoteColumn></NoteColumns></Line>");
                Assert(result.Song.patterns[0].lineCount==64 && result.Song.patterns[0].tracks[0].lines.Count==1 && result.Song.patterns[0].tracks[0].lines[0].notes[0].instrument==128 && result.Song.instruments.Count==129 && result.Song.instruments.All(i=>i==null) && result.Issues.Any(i=>i.Code=="PATTERN_TAIL_ARCHIVED") && result.Issues.Any(i=>i.Code=="INSTRUMENT_SLOT_UNRESOLVED"), "Hidden tail activated or missing slot guessed");
            });
            check("centered additive sends preserve target and insertion position", () =>
            {
                var xml=Fixture(""); xml.Root.Element("Tracks").Add(new XElement("SequencerSendTrack",new XElement("Name","Return")));
                xml.Descendants("Pattern").First().Element("Tracks").Add(new XElement("PatternSendTrack"));
                xml.Root.Element("Tracks").Elements().First().Element("FilterDevices").Element("Devices").Add(XElement.Parse("<GainerDevice><Volume><Value>0.5</Value></Volume></GainerDevice>"),XElement.Parse("<SendDevice><MuteSource>false</MuteSource><DestSendTrack><Value>0</Value></DestSendTrack><SendAmount><Value>0.25</Value></SendAmount><ApplyPostVolume>false</ApplyPostVolume></SendDevice>"));
                var song=RenoiseSongConverter.Convert(xml).Song; var send=song.tracks[0].sends.Single();
                Assert(send.trackId==song.tracks[2].id && send.devicePosition==1 && send.gain==.25f && !send.postfader,"Send tap changed");
            });
            report.Append("RenoiseSongCheck passed=" + passed + " failed=" + failed); return report.ToString();
        }

        static RenoiseSongConversion Convert(string rows) => RenoiseSongConverter.Convert(Fixture(rows));
        static XDocument Fixture(string rows) => XDocument.Parse("<RenoiseSong doc_version='67'><GlobalSongData><GlobalTrackHeadroom>1</GlobalTrackHeadroom></GlobalSongData><Instruments><Instrument/></Instruments><Tracks><SequencerTrack><Name>Notes</Name><FilterDevices><Devices><TrackMixerDevice/></Devices></FilterDevices></SequencerTrack><SequencerMasterTrack><Name>Master</Name></SequencerMasterTrack></Tracks><PatternPool><Patterns><Pattern><NumberOfLines>64</NumberOfLines><Tracks><PatternTrack><Lines>"+rows+"</Lines></PatternTrack><PatternMasterTrack/></Tracks></Pattern></Patterns></PatternPool><PatternSequence><SequenceEntries><SequenceEntry><Pattern>0</Pattern></SequenceEntry></SequenceEntries></PatternSequence></RenoiseSong>");
        static XElement Automation(int parameter,string mode,params string[] points) => new XElement("Automations",new XElement("Envelopes",new XElement("Envelope",new XElement("DeviceIndex",0),new XElement("ParameterIndex",parameter),new XElement("Envelope",new XElement("PlayMode",mode),new XElement("Length",16384),new XElement("Points",points.Select(p=>new XElement("Point",p)))))));
        static void Assert(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
        static void Reject(Action action) { try { action(); } catch(FormatException) { return; } throw new InvalidOperationException("Unsupported structure was accepted"); }
    }
}
