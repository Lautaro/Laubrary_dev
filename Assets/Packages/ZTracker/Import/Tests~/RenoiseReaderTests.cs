using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Laubrary.ZTracker.Renoise;

// Synthetic fixtures only. No factory audio, third-party song, Unity API, network, or persistent output.
public static class RenoiseReaderTests
{
    private static int assertions;

    public static int Main(string[] args)
    {
        try
        {
            Run();
            foreach (var path in args)
            {
                var archive = RenoiseArchive.Read(path);
                var catalog = RenoiseCatalog.Create(archive);
                Console.WriteLine("LOCAL FIXTURE " + Path.GetFileName(path) + ": revision " + archive.Revision +
                    ", samples " + archive.Samples.Count + ", catalog " + catalog.Entries.Count + ", warnings " + catalog.Issues.Count);
            }
            Console.WriteLine("PASS Renoise reader/catalog: " + assertions + " assertions");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    public static int Run()
    {
        assertions = 0;
        var source = Zip("Song.xml", Song(67, "<FutureFeature opaque='keep'><Payload>123</Payload></FutureFeature>"), "SampleData/Instrument00/Sample00.wav", new byte[] { 1, 2, 3 });
        var archive = RenoiseArchive.Read(source);
        Check(archive.IsSong && archive.Revision == 67, "song envelope");
        Check(archive.Hash.Length == 64 && archive.OriginalBytes.SequenceEqual(source), "lossless source/hash");
        source[0] = 0;
        Check(archive.OriginalBytes[0] != 0, "caller cannot mutate preserved bytes");
        Check(archive.Xml.Root.Element("FutureFeature").Element("Payload").Value == "123", "unknown XML preserved");
        Check(archive.Samples.Single().Value.SequenceEqual(new byte[] { 1, 2, 3 }), "sample bytes preserved");
        Check(!RenoiseArchive.Read(Zip("Instrument.xml", "<RenoiseInstrument doc_version='34'/> ")).IsSong, "XRNI envelope");
        for (int i = 54; i <= 67; i++)
            Check(RenoiseArchive.Read(Zip("Song.xml", Song(i))).Revision == i, "modern song " + i);
        for (int i = 24; i <= 34; i++)
            Check(RenoiseArchive.Read(Zip("Instrument.xml", "<RenoiseInstrument doc_version='" + i + "'/> ")).Revision == i, "modern instrument " + i);
        Check(RenoiseArchive.Read(Zip("Song.xml", Song(66))).Issues.Any(i => i.Code == "IntermediateRevision"), "intermediate revision warned");
        foreach (int revision in new[] { 21, 37, 53, 68 }) Reject(Zip("Song.xml", Song(revision)), "unsupported song revision");
        Reject(Zip("Instrument.xml", "<RenoiseInstrument doc_version='23'/>"), "old instrument");
        Check(RenoiseArchive.Read(Zip("Song.xml", "<RenoiseSong doc_version='67'><GlobalSongData><PlaybackEngineVersion>5</PlaybackEngineVersion></GlobalSongData></RenoiseSong>")).Issues.Any(i => i.Code == "LegacyPlaybackEngine"), "legacy playback inspection warning");
        Check(RenoiseArchive.Read(Zip("Song.xml", "<RenoiseSong doc_version='67'><GlobalSongData><PlaybackEngineVersion>7</PlaybackEngineVersion></GlobalSongData></RenoiseSong>")).Issues.Any(i => i.Code == "FuturePlaybackEngine"), "future playback inspection warning");
        Reject(Zip("Song.xml", "<RenoiseSong doc_version='67'><GlobalSongData><PlaybackEngineVersion>bad</PlaybackEngineVersion></GlobalSongData></RenoiseSong>"), "malformed playback engine");
        Reject(Zip("Song.xml", "<!DOCTYPE x [<!ENTITY payload SYSTEM 'file:///not-read'>]><RenoiseSong doc_version='67'>&payload;</RenoiseSong>"), "DTD and external entity");
        Reject(Zip("Song.xml", Song(67, string.Concat(Enumerable.Repeat("<a>", 80)) + string.Concat(Enumerable.Repeat("</a>", 80)))), "depth limit");
        Reject(Zip("Song.xml", Song(67), "../escape.wav", new byte[1]), "path traversal");
        Reject(Zip("Song.xml", Song(67), "SampleData\\escape.wav", new byte[1]), "backslash path");
        Reject(Zip("Song.xml", Song(67), "C:/escape.wav", new byte[1]), "drive path");
        Reject(Zip("Song.xml", Song(67), "Song.xml", Encoding.UTF8.GetBytes(Song(67))), "duplicate XML");
        Reject(Zip("Song.xml", Song(67), "song.xml", new byte[1]), "case ambiguous member");
        Reject(Zip("Song.xml", Song(67), "Instrument.xml", Encoding.UTF8.GetBytes("<RenoiseInstrument doc_version='34'/>")), "ambiguous archive kind");
        Reject(Zip("Instrument.xml", Song(67)), "root mismatch");
        Reject(new byte[] { 1, 2, 3 }, "invalid ZIP");
        Reject(Zip("Other.xml", Song(67)), "missing document");
        var small = Zip("Song.xml", Song(67));
        Reject(small, "compressed budget", new RenoiseReadLimits { MaxArchiveBytes = 4 });
        Reject(small, "XML byte budget", new RenoiseReadLimits { MaxXmlBytes = 5 });
        Reject(small, "expanded budget", new RenoiseReadLimits { MaxExpandedBytes = 5 });
        Reject(Zip("Song.xml", Song(67), "Other", new byte[1]), "entry budget", new RenoiseReadLimits { MaxEntries = 1 });
        Reject(Zip("Song.xml", Song(67, "<a/><a/><a/><a/>")), "node budget", new RenoiseReadLimits { MaxXmlNodes = 3 });
        Reject(Zip("Song.xml", Song(67), "SampleData/bomb.wav", new byte[2 * 1024 * 1024]), "compression ratio");
        byte[] lyingCount = (byte[])small.Clone();
        lyingCount[lyingCount.Length - 12] = 0;
        Reject(lyingCount, "false central directory count");
        bool cancelled = false;
        try { RenoiseArchive.Read(small, null, new CancellationToken(true)); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "cancellation");
        var numbers = XElement.Parse("<x><zero>0</zero><fraction>0.25</fraction><bad>NaN</bad><yes>1</yes><no>false</no></x>");
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
            Check(RenoiseXml.Float(numbers, "fraction") == .25f, "invariant decimals");
            Check(RenoiseXml.Int(numbers, "zero", 42) == 0, "authored zero");
            Check(RenoiseXml.Float(numbers, "bad", 42) == 42, "nonfinite float");
            Check(RenoiseXml.Bool(numbers, "yes") && !RenoiseXml.Bool(numbers, "no", true), "boolean representations");
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
        IdentityAndCatalog();
        return assertions;
    }

    private static void IdentityAndCatalog()
    {
        var first = XElement.Parse(Plugin("Bass", "abc", "patch-one"));
        var rename = new XElement(first);
        rename.Element("Name").Value = "Renamed";
        rename.Element("PluginGenerator").Element("PluginDevice").Element("PluginDisplayName").Value = "Display changed";
        Check(RenoiseIdentity.InstrumentKey(first, 0) == RenoiseIdentity.InstrumentKey(rename, 99), "identified plugin key survives rename/slot move");
        var patch = XElement.Parse(Plugin("Bass", "abc", "patch-two"));
        Check(RenoiseIdentity.InstrumentKey(first, 0) != RenoiseIdentity.InstrumentKey(patch, 0), "plugin patch identity");
        Check(RenoiseIdentity.Fingerprint(XElement.Parse("<x a='1' b='2'><y>yes</y></x>")) ==
            RenoiseIdentity.Fingerprint(XElement.Parse("<x b='2' a='1'>\n <y>yes</y>\n</x>")), "format-independent fingerprint");
        var chain = XElement.Parse("<FilterDevices><Devices><VolumeDevice/><DelayDevice/></Devices></FilterDevices>");
        var changed = XElement.Parse("<FilterDevices><Devices><DelayDevice/><VolumeDevice/></Devices></FilterDevices>");
        Check(RenoiseIdentity.ChainKey(chain, "track") != RenoiseIdentity.ChainKey(changed, "track"), "unnamed chain order identity");
        chain.Add(new XElement("Name", "My effects")); changed.Add(new XElement("Name", "My effects"));
        Check(RenoiseIdentity.ChainKey(chain, "track") == RenoiseIdentity.ChainKey(changed, "track"), "named chain stable identity");
        var xml = Song(67, "<Instruments>" + Plugin("Bass", "abc", "patch-one") + Plugin("Lead", "abc", "patch-one") +
            "</Instruments><Tracks><SequencerTrack><Name>Bass track</Name>" + chain + "</SequencerTrack></Tracks>");
        var catalog = RenoiseCatalog.Create(RenoiseArchive.Read(Zip("Song.xml", xml)));
        Check(catalog.Entries.Count(e => e.Kind == "Instrument") == 2 && catalog.Entries.Count(e => e.Kind == "Plugin") == 2, "instrument/plugin inventory");
        Check(catalog.Entries.Count(e => e.Kind == "Device") == 2 && catalog.Entries.Count(e => e.Kind == "EffectChain") == 1, "chain/device inventory");
        Check(catalog.Entries.Select(e => e.Key).Distinct().Count() == catalog.Entries.Count, "unique duplicate identities");
        var reordered = RenoiseCatalog.Create(RenoiseArchive.Read(Zip("Song.xml", Song(67, "<Instruments>" + Plugin("Lead", "abc", "patch-one") + Plugin("Bass", "abc", "patch-one") + "</Instruments>"))));
        Check(catalog.Entries.First(e => e.Kind == "Instrument" && e.Name == "Bass").Key == reordered.Entries.First(e => e.Kind == "Instrument" && e.Name == "Bass").Key, "same patch distinct roles survive source reorder");
        var identical = RenoiseCatalog.Create(RenoiseArchive.Read(Zip("Song.xml", Song(67, "<Instruments>" + Plugin("Bass", "abc", "patch-one") + Plugin("Bass", "abc", "patch-one") + "</Instruments>"))));
        Check(identical.Issues.Any(i => i.Code == "AmbiguousIdentity"), "duplicate ambiguity is explicit");
        Check(catalog.Issues.Single(i => i.Code == "PluginSubstitution").Count == 2, "warnings aggregate by cause");
        Check(catalog.Entries.Where(e => e.Kind == "Device").All(e => e.TrackIndex == 0 && e.InstrumentIndex == -1), "track device addressing");
        var nested = RenoiseCatalog.Create(RenoiseArchive.Read(Zip("Song.xml", Song(67, "<Tracks><SequencerTrack><Name>Nested</Name><FilterDevices><Devices><DooferDevice><DeviceChain><Devices><VolumeDevice/></Devices></DeviceChain></DooferDevice><DelayDevice/></Devices></FilterDevices></SequencerTrack></Tracks>"))));
        Check(nested.Entries.Count(e => e.Kind == "Device" && e.NestingDepth == 0) == 2 && nested.Entries.Count(e => e.Kind == "Device" && e.NestingDepth == 1) == 1, "nested source addresses remain distinct");
    }

    private static string Plugin(string name, string id, string state)
    {
        return "<Instrument><Name>" + name + "</Name><PluginGenerator><PluginDevice><PluginType>VST</PluginType><PluginIdentifier>" + id +
            "</PluginIdentifier><PluginDisplayName>Test FM</PluginDisplayName><ParameterChunk>" + state + "</ParameterChunk></PluginDevice></PluginGenerator></Instrument>";
    }

    private static string Song(int revision, string body = "") { return "<RenoiseSong doc_version='" + revision + "'>" + body + "</RenoiseSong>"; }
    private static byte[] Zip(string member, string xml, string extraName = null, byte[] extra = null)
    {
        using (var output = new MemoryStream())
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                using (var stream = zip.CreateEntry(member).Open()) { var data = Encoding.UTF8.GetBytes(xml); stream.Write(data, 0, data.Length); }
                if (extraName != null) using (var stream = zip.CreateEntry(extraName).Open()) stream.Write(extra, 0, extra.Length);
            }
            return output.ToArray();
        }
    }
    private static void Reject(byte[] bytes, string label, RenoiseReadLimits limits = null)
    {
        bool failed = false;
        try { RenoiseArchive.Read(bytes, limits); } catch (InvalidDataException) { failed = true; }
        Check(failed, label);
    }
    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception("Assertion failed: " + label);
    }
}
