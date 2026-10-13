using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Laubrary.ZTracker.Renoise
{
    /// <summary>Bounded, dependency-free import-time PCM decoding. Never called on the audio thread.</summary>
    public static class RenoiseInstrumentAudio
    {
        public const int MaximumPcmBytes = 256 * 1024 * 1024;
        public sealed class PcmBudgetException : IOException
        {
            public PcmBudgetException() : base("Sample exceeds the remaining decoded PCM memory budget; reduce the sample set before importing.") { }
        }
        public sealed class Pcm
        {
            public int Rate, Channels;
            public float[] Values;
            public int Frames => Values.Length / Channels;
        }

        public static Pcm Decode(byte[] source, int maximumBytes = MaximumPcmBytes)
        {
            if (source == null || source.Length < 12) throw new InvalidDataException("Missing or truncated sample header.");
            if (maximumBytes < 4 || maximumBytes > MaximumPcmBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            if (Tag(source, 0, "RIFF") && Tag(source, 8, "WAVE")) return Wave(source, maximumBytes);
            if (Tag(source, 0, "fLaC")) return Flac(source, maximumBytes);
            throw new InvalidDataException("Only PCM/IEEE-float WAV and native FLAC samples are supported.");
        }

        public static byte[] ToWave(Pcm pcm)
        {
            Validate(pcm.Rate, pcm.Channels, pcm.Frames, MaximumPcmBytes);
            using (var stream = new MemoryStream(checked(44 + pcm.Values.Length * 4)))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Values.Length * 4);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((ushort)3);
                writer.Write((ushort)pcm.Channels); writer.Write(pcm.Rate); writer.Write(pcm.Rate * pcm.Channels * 4);
                writer.Write((ushort)(pcm.Channels * 4)); writer.Write((ushort)32);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Values.Length * 4);
                foreach (var value in pcm.Values) writer.Write(value);
                return stream.ToArray();
            }
        }

        static void Validate(int rate, int channels, long frames, int budget)
        {
            if (rate < 1000 || rate > 384000 || channels < 1 || channels > 2 || frames < 1)
                throw new InvalidDataException("Sample exceeds mono/stereo or 1–384 kHz limits, or has no declared frames.");
            if (frames * channels > budget / 4) throw new PcmBudgetException();
        }
        static bool Tag(byte[] data, int at, string s)
        {
            if (at < 0 || at + s.Length > data.Length) return false;
            for (int i = 0; i < s.Length; i++) if (data[at + i] != s[i]) return false;
            return true;
        }
        static int U16(byte[] b, int p) => b[p] | b[p + 1] << 8;
        static uint U32(byte[] b, int p) => (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24);
        static Pcm Wave(byte[] b, int budget)
        {
            long size = U32(b, 4) + 8L;
            if (size > b.Length || size < 12) throw new InvalidDataException("Truncated RIFF container.");
            int format = 0, channels = 0, rate = 0, bits = 0, align = 0, data = -1, length = 0;
            for (long p = 12; p + 8 <= size;)
            {
                int at = (int)p; long count = U32(b, at + 4), end = p + 8 + count;
                if (end > size) throw new InvalidDataException("Truncated WAV chunk.");
                if (Tag(b, at, "fmt "))
                {
                    if (count < 16 || format != 0) throw new InvalidDataException("Invalid WAV format chunk.");
                    format = U16(b, at + 8); channels = U16(b, at + 10); rate = checked((int)U32(b, at + 12));
                    align = U16(b, at + 20); bits = U16(b, at + 22);
                    if (format == 65534)
                    {
                        if (count < 40 || U16(b, at + 24) < 22 || U16(b, at + 26) != bits)
                            throw new InvalidDataException("Unsupported WAV extensible precision.");
                        byte[] guidTail = { 0, 0, 0, 0, 16, 0, 128, 0, 0, 170, 0, 56, 155, 113 };
                        for (int i = 0; i < guidTail.Length; i++) if (b[at + 34 + i] != guidTail[i]) throw new InvalidDataException("Unsupported WAV subformat.");
                        format = U16(b, at + 32);
                    }
                }
                else if (Tag(b, at, "data"))
                {
                    if (data >= 0) throw new InvalidDataException("Multiple WAV data chunks are unsupported.");
                    data = at + 8; length = checked((int)count);
                }
                p = end + (count & 1);
            }
            if (data < 0 || (format != 1 && format != 3) || (bits != 8 && bits != 16 && bits != 24 && bits != 32) ||
                (format == 3 && bits != 32) || align != channels * (bits / 8) || align == 0 || length % align != 0)
                throw new InvalidDataException("Unsupported or malformed PCM WAV.");
            Validate(rate, channels, length / align, budget);
            var values = new float[length / (bits / 8)];
            for (int i = 0, at = data; i < values.Length; i++, at += bits / 8)
            {
                float value;
                if (format == 3) value = BitConverter.ToSingle(b, at);
                else if (bits == 8) value = (b[at] - 128) / 128f;
                else if (bits == 16) value = (short)U16(b, at) / 32768f;
                else if (bits == 24) value = ((b[at] << 8 | b[at + 1] << 16 | b[at + 2] << 24) >> 8) / 8388608f;
                else value = (int)U32(b, at) / 2147483648f;
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Non-finite sample value.");
                values[i] = value;
            }
            return new Pcm { Rate = rate, Channels = channels, Values = values };
        }

        // FLAC framing, predictor and Rice residual rules: RFC 9639 sections 8–9.
        static Pcm Flac(byte[] b, int budget)
        {
            var reader = new Bits(b, 4); bool last = false; int rate = 0, channels = 0, precision = 0, maxBlock = 0;
            long frames = 0; byte[] digest = null;
            while (!last)
            {
                last = reader.Read(1) != 0; int type = (int)reader.Read(7), count = (int)reader.Read(24), at = reader.Byte;
                if (count > b.Length - at) throw new InvalidDataException("Truncated FLAC metadata.");
                if (rate == 0 && type != 0) throw new InvalidDataException("FLAC STREAMINFO must come first.");
                if (type == 127) throw new InvalidDataException("Invalid FLAC metadata type.");
                if (type == 0)
                {
                    if (rate != 0 || count != 34) throw new InvalidDataException("Invalid FLAC STREAMINFO.");
                    reader.Read(16); maxBlock = (int)reader.Read(16); reader.Read(24); reader.Read(24);
                    rate = (int)reader.Read(20); channels = (int)reader.Read(3) + 1; precision = (int)reader.Read(5) + 1;
                    frames = (long)reader.Read(36); digest = new byte[16];
                    for (int i = 0; i < 16; i++) digest[i] = (byte)reader.Read(8);
                    Validate(rate, channels, frames, budget);
                    if (precision < 4 || precision > 32 || maxBlock < 16) throw new InvalidDataException("Invalid FLAC precision or block size.");
                }
                reader.Seek(at + count);
            }
            var result = new Pcm { Rate = rate, Channels = channels, Values = new float[checked((int)frames * channels)] };
            long decoded = 0, frameNumber = 0; int fixedBlock = 0;
            using (var md5 = MD5.Create())
            {
                var digestBuffer = new byte[maxBlock * channels * ((precision + 7) / 8)];
                while (reader.Byte < b.Length)
                {
                    int start = reader.Byte;
                    if (reader.Read(14) != 0x3ffe || reader.Read(1) != 0) throw new InvalidDataException("Invalid FLAC frame sync.");
                    bool variable = reader.Read(1) != 0;
                    int blockCode = (int)reader.Read(4), rateCode = (int)reader.Read(4), assignment = (int)reader.Read(4), bitsCode = (int)reader.Read(3);
                    if (reader.Read(1) != 0 || blockCode == 0 || rateCode == 15 || assignment > 10 || bitsCode == 3)
                        throw new InvalidDataException("Reserved FLAC frame header.");
                    long number = reader.Utf8Integer();
                    int block = blockCode == 1 ? 192 : blockCode <= 5 ? 576 << (blockCode - 2) : blockCode == 6 ? (int)reader.Read(8) + 1 : blockCode == 7 ? (int)reader.Read(16) + 1 : 256 << (blockCode - 8);
                    int[] rates = { rate, 88200, 176400, 192000, 8000, 16000, 22050, 24000, 32000, 44100, 48000, 96000 };
                    int frameRate = rateCode < 12 ? rates[rateCode] : rateCode == 12 ? (int)reader.Read(8) * 1000 : rateCode == 13 ? (int)reader.Read(16) : (int)reader.Read(16) * 10;
                    int[] precisions = { precision, 8, 12, 0, 16, 20, 24, 32 };
                    int frameChannels = assignment < 8 ? assignment + 1 : 2;
                    int headerEnd = reader.Byte;
                    if (reader.Read(8) != Crc(b, start, headerEnd, 8, 7)) throw new InvalidDataException("FLAC header CRC mismatch.");
                    if (block > maxBlock || decoded + block > frames || channels != frameChannels || frameRate != rate || precisions[bitsCode] != precision ||
                        number != (variable ? decoded : frameNumber)) throw new InvalidDataException("Inconsistent FLAC frame geometry or sequence.");
                    if (!variable && fixedBlock != 0 && block != fixedBlock && decoded + block != frames) throw new InvalidDataException("Inconsistent fixed FLAC block size.");
                    if (fixedBlock == 0) fixedBlock = block;
                    var samples = new long[channels][];
                    for (int c = 0; c < channels; c++)
                        samples[c] = Subframe(reader, block, precision + ((assignment == 8 && c == 1) || (assignment == 9 && c == 0) || (assignment == 10 && c == 1) ? 1 : 0));
                    reader.AlignZero(); int footer = reader.Byte;
                    if (reader.Read(16) != Crc(b, start, footer, 16, 0x8005)) throw new InvalidDataException("FLAC frame CRC mismatch.");
                    int digestLength = 0; double divisor = Math.Pow(2, precision - 1);
                    for (int i = 0; i < block; i++)
                    {
                        if (assignment == 8) samples[1][i] = samples[0][i] - samples[1][i];
                        else if (assignment == 9) samples[0][i] += samples[1][i];
                        else if (assignment == 10)
                        {
                            long middle = (samples[0][i] << 1) | (samples[1][i] & 1), side = samples[1][i];
                            samples[0][i] = (middle + side) >> 1; samples[1][i] = (middle - side) >> 1;
                        }
                        for (int c = 0; c < channels; c++)
                        {
                            long value = samples[c][i];
                            if (value < -divisor || value >= divisor) throw new InvalidDataException("FLAC decoded sample exceeds precision.");
                            result.Values[checked((int)(decoded + i) * channels + c)] = (float)(value / divisor);
                            for (int j = 0; j < (precision + 7) / 8; j++) digestBuffer[digestLength++] = (byte)(value >> (8 * j));
                        }
                    }
                    md5.TransformBlock(digestBuffer, 0, digestLength, digestBuffer, 0);
                    decoded += block; frameNumber++;
                }
                if (decoded != frames) throw new InvalidDataException("Truncated FLAC sample stream.");
                md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                bool present = false; foreach (byte value in digest) present |= value != 0;
                if (present) for (int i = 0; i < 16; i++) if (digest[i] != md5.Hash[i]) throw new InvalidDataException("FLAC PCM digest mismatch.");
            }
            return result;
        }

        static long[] Subframe(Bits reader, int count, int precision)
        {
            if (reader.Read(1) != 0) throw new InvalidDataException("Reserved FLAC subframe bit.");
            int type = (int)reader.Read(6), wasted = reader.Read(1) == 0 ? 0 : reader.Unary() + 1;
            precision -= wasted;
            if (precision < 1 || precision > 33) throw new InvalidDataException("Invalid FLAC wasted precision.");
            var values = new long[count];
            if (type == 0) { long value = reader.Signed(precision); for (int i = 0; i < count; i++) values[i] = value; }
            else if (type == 1) { for (int i = 0; i < count; i++) values[i] = reader.Signed(precision); }
            else
            {
                int order = type >= 32 ? (type & 31) + 1 : type >= 8 && type <= 12 ? type - 8 : -1;
                if (order < 0 || order > count) throw new InvalidDataException("Unsupported FLAC subframe predictor.");
                for (int i = 0; i < order; i++) values[i] = reader.Signed(precision);
                long[] coefficients = null; int shift = 0;
                if (type >= 32)
                {
                    int width = (int)reader.Read(4) + 1;
                    if (width == 16) throw new InvalidDataException("Reserved FLAC coefficient precision.");
                    shift = (int)reader.Signed(5);
                    if (shift < 0) throw new InvalidDataException("Negative FLAC predictor shift is unsupported.");
                    coefficients = new long[order]; for (int i = 0; i < order; i++) coefficients[i] = reader.Signed(width);
                }
                Residual(reader, values, order);
                checked
                {
                    for (int i = order; i < count; i++)
                    {
                        long prediction = 0;
                        if (coefficients != null) { for (int j = 0; j < order; j++) prediction += coefficients[j] * values[i - j - 1]; prediction >>= shift; }
                        else if (order == 1) prediction = values[i - 1];
                        else if (order == 2) prediction = 2 * values[i - 1] - values[i - 2];
                        else if (order == 3) prediction = 3 * values[i - 1] - 3 * values[i - 2] + values[i - 3];
                        else if (order == 4) prediction = 4 * values[i - 1] - 6 * values[i - 2] + 4 * values[i - 3] - values[i - 4];
                        values[i] += prediction;
                    }
                }
            }
            long limit = 1L << (precision - 1);
            for (int i = 0; i < count; i++)
            {
                if (values[i] < -limit || values[i] >= limit) throw new InvalidDataException("FLAC predictor exceeds sample precision.");
                values[i] <<= wasted;
            }
            return values;
        }
        static void Residual(Bits reader, long[] values, int order)
        {
            int method = (int)reader.Read(2), partitions = 1 << (int)reader.Read(4);
            if (method > 1 || values.Length % partitions != 0 || values.Length / partitions < order) throw new InvalidDataException("Invalid FLAC residual partition.");
            int width = method == 0 ? 4 : 5, per = values.Length / partitions, at = order;
            for (int p = 0; p < partitions; p++)
            {
                int parameter = (int)reader.Read(width), length = per - (p == 0 ? order : 0);
                if (parameter == (1 << width) - 1)
                {
                    int raw = (int)reader.Read(5); for (int i = 0; i < length; i++) values[at++] = raw == 0 ? 0 : reader.Signed(raw);
                }
                else for (int i = 0; i < length; i++)
                {
                    ulong folded = ((ulong)reader.Unary() << parameter) | reader.Read(parameter);
                    if (folded > uint.MaxValue) throw new InvalidDataException("FLAC residual exceeds 32 bits.");
                    values[at++] = (long)(folded >> 1) ^ -((long)folded & 1);
                }
            }
        }
        internal static uint Crc(byte[] b, int start, int end, int width, uint polynomial)
        {
            uint value = 0, mask = (1u << width) - 1;
            for (int i = start; i < end; i++)
            {
                value ^= (uint)b[i] << (width - 8);
                for (int bit = 0; bit < 8; bit++) value = ((value & (1u << (width - 1))) != 0 ? (value << 1) ^ polynomial : value << 1) & mask;
            }
            return value;
        }
        sealed class Bits
        {
            readonly byte[] data; long position;
            public Bits(byte[] data, int start) { this.data = data; position = (long)start * 8; }
            public int Byte => checked((int)(position / 8));
            public void Seek(int at) { if (at < 0 || at > data.Length) throw new InvalidDataException("FLAC seek beyond input."); position = (long)at * 8; }
            public ulong Read(int count)
            {
                if (count < 0 || count > 63 || position + count > (long)data.Length * 8) throw new InvalidDataException("Truncated FLAC bitstream.");
                ulong result = 0;
                while (count-- > 0) { result = result << 1 | (uint)((data[(int)(position >> 3)] >> (7 - (int)(position & 7))) & 1); position++; }
                return result;
            }
            public long Signed(int count) { ulong value = Read(count); return ((long)(value << (64 - count))) >> (64 - count); }
            public int Unary() { int zeros = 0; while (Read(1) == 0) { if (++zeros > 16777216) throw new InvalidDataException("FLAC unary code exceeds safety bound."); } return zeros; }
            public void AlignZero() { while ((position & 7) != 0) if (Read(1) != 0) throw new InvalidDataException("Nonzero FLAC padding."); }
            public long Utf8Integer()
            {
                int first = (int)Read(8);
                if (first < 128) return first;
                int count = 0, mask = 128; while ((first & mask) != 0 && mask != 0) { count++; mask >>= 1; }
                if (count < 2 || count > 7) throw new InvalidDataException("Invalid FLAC frame number.");
                long result = first & (127 >> count);
                for (int i = 1; i < count; i++) { int next = (int)Read(8); if ((next & 192) != 128) throw new InvalidDataException("Invalid FLAC number continuation."); result = result << 6 | (uint)(next & 63); }
                long minimum = count == 2 ? 128 : 1L << (5 * count - 4);
                if (result < minimum || result >= 1L << 36) throw new InvalidDataException("Overlong FLAC frame number.");
                return result;
            }
        }
    }
}
