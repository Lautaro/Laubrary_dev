using System.Collections.Generic;
using Laubrary.ZTracker.Model;

namespace Laubrary.ZTracker.Editor
{
    // Pasting across column types (T-0602). A copied cell lands in the column under it when the two hold the same kind
    // of thing: the same column type, volume and pan (both a 00–80 value or a column command), a note column's sample
    // command and a track FX command (an id and its value), and two envelope columns. Anything else is refused,
    // counted, and named on the status line, so nothing is half-pasted silently.
    public sealed partial class ZTrackerModelWindow
    {
        sealed class PasteRefusals
        {
            public int count;
            FieldKind from, to;
            string why;
            public void Add(FieldKind source, FieldKind target, string reason = null) { if (count++ == 0) { from = source; to = target; why = reason; } }
            public string Notice(string verb) => count == 0 ? "" : "⚠ " + verb + " skipped " + Plural(count, "cell") + ": " + (why ?? "a " + Describe(from) + " cell cannot go into a " + Describe(to) + " column") + (count > 1 ? " (and others like it)." : ".");
            static string Describe(FieldKind kind)
            {
                switch (kind)
                {
                    case FieldKind.Note: return "note"; case FieldKind.Instrument: return "instrument"; case FieldKind.Volume: return "volume"; case FieldKind.Pan: return "pan"; case FieldKind.Delay: return "delay";
                    case FieldKind.LocalId: case FieldKind.EffectId: return "command id"; case FieldKind.LocalValue: case FieldKind.EffectValue: return "command value";
                    case FieldKind.Event: return "event"; case FieldKind.Envelope: return "envelope"; default: return kind.ToString();
                }
            }
        }

        /// Whether a copied cell of one column type can be pasted into another.
        internal static bool PasteFits(FieldKind from, FieldKind to)
            => from == to
               || (from == FieldKind.Volume || from == FieldKind.Pan) && (to == FieldKind.Volume || to == FieldKind.Pan)
               || (from == FieldKind.LocalId || from == FieldKind.EffectId) && (to == FieldKind.LocalId || to == FieldKind.EffectId)
               || (from == FieldKind.LocalValue || from == FieldKind.EffectValue) && (to == FieldKind.LocalValue || to == FieldKind.EffectValue);

        /// Writes one copied cell at a row of a field, converting between compatible column types; refusals are counted.
        void PasteCell(int r, FieldSlot f, CopiedField p, PasteRefusals refused)
        {
            if (!PasteFits(p.kind, f.kind)) { refused.Add(p.kind, f.kind); return; }
            if (f.kind == FieldKind.Envelope)
            {
                if (!PasteEnvelopeRow(f, r, p.points)) refused.Add(p.kind, f.kind, "ENV" + (f.column + 1) + " is not bound to a command (Alt+B binds it)");
                return;
            }
            EditField(r, f, (n, x, v) =>
            {
                if (p.kind == f.kind) { CopyField(f.kind, n, x, v, p.note, p.effect, p.evt); return; }
                switch (f.kind)
                {
                    case FieldKind.Volume: n.volume = Clone(p.note.pan); break;
                    case FieldKind.Pan: n.pan = Clone(p.note.volume); break;
                    case FieldKind.EffectId: x.command = Clone(p.note.sampleFx); break;
                    case FieldKind.LocalId: n.sampleFx = Clone(p.effect.command); break;
                    case FieldKind.EffectValue: x.command.valuePresent = p.note.sampleFx.valuePresent; x.command.value = p.note.sampleFx.value; x.command.rawValue = p.note.sampleFx.rawValue; break;
                    case FieldKind.LocalValue: n.sampleFx.valuePresent = p.effect.command.valuePresent; n.sampleFx.value = p.effect.command.value; n.sampleFx.rawValue = p.effect.command.rawValue; break;
                }
            });
        }
    }
}
