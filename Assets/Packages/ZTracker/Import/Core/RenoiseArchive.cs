using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace Laubrary.ZTracker.Renoise
{
    [Serializable]
    public sealed class RenoiseIssue
    {
        public string Code;
        public string Message;
        public string Location;
        public int Count = 1;

        public RenoiseIssue() { }
        public RenoiseIssue(string code, string message, string location = "", int count = 1)
        {
            Code = code; Message = message; Location = location; Count = count;
        }
    }

    /// <summary>Resource limits apply before allocating entry or XML payloads. No entry is extracted to disk.</summary>
    public sealed class RenoiseReadLimits
    {
        public long MaxArchiveBytes = 256L * 1024 * 1024;
        public long MaxExpandedBytes = 512L * 1024 * 1024;
        public int MaxEntryBytes = 256 * 1024 * 1024;
        public int MaxXmlBytes = 64 * 1024 * 1024;
        public int MaxEntries = 16384;
        public int MaxXmlDepth = 64;
        public int MaxXmlNodes = 4000000;
        public int MaxCompressionRatio = 1000;

        internal void Validate()
        {
            if (MaxArchiveBytes < 1 || MaxArchiveBytes > int.MaxValue || MaxExpandedBytes < 1 ||
                MaxEntryBytes < 1 || MaxXmlBytes < 1 || MaxEntries < 1 || MaxXmlDepth < 1 ||
                MaxXmlNodes < 1 || MaxCompressionRatio < 1)
                throw new ArgumentOutOfRangeException(nameof(RenoiseReadLimits), "Read limits must be positive; archive bytes must fit a managed array.");
        }
    }

    /// <summary>Safe, lossless archive envelope. Reading never loads plugins, runs scripts, or creates assets.</summary>
    public sealed class RenoiseArchive
    {
        public XDocument Xml { get; private set; }
        public bool IsSong { get; private set; }
        public int Revision { get; private set; }
        public string Hash { get; private set; }
        public byte[] OriginalBytes { get; private set; }
        public Dictionary<string, byte[]> Samples { get; private set; }
        public List<RenoiseIssue> Issues { get; private set; }

        public static RenoiseArchive Read(string path)
        {
            return Read(path, new RenoiseReadLimits(), CancellationToken.None);
        }

        public static RenoiseArchive Read(byte[] bytes)
        {
            return Read(bytes, new RenoiseReadLimits(), CancellationToken.None);
        }

        public static RenoiseArchive Read(string path, RenoiseReadLimits limits, CancellationToken cancellation = default(CancellationToken))
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            limits = limits ?? new RenoiseReadLimits();
            limits.Validate();
            cancellation.ThrowIfCancellationRequested();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > limits.MaxArchiveBytes) throw new InvalidDataException("Renoise archive exceeds the compressed-byte budget.");
                return ReadOwned(ReadBounded(stream, stream.Length, limits.MaxArchiveBytes, cancellation), limits, cancellation);
            }
        }

        public static RenoiseArchive Read(byte[] bytes, RenoiseReadLimits limits, CancellationToken cancellation = default(CancellationToken))
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            limits = limits ?? new RenoiseReadLimits();
            limits.Validate();
            cancellation.ThrowIfCancellationRequested();
            if (bytes.LongLength > limits.MaxArchiveBytes) throw new InvalidDataException("Renoise archive exceeds the compressed-byte budget.");
            return ReadOwned((byte[])bytes.Clone(), limits, cancellation);
        }

        private static RenoiseArchive ReadOwned(byte[] bytes, RenoiseReadLimits limits, CancellationToken cancellation)
        {
            ValidateDirectory(bytes, limits, cancellation);
            var result = new RenoiseArchive
            {
                OriginalBytes = bytes,
                Hash = RenoiseIdentity.Hash(bytes),
                Samples = new Dictionary<string, byte[]>(StringComparer.Ordinal),
                Issues = new List<RenoiseIssue>()
            };
            byte[] xmlBytes = null;
            string xmlName = null;
            using (var memory = new MemoryStream(bytes, false))
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Read, false))
            {
                if (zip.Entries.Count > limits.MaxEntries) throw new InvalidDataException("Renoise archive exceeds the entry-count budget.");
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long expanded = 0;
                // Validate every entry, including unrecognized payloads retained only in OriginalBytes.
                foreach (var entry in zip.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    ValidatePath(entry.FullName);
                    if (!paths.Add(entry.FullName)) throw new InvalidDataException("Duplicate or case-ambiguous archive member: " + entry.FullName);
                    if (entry.Length > limits.MaxEntryBytes || entry.Length > limits.MaxExpandedBytes - expanded)
                        throw new InvalidDataException("Renoise archive exceeds the expanded-byte budget.");
                    expanded += entry.Length;
                    if (entry.Length > 1024 * 1024 && (double)entry.Length / Math.Max(1L, entry.CompressedLength) > limits.MaxCompressionRatio)
                        throw new InvalidDataException("Suspicious archive compression ratio: " + entry.FullName);
                    if (entry.FullName == "Song.xml" || entry.FullName == "Instrument.xml")
                    {
                        if (xmlName != null) throw new InvalidDataException("Archive must contain exactly one Song.xml or Instrument.xml.");
                        if (entry.Length > limits.MaxXmlBytes) throw new InvalidDataException("Renoise XML exceeds the XML-byte budget.");
                        xmlName = entry.FullName;
                    }
                }
                if (xmlName == null) throw new InvalidDataException("Archive has no root Song.xml or Instrument.xml.");
                foreach (var entry in zip.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (entry.FullName != xmlName && !entry.FullName.StartsWith("SampleData/", StringComparison.Ordinal)) continue;
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                    using (var stream = entry.Open())
                    {
                        var data = ReadBounded(stream, entry.Length, entry.FullName == xmlName ? limits.MaxXmlBytes : limits.MaxEntryBytes, cancellation);
                        if (entry.FullName == xmlName) xmlBytes = data;
                        else result.Samples.Add(entry.FullName, data);
                    }
                }
            }
            result.Xml = ReadXml(xmlBytes, limits, cancellation);
            result.IsSong = xmlName == "Song.xml";
            var root = result.Xml.Root;
            if (root == null || root.Name != (result.IsSong ? "RenoiseSong" : "RenoiseInstrument"))
                throw new InvalidDataException("Renoise XML root does not match its archive member.");
            int revision;
            if (!int.TryParse((string)root.Attribute("doc_version"), NumberStyles.None, CultureInfo.InvariantCulture, out revision))
                throw new InvalidDataException("Missing or invalid Renoise document revision.");
            result.Revision = revision;
            if (result.IsSong ? revision < 54 || revision > 67 : revision < 24 || revision > 34)
                throw new InvalidDataException("Unsupported Renoise revision " + revision + ". Version 1 reads song revisions 54–67 and instrument revisions 24–34 (Renoise 3.0–3.5 family). Older timing needs an explicit adapter.");
            if (!(result.IsSong ? new[] { 54, 63, 64, 65, 67 } : new[] { 24, 31, 32, 33, 34 }).Contains(revision))
                result.Issues.Add(new RenoiseIssue("IntermediateRevision", "Intermediate document revision accepted using the modern profile; exact compatibility has not been established.", xmlName));
            if (result.IsSong)
            {
                int engine = 6;
                var savedEngine = RenoiseXml.Element(root, "GlobalSongData/PlaybackEngineVersion");
                if (savedEngine != null && !int.TryParse(savedEngine.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out engine))
                    throw new InvalidDataException("Malformed playback-engine version.");
                if (engine < 1) throw new InvalidDataException("Playback-engine version must be positive.");
                if (engine < 6) result.Issues.Add(new RenoiseIssue("LegacyPlaybackEngine", "Legacy playback engine " + engine + " retains old timing/command semantics. Arrangement can be inspected, but commands require an explicit legacy adapter or an engine upgrade in Renoise Song Options.", "GlobalSongData/PlaybackEngineVersion"));
                if (engine > 6) result.Issues.Add(new RenoiseIssue("FuturePlaybackEngine", "Unknown playback engine " + engine + "; inspection is available but playback conversion must not assume modern timing semantics.", "GlobalSongData/PlaybackEngineVersion"));
            }
            cancellation.ThrowIfCancellationRequested();
            return result;
        }

        private static void ValidateDirectory(byte[] bytes, RenoiseReadLimits limits, CancellationToken cancellation)
        {
            // ZipArchive materializes the central directory when Entries is accessed. Bound it BEFORE that
            // allocation, including a check of actual records rather than trusting the declared count.
            int end = -1;
            for (int i = bytes.Length - 22; i >= Math.Max(0, bytes.Length - 65557); i--)
            {
                if (U32(bytes, i) == 0x06054b50 && i + 22 + U16(bytes, i + 20) == bytes.Length) { end = i; break; }
            }
            if (end < 0) throw new InvalidDataException("Missing ZIP central directory.");
            int count = U16(bytes, end + 10);
            if (U16(bytes, end + 4) != 0 || U16(bytes, end + 6) != 0 || U16(bytes, end + 8) != count)
                throw new InvalidDataException("Split ZIP archives are not supported.");
            long length = U32(bytes, end + 12), start = U32(bytes, end + 16);
            if (count == 65535 || length == uint.MaxValue || start == uint.MaxValue)
                throw new InvalidDataException("ZIP64 is outside version 1's bounded archive profile.");
            if (count > limits.MaxEntries || start + length != end || start < 0)
                throw new InvalidDataException("Invalid or oversized ZIP central directory.");
            int cursor = (int)start;
            for (int record = 0; record < count; record++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (cursor > end - 46 || U32(bytes, cursor) != 0x02014b50)
                    throw new InvalidDataException("Truncated ZIP central-directory record.");
                int flags = U16(bytes, cursor + 8), method = U16(bytes, cursor + 10);
                if ((flags & 1) != 0 || (method != 0 && method != 8))
                    throw new InvalidDataException("Encrypted or unsupported ZIP compression method.");
                if (((U32(bytes, cursor + 38) >> 16) & 0xf000) == 0xa000)
                    throw new InvalidDataException("Symbolic links are not permitted in a Renoise archive.");
                long next = (long)cursor + 46 + U16(bytes, cursor + 28) + U16(bytes, cursor + 30) + U16(bytes, cursor + 32);
                if (next > end) throw new InvalidDataException("ZIP central-directory record exceeds its bounds.");
                cursor = (int)next;
            }
            if (cursor != end) throw new InvalidDataException("ZIP central-directory entry count does not match its contents.");
        }

        private static int U16(byte[] bytes, int at) { return bytes[at] | bytes[at + 1] << 8; }
        private static uint U32(byte[] bytes, int at) { return (uint)(U16(bytes, at) | U16(bytes, at + 2) << 16); }

        private static void ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 1024 || path[0] == '/' || path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0 || path.Any(char.IsControl))
                throw new InvalidDataException("Unsafe archive member path.");
            string[] segments = path.TrimEnd('/').Split('/');
            if (segments.Any(s => s.Length == 0 || s == "." || s == ".." || s.EndsWith(" ", StringComparison.Ordinal) || s.EndsWith(".", StringComparison.Ordinal)))
                throw new InvalidDataException("Unsafe archive member path: " + path);
        }

        private static byte[] ReadBounded(Stream input, long expected, long limit, CancellationToken cancellation)
        {
            if (expected < 0 || expected > limit || expected > int.MaxValue) throw new InvalidDataException("Archive member exceeds its byte budget.");
            var data = new byte[(int)expected];
            int offset = 0;
            while (offset < data.Length)
            {
                cancellation.ThrowIfCancellationRequested();
                int count = input.Read(data, offset, Math.Min(65536, data.Length - offset));
                if (count == 0) throw new InvalidDataException("Truncated archive member.");
                offset += count;
            }
            cancellation.ThrowIfCancellationRequested();
            if (input.ReadByte() != -1) throw new InvalidDataException("Archive member expanded beyond its declared length.");
            return data;
        }

        private static XDocument ReadXml(byte[] bytes, RenoiseReadLimits limits, CancellationToken cancellation)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = limits.MaxXmlBytes,
                MaxCharactersFromEntities = 1024,
                IgnoreWhitespace = false
            };
            try
            {
                // Check nesting and total nodes before LINQ to XML builds an object graph.
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = XmlReader.Create(stream, settings))
                {
                    int nodes = 0;
                    while (reader.Read())
                    {
                        cancellation.ThrowIfCancellationRequested();
                        nodes += 1 + reader.AttributeCount;
                        if (reader.Depth > limits.MaxXmlDepth || nodes > limits.MaxXmlNodes)
                            throw new InvalidDataException("Renoise XML exceeds the nesting/node budget.");
                    }
                }
                cancellation.ThrowIfCancellationRequested();
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = XmlReader.Create(stream, settings))
                    return XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            }
            catch (XmlException ex) { throw new InvalidDataException("Unsafe or malformed Renoise XML: " + ex.Message, ex); }
        }
    }

    /// <summary>Invariant, explicit-default accessors. Missing data and authored zero remain distinct.</summary>
    public static class RenoiseXml
    {
        public static XElement Element(XElement element, string path)
        {
            if (element == null) return null;
            foreach (string part in path.Split('/'))
            {
                if (part.Length == 0) continue;
                element = element.Element(part);
                if (element == null) return null;
            }
            return element;
        }

        public static string Text(XElement element, string path, string fallback = "")
        {
            var found = Element(element, path);
            return found == null ? fallback : found.Value;
        }

        public static int Int(XElement element, string path, int fallback = 0)
        {
            int result;
            return int.TryParse(Text(element, path), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        public static float Float(XElement element, string path, float fallback = 0)
        {
            float result;
            return float.TryParse(Text(element, path), NumberStyles.Float, CultureInfo.InvariantCulture, out result) && !float.IsNaN(result) && !float.IsInfinity(result) ? result : fallback;
        }

        public static bool Bool(XElement element, string path, bool fallback = false)
        {
            string text = Text(element, path);
            bool result;
            return bool.TryParse(text, out result) ? result : text == "1" ? true : text == "0" ? false : fallback;
        }
    }
}
