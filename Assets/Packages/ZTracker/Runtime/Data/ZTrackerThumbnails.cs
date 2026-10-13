using System;
using UnityEngine;

namespace Laubrary.ZTracker.Model
{
    /// <summary>
    /// The pictures ZTracker assets show in every Laubrary asset browser and picker (through IVisualPreview): a song
    /// is drawn as its first played pattern (one column per note track, a mark per note in the track's colour), a
    /// sampler as the waveform of its first sample, a synth as its amplitude envelope and a drum kit as its pads.
    /// Fresh textures each call; the caller owns and destroys them. Nothing here reads or writes the asset's data
    /// beyond looking at it, so drawing a thumbnail never dirties a song or an instrument.
    /// </summary>
    public static class ZTrackerThumbnails
    {
        public const int Size = 64;
        static readonly Color Back = new Color32(0x1A, 0x27, 0x32, 0xFF), Beat = new Color32(0x22, 0x2E, 0x38, 0xFF), Line = new Color32(0x86, 0xC0, 0xFF, 0xFF);

        static Texture2D Canvas(out Color[] px)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            px = new Color[Size * Size];
            for (int i = 0; i < px.Length; i++) px[i] = Back;
            return tex;
        }

        static Texture2D Finish(Texture2D tex, Color[] px) { tex.SetPixels(px); tex.Apply(false); return tex; }

        // Texture rows run bottom-up; y here counts from the top so the picture reads like the pattern grid.
        static void Plot(Color[] px, int x, int y, Color c) { if (x >= 0 && x < Size && y >= 0 && y < Size) px[(Size - 1 - y) * Size + x] = c; }
        static void Fill(Color[] px, int x0, int y0, int x1, int y1, Color c) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Plot(px, x, y, c); }

        public static Texture2D Song(SongData song)
        {
            var tex = Canvas(out var px);
            if (song == null) return Finish(tex, px);
            PatternData pattern = null;
            if (song.sequence != null && song.sequence.Count > 0 && song.patterns != null) pattern = song.patterns.Find(p => p != null && p.id == song.sequence[0].patternId);
            if (pattern == null && song.patterns != null && song.patterns.Count > 0) pattern = song.patterns[0];
            int lines = Math.Max(1, pattern?.lineCount ?? 64), lpb = Math.Max(1, song.linesPerBeat);
            for (int l = 0; l < lines; l += lpb) { int y = l * Size / lines; Fill(px, 0, y, Size - 1, y, Beat); }
            int columns = 0;
            foreach (var t in song.tracks) if (t != null && t.kind == TrackKind.Sequencer) columns++;
            if (columns == 0 || pattern == null) return Finish(tex, px);
            int width = Math.Max(2, (Size - 2) / columns), at = 0;
            foreach (var t in song.tracks)
            {
                if (t == null || t.kind != TrackKind.Sequencer) continue;
                int x0 = 1 + at * width; at++;
                var color = t.color.a > 0 ? t.color : Line; color.a = 1;
                if (color.maxColorComponent < .35f) color = Line;
                var pt = pattern.tracks?.Find(p => p != null && p.trackId == t.id);
                if (pt?.lines == null) continue;
                int h = Math.Max(2, Size / lines);
                foreach (var line in pt.lines)
                {
                    if (line?.notes == null || line.line < 0 || line.line >= lines) continue;
                    foreach (var n in line.notes)
                    {
                        if (n == null || n.inactive || n.note != NoteKind.Note) continue;
                        // A note's pitch moves its mark across the track's column, so melodies read as shapes.
                        int span = Math.Max(1, width - 2), x = x0 + (int)(Mathf.Clamp01(n.pitch / 119f) * (span - 1));
                        int y = line.line * Size / lines;
                        Fill(px, x, y, Math.Min(x + 2, x0 + span), y + h - 1, color);
                    }
                }
            }
            return Finish(tex, px);
        }

        public static Texture2D Instrument(InstrumentData data)
        {
            var tex = Canvas(out var px);
            if (data == null) return Finish(tex, px);
            Fill(px, 0, Size / 2, Size - 1, Size / 2, Beat);
            if (data.family == InstrumentFamily.DrumKit) { DrumPads(px, data.drumKit); return Finish(tex, px); }
            if (data.family == InstrumentFamily.Sampler && Waveform(px, data.sampler)) return Finish(tex, px);
            Envelope(px, data.parameters);
            return Finish(tex, px);
        }

        static bool Waveform(Color[] px, SamplerData sampler)
        {
            var sample = sampler?.samples?.Find(s => s != null && !s.inactive && s.pcm != null);
            var clip = sample?.pcm;
            if (clip == null || clip.samples < 2) return false;
            float[] data;
            try
            {
                int channels = Math.Max(1, clip.channels);
                data = new float[clip.samples * channels];
                if (!clip.GetData(data, 0)) return false;
                int frames = clip.samples;
                for (int x = 0; x < Size; x++)
                {
                    int a = (int)((long)x * frames / Size), b = Math.Max(a + 1, (int)((long)(x + 1) * frames / Size));
                    float lo = 0, hi = 0;
                    for (int f = a; f < b && f < frames; f += Math.Max(1, (b - a) / 64)) { float v = data[f * channels]; lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
                    int y0 = (int)((1 - Mathf.Clamp(hi, -1, 1)) * .5f * (Size - 1)), y1 = (int)((1 - Mathf.Clamp(lo, -1, 1)) * .5f * (Size - 1));
                    Fill(px, x, Math.Min(y0, y1), x, Math.Max(y0, y1), Line);
                }
                return true;
            }
            catch (Exception) { return false; }   // a clip whose samples cannot be read: draw the envelope instead
        }

        static void Envelope(Color[] px, InstrumentParameters q)
        {
            if (q == null) return;
            float attack = Mathf.Max(0, q.attack), decay = Mathf.Max(0, q.decay), release = Mathf.Max(0, q.release), sustain = Mathf.Clamp01(q.sustain);
            float hold = .3f, total = Mathf.Max(.001f, attack + decay + hold + release);
            Vector2 P(float t, float level) => new Vector2(t / total * (Size - 1), (1 - level) * (Size - 8) + 4);
            var points = new[] { P(0, 0), P(attack, 1), P(attack + decay, sustain), P(attack + decay + hold, sustain), P(total, 0) };
            var color = new Color32(0x8B, 0xE0, 0x7A, 0xFF);
            for (int i = 0; i + 1 < points.Length; i++)
            {
                var a = points[i]; var b = points[i + 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y))));
                for (int s = 0; s <= steps; s++) { var p = Vector2.Lerp(a, b, s / (float)steps); Plot(px, (int)p.x, (int)p.y, color); Plot(px, (int)p.x, (int)p.y + 1, color); }
            }
        }

        static void DrumPads(Color[] px, DrumKitData kit)
        {
            int count = kit?.mappings?.Count ?? 0;
            var on = new Color32(0xF2, 0xC6, 0x6D, 0xFF);
            for (int i = 0; i < 16; i++)
            {
                int x = 4 + (i % 4) * 15, y = 4 + (i / 4) * 15;
                Fill(px, x, y, x + 11, y + 11, i < count ? (Color)on : Beat);
            }
        }
    }
}
