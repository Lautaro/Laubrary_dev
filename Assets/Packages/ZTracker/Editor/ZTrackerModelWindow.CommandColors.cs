using System.Collections.Generic;
using Laubrary.ZTracker.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>
    /// How a command id is drawn (T-0600). Built-in commands are coloured text on the dark grid, one colour per kind;
    /// the song's own commands are dark text on their own colour (automatic and stable, or chosen in Song › Commands);
    /// an id that names nothing is white on red. Colours are view-only: nothing that saves, plays or copies reads them.
    /// </summary>
    internal enum CommandKind { None, Pitch, Volume, Pan, Sample, Timing, Device, Song }

    internal readonly struct CommandLook
    {
        internal readonly bool present, known, user;
        internal readonly CommandKind kind;
        internal readonly Color color;              // text colour of a built-in, background of a song command
        internal readonly DynamicParameterData definition;
        internal CommandLook(bool present, bool known, bool user, CommandKind kind, Color color, DynamicParameterData definition)
        { this.present = present; this.known = known; this.user = user; this.kind = kind; this.color = color; this.definition = definition; }
        internal static readonly CommandLook Empty = default;
    }

    internal static class CommandColors
    {
        static readonly Color32 Pitch = new Color32(0x86, 0xC0, 0xFF, 0xFF), Volume = new Color32(0x8B, 0xE0, 0x7A, 0xFF), Pan = new Color32(0xC8, 0xAE, 0xFF, 0xFF),
            Sample = new Color32(0xF2, 0xC6, 0x6D, 0xFF), Timing = new Color32(0xFF, 0x9E, 0x7A, 0xFF), Device = new Color32(0x5F, 0xD7, 0xD7, 0xFF), Song = new Color32(0xFF, 0xA6, 0xD8, 0xFF);
        /// Dark text on a song command's colour; white on a colour too dark for it (a user-chosen dark colour).
        internal static readonly Color DarkText = new Color32(0x14, 0x14, 0x14, 0xFF);
        internal static readonly Color UndefinedBack = new Color32(0xC6, 0x28, 0x28, 0xFF), UndefinedText = Color.white;
        /// The grid's background (measured on the ZTracker skin) and the selection tint over it, for blending and contrast measurement.
        internal static readonly Color GridBack = new Color32(0x1A, 0x27, 0x32, 0xFF), SelectionTint = new Color(90 / 255f, 145 / 255f, 200 / 255f, .3f);

        internal static Color KindColor(CommandKind kind)
        {
            switch (kind)
            {
                case CommandKind.Pitch: return Pitch;
                case CommandKind.Volume: return Volume;
                case CommandKind.Pan: return Pan;
                case CommandKind.Sample: return Sample;
                case CommandKind.Timing: return Timing;
                case CommandKind.Device: return Device;
                case CommandKind.Song: return Song;
                default: return new Color(.78f, .78f, .78f);
            }
        }

        internal static string KindName(CommandKind kind) => kind == CommandKind.Device ? "Track" : kind.ToString();

        /// Kind of a built-in command id (two characters, or a volume/pan column letter).
        internal static CommandKind KindOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return CommandKind.None;
            if (id[0] == 'Z') return CommandKind.Song;
            char c = id.Length == 2 && id[0] == '0' ? id[1] : id.Length == 1 ? id[0] : '\0';
            switch (c)
            {
                case 'A': case 'U': case 'D': case 'G': case 'V': return CommandKind.Pitch;
                case 'I': case 'O': case 'T': case 'C': case 'M': case 'L': return id.Length == 1 && c == 'C' ? CommandKind.Timing : CommandKind.Volume;
                case 'N': case 'P': case 'J': case 'K': return id.Length == 2 && c == 'J' ? CommandKind.Device : CommandKind.Pan;
                case 'S': case 'B': case 'E': case 'Y': return CommandKind.Sample;
                case 'Q': case 'R': case 'X': return CommandKind.Timing;
            }
            return CommandKind.Device;
        }

        /// Text colour that reads on <paramref name="back"/>: the dark text, or white when the background is too dark for it.
        internal static Color TextOn(Color back) => Contrast(DarkText, back) >= Contrast(Color.white, back) ? DarkText : Color.white;

        /// What a selected song-command cell is filled with: its colour pulled toward the selection blue.
        internal static Color Selected(Color back) => Color.Lerp(back, new Color(.35f, .57f, .78f), .45f);

        // WCAG 2 relative luminance and contrast ratio, used for the measured ratios in the docs and the check.
        internal static float Luminance(Color c) => .2126f * Linear(c.r) + .7152f * Linear(c.g) + .0722f * Linear(c.b);
        static float Linear(float v) => v <= .04045f ? v / 12.92f : Mathf.Pow((v + .055f) / 1.055f, 2.4f);
        internal static float Contrast(Color a, Color b) { float la = Luminance(a), lb = Luminance(b); return (Mathf.Max(la, lb) + .05f) / (Mathf.Min(la, lb) + .05f); }
        internal static Color Over(Color top, Color back) => new Color(Mathf.Lerp(back.r, top.r, top.a), Mathf.Lerp(back.g, top.g, top.a), Mathf.Lerp(back.b, top.b, top.a), 1);

        /// Paints an id like a chip: song command = dark text on its colour, built-in = kind colour on the dark grid, undefined = white on red.
        internal static void Paint(VisualElement element, in CommandLook look, bool selected = false, float textAlpha = 1)
        {
            if (!look.present) { Clear(element); return; }
            Color text, back;
            if (!look.known) { back = selected ? Selected(UndefinedBack) : UndefinedBack; text = UndefinedText; }
            else if (look.user) { back = selected ? Selected(look.color) : look.color; text = TextOn(back); }
            else { back = Color.clear; text = look.color; }
            text.a *= textAlpha;
            element.style.color = text;
            if (back.a > 0) element.style.backgroundColor = back; else element.style.backgroundColor = StyleKeyword.Null;
        }

        internal static void Clear(VisualElement element) { element.style.color = StyleKeyword.Null; element.style.backgroundColor = StyleKeyword.Null; }
    }

    public sealed partial class ZTrackerModelWindow
    {
        // Song command ids → definition, refreshed once per grid refresh so every cell looks its id up without a scan.
        readonly Dictionary<string, DynamicParameterData> lookDefinitions = new Dictionary<string, DynamicParameterData>();

        void RefreshLookDefinitions()
        {
            lookDefinitions.Clear();
            if (Data?.dynamicParameters == null) return;
            foreach (var p in Data.dynamicParameters)
            {
                if (p == null || p.id == null || p.id.Length != 2) continue;
                string id = ZTrackerCommandRegistry.Normalize(p.id);
                if (!lookDefinitions.ContainsKey(id)) lookDefinitions[id] = p;
            }
        }

        /// How an id typed in a field of a track is drawn. Never allocates for an id typed in the grid.
        internal CommandLook Look(FieldKind kind, int trackIndex, string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return CommandLook.Empty;
            var builtin = FxCommandCatalog.Builtin(identifier, kind);
            if (builtin != null) return new CommandLook(true, true, false, builtin.kind, CommandColors.KindColor(builtin.kind), null);
            if (kind == FieldKind.EffectId || kind == FieldKind.EffectValue)
            {
                string id = identifier.Length == 2 && (char.IsLower(identifier[0]) || char.IsLower(identifier[1])) ? identifier.ToUpperInvariant() : identifier;
                if (lookDefinitions.TryGetValue(id, out var p)) return new CommandLook(true, true, true, CommandKind.None, ZTrackerCommandRegistry.ColorOf(p), p);
                if (trackIndex >= 0 && trackIndex < Data.tracks.Count && FxCommandCatalog.IsDeviceId(id, Data.tracks[trackIndex]))
                    return new CommandLook(true, true, false, CommandKind.Device, CommandColors.KindColor(CommandKind.Device), null);
            }
            return new CommandLook(true, false, false, CommandKind.None, CommandColors.UndefinedBack, null);
        }

        /// The id stored under a field at a row (a command, never a plain value), or null.
        string CommandIdAt(int r, FieldSlot f)
        {
            switch (f.kind)
            {
                case FieldKind.EffectId: case FieldKind.EffectValue: { var c = Effect(r, f).command; return c.present ? c.identifier : null; }
                case FieldKind.LocalId: case FieldKind.LocalValue: { var c = Note(r, f).sampleFx; return c.present ? c.identifier : null; }
                case FieldKind.Volume: { var v = Note(r, f).volume; return v.kind == ValueKind.Command && v.command.present ? v.command.identifier : null; }
                case FieldKind.Pan: { var v = Note(r, f).pan; return v.kind == ValueKind.Command && v.command.present ? v.command.identifier : null; }
            }
            return null;
        }

        /// A bound FX column's title in its command's colours (built-in text colour, song command chip, red when the command is gone).
        void PaintFxTitle(Label title, int trackIndex, int column)
        {
            if (BoundBroken(trackIndex, column)) { CommandColors.Paint(title, new CommandLook(true, false, false, CommandKind.None, CommandColors.UndefinedBack, null)); return; }
            string id = BoundId(trackIndex, column);
            if (id == null) return;
            RefreshLookDefinitions();
            var look = Look(FieldKind.EffectId, trackIndex, id);
            CommandColors.Paint(title, look);
            if (!look.user && look.known) title.style.backgroundColor = new Color(.14f, .14f, .14f, 1);
        }

        /// " — not in context …" for a song command typed on a row it does not belong to (it still plays), else "".
        string OutOfContext(FieldSlot f, string id)
        {
            if (!IsFx(f.kind) || Pattern == null) return "";
            var p = ZTrackerCommandRegistry.Find(Data, id);
            if (p == null || ZTrackerCommandRegistry.InContext(Data, Pattern.id, Data.tracks[f.track].id, row, order).Contains(p)) return "";
            string why = p.scope == DynamicScope.Instrument || p.scope == DynamicScope.InstrumentEffect ? "its instrument is not playing on this row" : "it belongs to " + (Data.tracks.Find(t => t != null && t.id == p.target.trackId)?.name ?? "another track");
            return " — not in context here (" + why + "); it still plays";
        }

        /// Colours a grid cell by the command it holds. The cursor cell keeps the cursor's own colours.
        void ColourCell(Label label, int r, FieldSlot f, bool picked, bool selected)
        {
            if (f.kind != FieldKind.EffectId && f.kind != FieldKind.EffectValue && f.kind != FieldKind.LocalId && f.kind != FieldKind.LocalValue && f.kind != FieldKind.Volume && f.kind != FieldKind.Pan) return;
            string id = CommandIdAt(r, f);
            var look = id == null ? CommandLook.Empty : Look(f.kind, f.track, id);
            var bar = label.Q<VisualElement>("fx-bar");
            if (bar != null) bar.style.backgroundColor = look.present && look.known ? look.color : CommandColors.UndefinedBack;
            if (picked || !look.present || label.ClassListContains("tracker-bound-plot")) { CommandColors.Clear(label); return; }
            CommandColors.Paint(label, look, selected, label.ClassListContains("tracker-bound-id") ? .55f : 1);
        }
    }
}
