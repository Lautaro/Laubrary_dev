using System;
using System.IO;

namespace Laubrary.ZTracker.Renoise
{
    /// <summary>Pure checks: synthetic PCM fixtures, never factory audio. Invoke Run directly, without Test Runner.</summary>
    public static class RenoiseInstrumentAudioCheck
    {
        // Original generated signals encoded by FFmpeg 8; metadata stripped except STREAMINFO.
        // Exact integer formulae below are the independent PCM oracle; STREAMINFO MD5 also verifies decoded bytes.
        // The complete compact fixtures are intentionally embedded so a clean checkout needs no encoder installation.
        const string Ramp = "ZkxhQ4AAACIJAAkAAAAAABIVBWIg8AAAAGCat2uefP41jY/xaMpQlE6F//hmCABfVUb8GPxb/J784ec3vv8//mPaIG7xaa3TP7w4dO8TJWGZ0IZRMBuPoliynUNs4rpvE+ADo4WhqHrrs9jmVXlb/44tbooNxKjTcTwf5Gk4VRcGOapyHkgABnY1HpF5+XrcjdavO468DNXGp5I5WMhyEETxeTrG7cl13mfQBzMPRJE1x2OAeqw=";
        const string Sine = "ZkxhQ4AAACIJAAkAAAAAACU4BWIi8AAAAGCDoN0CCAV7fKMLPNQL6sQ6//hmiABfXkYAAAqvFM8d1uYuJ5z6XN/fAApA2XINpKd06ly/dvXq1TzXFl4uZPiAkeOmSTiqPP9WxIXb16vW/1z4cKGToifEjpgs6wht/TsW5K7dt1/+ddmSZY4ICB8+aLvspqeUq1G/iF/GP+W39NeYwnZPiZPfgqAIkQRCIUCZZGjm8YQ4lIJCQFCQTVzMgyOZGSCMBQghQoCBim0zRzssEwLCCIiQtnM=";
        const string Silence = "ZkxhQ4AAACIJAAkAAAAAABIVBWIg8AAAAGC33V4BlO4KwIpLgCy3PYZ///hmCABfVQAAAOsz";
        public static byte[] FlacFixture() => System.Convert.FromBase64String(Ramp);

        public static string Run()
        {
            var pcm = RenoiseInstrumentAudio.Decode(FlacFixture());
            Need(pcm.Rate == 22050 && pcm.Channels == 1 && pcm.Frames == 96, "FLAC mono geometry");
            for (int i = 0; i < 96; i++) Need(pcm.Values[i] == ((i * 67) % 2000 - 1000) / 32768f, "FLAC predictor exact PCM " + i);
            pcm = RenoiseInstrumentAudio.Decode(System.Convert.FromBase64String(Sine));
            Need(pcm.Channels == 2 && pcm.Frames == 96, "FLAC stereo geometry");
            for (int i = 0; i < 96; i++) for (int c = 0; c < 2; c++)
                Need(pcm.Values[i * 2 + c] == (int)(12000 * Math.Sin(i * .23 + c * .08)) / 32768f, "FLAC stereo decorrelation exact PCM");
            pcm = RenoiseInstrumentAudio.Decode(System.Convert.FromBase64String(Silence));
            foreach (float sample in pcm.Values) Need(sample == 0, "FLAC constant subframe");
            var floatPcm = new RenoiseInstrumentAudio.Pcm { Channels = 2, Rate = 48000, Values = new[] { -1f, 1f, 0f, .125f, -.5f, .999f } };
            var wave = RenoiseInstrumentAudio.ToWave(floatPcm);
            pcm = RenoiseInstrumentAudio.Decode(wave);
            Need(pcm.Rate == 48000 && pcm.Channels == 2 && pcm.Frames == 3, "WAV float geometry");
            for (int i = 0; i < pcm.Values.Length; i++) Need(pcm.Values[i] == floatPcm.Values[i], "WAV float exact PCM");
            foreach (int bits in new[] { 8, 16, 24, 32 })
            {
                pcm = RenoiseInstrumentAudio.Decode(IntegerWave(bits));
                Need(pcm.Values[0] == -1 && pcm.Values[1] == 0, "Integer WAV signed boundary " + bits);
                Need(pcm.Values[2] == (float)(1 - 1 / Math.Pow(2, bits - 1)), "Integer WAV positive boundary " + bits);
            }
            Reject(() => RenoiseInstrumentAudio.Decode(new byte[20]), "unknown codec");
            Reject(() => RenoiseInstrumentAudio.Decode(wave, 4), "WAV decoded bound");
            Reject(() => RenoiseInstrumentAudio.Decode(FlacFixture(), 4), "FLAC decoded bound");
            var truncated = FlacFixture(); Array.Resize(ref truncated, truncated.Length - 1);
            Reject(() => RenoiseInstrumentAudio.Decode(truncated), "truncated FLAC");
            var badCrc = FlacFixture(); badCrc[badCrc.Length - 1] ^= 1;
            Reject(() => RenoiseInstrumentAudio.Decode(badCrc), "FLAC CRC");
            var badDigest = FlacFixture(); badDigest[26] ^= 1;
            Reject(() => RenoiseInstrumentAudio.Decode(badDigest), "FLAC MD5");
            var badFloat = (byte[])wave.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, badFloat, 44, 4);
            Reject(() => RenoiseInstrumentAudio.Decode(badFloat), "nonfinite float");
            var badLength = (byte[])wave.Clone(); Array.Copy(BitConverter.GetBytes(uint.MaxValue), 0, badLength, 40, 4);
            Reject(() => RenoiseInstrumentAudio.Decode(badLength), "WAV chunk bounds");
            return "PASS 16 Renoise audio groups: mono/stereo/constant FLAC exact PCM, float + 8/16/24/32-bit WAV, codecs/budgets/truncation/CRC/MD5/nonfinite/chunk bounds.";
        }
        public static byte[] IntegerWave(int bits)
        {
            int bytes = bits / 8, data = 3 * bytes;
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + data + (data & 1));
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((ushort)1);
                writer.Write((ushort)1); writer.Write(22050); writer.Write(22050 * bytes); writer.Write((ushort)bytes); writer.Write((ushort)bits);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(data);
                long minimum = -(1L << (bits - 1)), maximum = (1L << (bits - 1)) - 1;
                foreach (long value in new[] { minimum, 0L, maximum })
                    for (int i = 0; i < bytes; i++) writer.Write((byte)((bits == 8 ? value + 128 : value) >> (8 * i)));
                if ((data & 1) != 0) writer.Write((byte)0);
                return stream.ToArray();
            }
        }
        static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Reject(Action action, string message)
        {
            try { action(); } catch (InvalidDataException) { return; } catch (RenoiseInstrumentAudio.PcmBudgetException) { return; } catch (OverflowException) { return; }
            throw new InvalidOperationException("Expected refusal: " + message);
        }
    }
}
