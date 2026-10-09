using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Laubrary.Zounds.Destructive {

    /// <summary>
    /// A piece of audio as the bytes of a WAV file's data chunk, with its format (destructive editing, 2026-10-09).
    ///
    /// Cutting, inserting and overwriting only ever move whole frames of these bytes, so every sample an edit does not
    /// touch is written back bit for bit, whatever the file's bit depth. Samples are decoded to numbers only when audio
    /// of one format has to go into a file of another (a paste between files); see <see cref="ConvertedTo"/> for the rule.
    /// </summary>
    internal sealed class AudioPcm {

        public const int IntFormat = 1, FloatFormat = 3;

        /// <summary>1 = whole-number samples, 3 = floating point.</summary>
        public int format;
        public int bits, channels, rate;
        public byte[] data = new byte[0];
        /// <summary>The Import &amp; Trim provenance chunk of the file this came from, kept when the file is rewritten.</summary>
        public byte[] provenance;

        public int BytesPerSample => bits / 8;
        public int BlockAlign => channels * BytesPerSample;
        public int Frames => BlockAlign > 0 ? data.Length / BlockAlign : 0;
        public double Seconds => rate > 0 ? (double)Frames / rate : 0d;

        public bool SameFormat(AudioPcm o) => o != null && o.format == format && o.bits == bits && o.channels == channels && o.rate == rate;

        public AudioPcm EmptyLike() => new AudioPcm { format = format, bits = bits, channels = channels, rate = rate, provenance = provenance };

        public string Describe() => channels + (channels == 1 ? " channel, " : " channels, ") + rate + " Hz, " + bits + "-bit" + (format == FloatFormat ? " float" : "");

        /// <summary>The frame nearest <paramref name="seconds"/>, clamped to the audio.</summary>
        public int FrameAt(double seconds) => Mathf.Clamp((int)Math.Round(seconds * rate), 0, Frames);

        // ─────────────────────────── frame operations (exact) ───────────────────────────

        public AudioPcm Slice(int from, int to) {
            from = Mathf.Clamp(from, 0, Frames); to = Mathf.Clamp(to, from, Frames);
            var r = EmptyLike();
            r.data = new byte[(to - from) * BlockAlign];
            Buffer.BlockCopy(data, from * BlockAlign, r.data, 0, r.data.Length);
            return r;
        }

        /// <summary>A copy with <paramref name="remove"/> frames at <paramref name="at"/> replaced by <paramref name="insert"/>
        /// (which must be in this format; null inserts nothing).</summary>
        public AudioPcm Spliced(int at, int remove, AudioPcm insert) {
            at = Mathf.Clamp(at, 0, Frames); remove = Mathf.Clamp(remove, 0, Frames - at);
            int ba = BlockAlign, ins = insert != null ? insert.data.Length : 0;
            var r = EmptyLike();
            r.data = new byte[data.Length - remove * ba + ins];
            Buffer.BlockCopy(data, 0, r.data, 0, at * ba);
            if (ins > 0) Buffer.BlockCopy(insert.data, 0, r.data, at * ba, ins);
            Buffer.BlockCopy(data, (at + remove) * ba, r.data, at * ba + ins, data.Length - (at + remove) * ba);
            return r;
        }

        /// <summary>A copy with <paramref name="src"/> written over the frames from <paramref name="at"/> on; the audio grows
        /// when it runs past the end. Nothing moves.</summary>
        public AudioPcm Overwritten(int at, AudioPcm src) {
            at = Mathf.Clamp(at, 0, Frames);
            int ba = BlockAlign;
            int end = Math.Max(Frames, at + src.Frames);
            var r = EmptyLike();
            r.data = new byte[end * ba];
            Buffer.BlockCopy(data, 0, r.data, 0, data.Length);
            Buffer.BlockCopy(src.data, 0, r.data, at * ba, src.data.Length);
            return r;
        }

        // ─────────────────────────── samples as numbers (only for a change of format) ───────────────────────────

        public float Sample(int frame, int channel) {
            int i = frame * BlockAlign + channel * BytesPerSample;
            if (format == FloatFormat) return bits == 64 ? (float)BitConverter.ToDouble(data, i) : BitConverter.ToSingle(data, i);
            switch (bits) {
                case 8: return (data[i] - 128) / 128f;
                case 16: return BitConverter.ToInt16(data, i) / 32768f;
                case 24: { int s = data[i] | (data[i + 1] << 8) | (data[i + 2] << 16); if ((s & 0x800000) != 0) s |= unchecked((int)0xFF000000); return s / 8388608f; }
                case 32: return (float)(BitConverter.ToInt32(data, i) / 2147483648d);
            }
            return 0f;
        }

        void Put(int frame, int channel, float v) {
            int i = frame * BlockAlign + channel * BytesPerSample;
            if (format == FloatFormat) {
                byte[] b = bits == 64 ? BitConverter.GetBytes((double)v) : BitConverter.GetBytes(v);
                Buffer.BlockCopy(b, 0, data, i, b.Length);
                return;
            }
            switch (bits) {
                case 8: data[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 128f) + 128, 0, 255); break;
                case 16: { short s = (short)Mathf.Clamp(Mathf.RoundToInt(v * 32768f), -32768, 32767); data[i] = (byte)s; data[i + 1] = (byte)(s >> 8); break; }
                case 24: { int s = Mathf.Clamp(Mathf.RoundToInt(v * 8388608f), -8388608, 8388607); data[i] = (byte)s; data[i + 1] = (byte)(s >> 8); data[i + 2] = (byte)(s >> 16); break; }
                case 32: { int s = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(v * 2147483648d))); Buffer.BlockCopy(BitConverter.GetBytes(s), 0, data, i, 4); break; }
            }
        }

        /// <summary>
        /// This audio in another format, for a paste between files that differ. The rule, stated once:
        /// <b>channels</b> -- one channel going to several is copied to each; several going to one are averaged; otherwise
        /// channel c takes channel c, and an extra channel repeats the last one there is. <b>Rate</b> -- resampled by straight-
        /// line interpolation between neighbouring frames (the length in seconds stays the same, to the nearest frame).
        /// <b>Depth</b> -- written at the target's depth, rounded to its nearest step and held inside its range.
        /// Audio already in the target format is returned as it is, untouched.
        /// </summary>
        public AudioPcm ConvertedTo(AudioPcm target) {
            if (SameFormat(target)) return this;
            int srcFrames = Frames;
            int dstFrames = rate == target.rate ? srcFrames : Math.Max(0, (int)Math.Round((double)srcFrames * target.rate / rate));
            var r = target.EmptyLike();
            r.provenance = null;
            r.data = new byte[dstFrames * r.BlockAlign];
            double step = dstFrames > 1 && srcFrames > 1 ? (double)(srcFrames - 1) / (dstFrames - 1) : 0d;
            for (int f = 0; f < dstFrames; f++) {
                double pos = rate == target.rate ? f : f * step;
                int f0 = Math.Min((int)pos, Math.Max(0, srcFrames - 1)), f1 = Math.Min(f0 + 1, Math.Max(0, srcFrames - 1));
                float u = (float)(pos - f0);
                for (int c = 0; c < r.channels; c++) {
                    float a = Mixed(f0, c, r.channels), b = f1 != f0 ? Mixed(f1, c, r.channels) : a;
                    r.Put(f, c, a + (b - a) * u);
                }
            }
            return r;
        }

        float Mixed(int frame, int dstChannel, int dstChannels) {
            if (channels == 1) return Sample(frame, 0);
            if (dstChannels == 1) { float s = 0f; for (int c = 0; c < channels; c++) s += Sample(frame, c); return s / channels; }
            return Sample(frame, Math.Min(dstChannel, channels - 1));
        }

        /// <summary>Audio decoded by Unity (a source that is not a WAV file), as 32-bit floating point.</summary>
        public static AudioPcm FromClip(AudioClip clip) {
            if (clip == null) return null;
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            var f = new float[clip.samples * clip.channels];
            if (!clip.GetData(f, 0)) return null;
            var r = new AudioPcm { format = FloatFormat, bits = 32, channels = clip.channels, rate = clip.frequency };
            r.data = new byte[f.Length * 4];
            Buffer.BlockCopy(f, 0, r.data, 0, r.data.Length);
            return r;
        }

        // ─────────────────────────── WAV files ───────────────────────────

        public const string ProvenanceChunk = "ZSRC";

        /// <summary>Reads a WAV file (whole-number 8/16/24/32-bit or floating-point 32/64-bit, plain or extensible header).</summary>
        public static AudioPcm ReadWav(string absolutePath, out string error) {
            error = null;
            byte[] wav;
            try { wav = File.ReadAllBytes(absolutePath); }
            catch (Exception e) { error = "Could not read the file: " + e.Message; return null; }
            if (wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE") { error = "Not a WAV file."; return null; }
            int pos = 12, fmt = -1, dataAt = -1, dataSize = 0;
            byte[] prov = null;
            while (pos + 8 <= wav.Length) {
                string id = Encoding.ASCII.GetString(wav, pos, 4);
                int size = BitConverter.ToInt32(wav, pos + 4);
                if (size < 0 || pos + 8 + size > wav.Length) size = wav.Length - pos - 8;
                if (id == "fmt ") fmt = pos + 8;
                else if (id == "data") { dataAt = pos + 8; dataSize = size; }
                else if (id == ProvenanceChunk) { prov = new byte[size]; Buffer.BlockCopy(wav, pos + 8, prov, 0, size); }
                pos += 8 + size + (size & 1);
            }
            if (fmt < 0 || dataAt < 0) { error = "The WAV file has no format or no data."; return null; }
            int tag = BitConverter.ToUInt16(wav, fmt);
            if (tag == 0xFFFE) tag = BitConverter.ToUInt16(wav, fmt + 24);   // extensible: the sub-format's first two bytes
            var r = new AudioPcm {
                format = tag, channels = BitConverter.ToUInt16(wav, fmt + 2), rate = BitConverter.ToInt32(wav, fmt + 4),
                bits = BitConverter.ToUInt16(wav, fmt + 14), provenance = prov,
            };
            bool ok = (r.format == IntFormat && (r.bits == 8 || r.bits == 16 || r.bits == 24 || r.bits == 32))
                   || (r.format == FloatFormat && (r.bits == 32 || r.bits == 64));
            if (!ok || r.channels < 1 || r.rate < 1) { error = "Unsupported WAV format (" + r.format + ", " + r.bits + "-bit)."; return null; }
            int usable = dataSize - dataSize % r.BlockAlign;
            r.data = new byte[usable];
            Buffer.BlockCopy(wav, dataAt, r.data, 0, usable);
            return r;
        }

        /// <summary>The WAV file for this audio: a plain header, the provenance chunk if there was one, the data.</summary>
        public byte[] ToWav() {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms)) {
                int provLen = provenance != null ? provenance.Length + 8 + (provenance.Length & 1) : 0;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(4 + 24 + provLen + 8 + data.Length + (data.Length & 1));
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((ushort)format);
                w.Write((ushort)channels);
                w.Write(rate);
                w.Write(rate * BlockAlign);
                w.Write((ushort)BlockAlign);
                w.Write((ushort)bits);
                if (provenance != null) {
                    w.Write(Encoding.ASCII.GetBytes(ProvenanceChunk));
                    w.Write(provenance.Length);
                    w.Write(provenance);
                    if ((provenance.Length & 1) == 1) w.Write((byte)0);
                }
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(data.Length);
                w.Write(data);
                if ((data.Length & 1) == 1) w.Write((byte)0);
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
