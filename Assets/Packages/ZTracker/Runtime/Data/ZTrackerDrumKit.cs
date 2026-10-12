using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Laubrary.ZTracker.Model
{
    public enum DrumCategory { Kick, Snare, Hats, Percussion, Shaker, Misc, FX }

    [Serializable] public sealed class DrumMapping
    {
        public string id = "", name = "", tag = "Misc", sourceUrl = "";
        public int noteMin = 60, noteMax = 60, baseNote = 60;
        public bool keyTracking;
        public AudioClip pcm;
        public ZTrackerInstrument synth;
        public float volume = 1;
        // A struck synth enters its release after this gate, even if its envelope sustains.
        public float gateSeconds = .1f;
    }

    [Serializable] public sealed class DrumKitData
    {
        public List<DrumMapping> mappings = new List<DrumMapping>();
        public DrumMapping Find(int note) => mappings?.Find(m => m != null && note >= m.noteMin && note <= m.noteMax);
    }

    public static class DrumTags
    {
        public static string Normalize(string value)
        {
            var result = new StringBuilder();
            if (value != null) foreach (char c in value) if (!char.IsWhiteSpace(c)) result.Append(char.ToLowerInvariant(c));
            return result.ToString();
        }
        public static DrumCategory Category(string value)
        {
            switch (Normalize(value))
            {
                case "kd": case "kick": case "kicks": case "kickdrum": case "kickdrums": case "bd": case "bassdrum": case "bassdrums": return DrumCategory.Kick;
                case "snare": case "snares": case "snaredrum": case "snaredrums": return DrumCategory.Snare;
                case "hats": case "hh": case "hihat": case "hihats": return DrumCategory.Hats;
                case "percussion": return DrumCategory.Percussion;
                case "shaker": case "shakers": return DrumCategory.Shaker;
                case "fx": case "sfx": case "effects": return DrumCategory.FX;
                default: return DrumCategory.Misc;
            }
        }
        public static string Canonical(string value)
        {
            var category = Category(value);
            return category != DrumCategory.Misc || Normalize(value) == "misc" || string.IsNullOrWhiteSpace(value) ? category.ToString() : value.Trim();
        }
        public static bool Matches(string text, string query) => string.IsNullOrWhiteSpace(query) || Normalize(Canonical(text)).Contains(Normalize(Canonical(query)));
    }
}
