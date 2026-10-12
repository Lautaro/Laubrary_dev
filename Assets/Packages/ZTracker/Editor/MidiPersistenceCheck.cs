using System;
using System.Collections.Generic;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>Pure in-memory checks for MIDI preference round-tripping and preferred-device reconnect selection.</summary>
    public static class MidiPersistenceCheck
    {
        public static string Run()
        {
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            const string prefix = "check.";
            var authored = new MidiPreferenceSnapshot
            {
                enabled = true, record = true, device = "Controller A", channel = 5, octave = -2,
                curve = 2, bend = 7, modWheel = 6, buffer = 3
            };
            MidiPreferenceSnapshot.Write(prefix, authored,
                (key, value) => values[key] = value,
                (key, value) => values[key] = value,
                (key, value) => values[key] = value,
                (key, value) => values[key] = value);
            var restored = MidiPreferenceSnapshot.Read(prefix,
                (key, fallback) => values.TryGetValue(key, out var value) ? (string)value : fallback,
                (key, fallback) => values.TryGetValue(key, out var value) ? (int)value : fallback,
                (key, fallback) => values.TryGetValue(key, out var value) ? (float)value : fallback,
                (key, fallback) => values.TryGetValue(key, out var value) ? (bool)value : fallback);
            Need(restored.enabled && restored.record && restored.device == authored.device && restored.channel == authored.channel &&
                 restored.octave == authored.octave && restored.curve == authored.curve && restored.bend == authored.bend &&
                 restored.modWheel == authored.modWheel && restored.buffer == authored.buffer,
                 "MIDI preferences did not round-trip as a complete set.");
            var available = new List<string> { "Different controller", "Simulated keyboard" };
            Need(MidiPreferenceSnapshot.ResolveDevice("Controller A", available, "Simulated keyboard") == null,
                 "A missing saved controller silently selected another input.");
            available.Insert(0, "Controller A");
            Need(MidiPreferenceSnapshot.ResolveDevice("Controller A", available, "Simulated keyboard") == "Controller A",
                 "The saved controller was not selected when it reappeared.");
            Need(MidiPreferenceSnapshot.ResolveDevice("", available, "Simulated keyboard") == "Controller A",
                 "An unset preference did not select the first physical device.");
            return "PASS all MIDI settings round-trip in memory; missing preferred device waits; reconnect selects the saved device.";
        }

        static void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
