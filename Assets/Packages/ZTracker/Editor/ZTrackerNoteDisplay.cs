using Laubrary.ZTracker.Model;

namespace Laubrary.ZTracker.Editor
{
    // A missing mapping must never hide the authored pitch. Invalid retained values stay visibly invalid.
    internal static class ZTrackerNoteDisplay
    {
        static readonly string[] Names = { "C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-" };

        internal static string Pitch(int value, int maximum = 119) => value < 0 || value > maximum ? "ERR" : Names[value % 12] + (value / 12);

        internal static string Cell(NoteCell value)
        {
            if (value == null || value.note == NoteKind.Empty) return "---";
            if (value.note == NoteKind.Off) return "OFF";
            if (value.note == NoteKind.Note) return Pitch(value.pitch);
            if (value.note == NoteKind.Legacy) return value.legacyNote == 127 ? "OFF" : Pitch(value.legacyNote, 126);
            return "ERR";
        }

        internal static string Legacy(int value, ZTrackerInstrument instrument)
        {
            if (value < 0) return "...";
            if (value == 127) return "OFF";
            string pitch = Pitch(value, 126);
            if (pitch == "ERR") return pitch;
            string mapped = instrument != null ? instrument.GetNoteDisplayName(value) : null;
            return string.IsNullOrWhiteSpace(mapped) ? pitch : mapped;
        }
    }
}
