
//	Copyright (c) 2012 Calvin Rien
//        http://the.darktable.com
//
//	This software is provided 'as-is', without any express or implied warranty. In
//	no event will the authors be held liable for any damages arising from the use
//	of this software.
//
//	Permission is granted to anyone to use this software for any purpose,
//	including commercial applications, and to alter it and redistribute it freely,
//	subject to the following restrictions:
//
//	1. The origin of this software must not be misrepresented; you must not claim
//	that you wrote the original software. If you use this software in a product,
//	an acknowledgment in the product documentation would be appreciated but is not
//	required.
//
//	2. Altered source versions must be plainly marked as such, and must not be
//	misrepresented as being the original software.
//
//	3. This notice may not be removed or altered from any source distribution.
//
//  =============================================================================
//
//  derived from Gregorio Zanon's script
//  http://forum.unity3d.com/threads/119295-Writing-AudioListener.GetOutputData-to-wav-problem?p=806734&viewfull=1#post806734

// 
// Fork by R-WebsterNoble
// Optimized! Now 20 times quicker
// Easy to get byte[] instead of saving file

// Note: GetWav() with trimming returns the full buffer with a load of zeros on the end. 
// Use the length out parameter to know where the data stops.
//

using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class SavWav {
    private const uint HeaderSize = 44;
    private const float RescaleFactor = 32767; //to convert float to Int16

    /// <summary>
    /// Output files are frequently byte-copies of source library files, and File.Copy
    /// preserves the source's ReadOnly attribute (common for purchased/imported SFX packs
    /// that ship read-only). Clear it before overwriting so re-renders don't throw
    /// UnauthorizedAccessException. Public so every write/copy site in Zounds shares this one
    /// implementation — keeps the explanatory log consistent instead of duplicating it per call site.
    /// </summary>
    public static void ClearReadOnlyIfPresent(string filepath) {
        if (!File.Exists(filepath)) return;
        var attributes = File.GetAttributes(filepath);
        if ((attributes & FileAttributes.ReadOnly) != 0) {
            File.SetAttributes(filepath, attributes & ~FileAttributes.ReadOnly);
            Debug.LogWarning($"[Zounds] '{Path.GetFileName(filepath)}' was marked read-only on disk "
                + "(common when its source audio came from a purchased/imported SFX pack that ships "
                + "read-only files) — cleared the attribute so Zounds could write the rendered output. "
                + "No action needed; this is just making the change visible instead of silent.");
        }
    }

    public static void Save(string filename, AudioClip clip, bool trim = false) {
        if (!filename.ToLower().EndsWith(".wav")) {
            filename += ".wav";
        }

        var filepath = filename; // The absolute path is already passed from AudioRenderUtility

        // Make sure directory exists if user is saving to sub dir.
        Directory.CreateDirectory(Path.GetDirectoryName(filepath));
        ClearReadOnlyIfPresent(filepath);

        using (var fileStream = new FileStream(filepath, FileMode.Create))
        using (var writer = new BinaryWriter(fileStream)) {
            var wav = GetWav(clip, out var length, trim);
            writer.Write(wav, 0, (int)length);
        }
    }

    /// <summary>
    /// Saves a standard 16-bit PCM WAV and appends a custom RIFF chunk (id + UTF-8 payload)
    /// after the data chunk. Unknown chunks are ignored by Unity's importer and by WavDecoder,
    /// so the file plays normally while carrying out-of-band metadata (e.g. source provenance).
    /// chunkId must be exactly 4 ASCII chars.
    /// </summary>
    public static void Save(string filename, AudioClip clip, byte[] metadataChunk, string chunkId) {
        if (!filename.ToLower().EndsWith(".wav")) {
            filename += ".wav";
        }
        Directory.CreateDirectory(Path.GetDirectoryName(filename));
        ClearReadOnlyIfPresent(filename);

        var wav = GetWav(clip, out var length, false);

        byte[] output;
        if (metadataChunk != null && metadataChunk.Length > 0 && !string.IsNullOrEmpty(chunkId) && chunkId.Length == 4) {
            bool pad = (metadataChunk.Length & 1) == 1; // RIFF chunks are word-aligned.
            int extra = 8 + metadataChunk.Length + (pad ? 1 : 0);
            output = new byte[length + extra];
            Array.Copy(wav, 0, output, 0, (int)length);

            int o = (int)length;
            var id = Encoding.ASCII.GetBytes(chunkId);
            output[o++] = id[0]; output[o++] = id[1]; output[o++] = id[2]; output[o++] = id[3];
            var sz = BitConverter.GetBytes((uint)metadataChunk.Length);
            output[o++] = sz[0]; output[o++] = sz[1]; output[o++] = sz[2]; output[o++] = sz[3];
            Array.Copy(metadataChunk, 0, output, o, metadataChunk.Length);
            o += metadataChunk.Length;
            if (pad) output[o++] = 0;

            // Patch the RIFF chunk size (bytes 4..7) to cover the appended chunk.
            var riffSize = BitConverter.GetBytes((uint)(output.Length - 8));
            output[4] = riffSize[0]; output[5] = riffSize[1]; output[6] = riffSize[2]; output[7] = riffSize[3];
        }
        else {
            output = new byte[length];
            Array.Copy(wav, 0, output, 0, (int)length);
        }

        using (var fileStream = new FileStream(filename, FileMode.Create))
        using (var writer = new BinaryWriter(fileStream)) {
            writer.Write(output, 0, output.Length);
        }
    }

    public static byte[] GetWav(AudioClip clip, out uint length, bool trim = false) {
        var data = ConvertAndWrite(clip, out length, out var samples, trim);

        WriteHeader(data, clip, length, samples);

        return data;
    }

    private static byte[] ConvertAndWrite(AudioClip clip, out uint length, out uint samplesAfterTrimming, bool trim) {
        var samples = new float[clip.samples * clip.channels];

        clip.GetData(samples, 0);

        var sampleCount = samples.Length;

        var start = 0;
        var end = sampleCount - 1;

        if (trim) {
            for (var i = 0; i < sampleCount; i++) {
                if ((short)(samples[i] * RescaleFactor) == 0)
                    continue;

                start = i;
                break;
            }

            for (var i = sampleCount - 1; i >= 0; i--) {
                if ((short)(samples[i] * RescaleFactor) == 0)
                    continue;

                end = i;
                break;
            }
        }

        var buffer = new byte[(sampleCount * 2) + HeaderSize];

        var p = HeaderSize;
        for (var i = start; i <= end; i++) {
            var value = (short)(samples[i] * RescaleFactor);
            buffer[p++] = (byte)(value >> 0);
            buffer[p++] = (byte)(value >> 8);
        }

        length = p;
        samplesAfterTrimming = (uint)(end - start + 1);
        return buffer;
    }

    private static void AddDataToBuffer(byte[] buffer, ref uint offset, byte[] addBytes) {
        foreach (var b in addBytes) {
            buffer[offset++] = b;
        }
    }

    private static void WriteHeader(byte[] stream, AudioClip clip, uint length, uint samples) {
        var hz = (uint)clip.frequency;
        var channels = (ushort)clip.channels;

        var offset = 0u;

        var riff = Encoding.UTF8.GetBytes("RIFF");
        AddDataToBuffer(stream, ref offset, riff);

        var chunkSize = BitConverter.GetBytes(length - 8);
        AddDataToBuffer(stream, ref offset, chunkSize);

        var wave = Encoding.UTF8.GetBytes("WAVE");
        AddDataToBuffer(stream, ref offset, wave);

        var fmt = Encoding.UTF8.GetBytes("fmt ");
        AddDataToBuffer(stream, ref offset, fmt);

        var subChunk1 = BitConverter.GetBytes(16u);
        AddDataToBuffer(stream, ref offset, subChunk1);

        //const ushort two = 2;
        const ushort one = 1;

        var audioFormat = BitConverter.GetBytes(one);
        AddDataToBuffer(stream, ref offset, audioFormat);

        var numChannels = BitConverter.GetBytes(channels);
        AddDataToBuffer(stream, ref offset, numChannels);

        var sampleRate = BitConverter.GetBytes(hz);
        AddDataToBuffer(stream, ref offset, sampleRate);

        var byteRate = BitConverter.GetBytes(hz * channels * 2); // sampleRate * bytesPerSample*number of channels, here 44100*2*2
        AddDataToBuffer(stream, ref offset, byteRate);

        var blockAlign = (ushort)(channels * 2);
        AddDataToBuffer(stream, ref offset, BitConverter.GetBytes(blockAlign));

        ushort bps = 16;
        var bitsPerSample = BitConverter.GetBytes(bps);
        AddDataToBuffer(stream, ref offset, bitsPerSample);

        var dataString = Encoding.UTF8.GetBytes("data");
        AddDataToBuffer(stream, ref offset, dataString);

        var subChunk2 = BitConverter.GetBytes(samples * 2);
        AddDataToBuffer(stream, ref offset, subChunk2);
    }
}