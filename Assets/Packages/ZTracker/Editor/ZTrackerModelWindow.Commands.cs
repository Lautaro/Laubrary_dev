using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Audio;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>
    /// Command ids for the song's own settings (dynamic parameters, T-0595): right-click a setting (a mixer value, a track
    /// effect's setting, an instrument parameter or macro, the song tempo) to give it a two-character id and a name, and a
    /// compact list in the Song view to review, re-range, rename and remove them. The data and its rules live in
    /// ZTrackerCommandRegistry; this file only presents them.
    /// </summary>
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>One setting that can be given a command id, with its useful default range and hard limits.</summary>
        internal sealed class CommandTarget
        {
            public string label, units;
            public DynamicScope scope;
            public ParameterTarget target;
            public float min, max, lo, hi;
            public int decimals = 2;
        }

        static readonly string[] TrackParameterLabels = { "Pre gain", "Pre pan", "Width", "Post gain", "Post pan" };

        CommandTarget SongTempoTarget() => new CommandTarget { label = "Song / Tempo", units = "BPM", scope = DynamicScope.Song, target = new ParameterTarget { kind = ParameterKind.Timing, parameter = "bpm" }, min = 60, max = 240, lo = 32, hi = 999, decimals = 0 };

        static CommandTarget TrackTarget(TrackData t, string parameter)
        {
            int at = Array.IndexOf(ZTrackerCommandRegistry.TrackParameters, parameter);
            bool pan = parameter == "prePan" || parameter == "postPan";
            return new CommandTarget { label = t.name + " / " + TrackParameterLabels[at], units = pan ? "pan" : "×", scope = DynamicScope.Track, target = new ParameterTarget { kind = ParameterKind.Mixer, trackId = t.id, parameter = parameter }, min = pan ? -1 : 0, max = pan ? 1 : 2, lo = pan ? -1 : 0, hi = pan ? 1 : 4 };
        }

        static CommandTarget EffectTarget(TrackData t, AudioEffectNodeData node, int index)
        {
            var desc = ZoundEffectDescriptors.Get(node.type);
            if (desc == null || index < 0 || index >= desc.parameters.Length) return null;
            var pd = desc.parameters[index];
            if (!pd.automatable || pd.IsChoice || pd.curve == ParamCurve.Toggle || pd.curve == ParamCurve.Integer || node.type == ZoundEffectType.Delay && index == 3) return null;
            int position = t.devices.nodes.IndexOf(node) + 1;
            return new CommandTarget { label = t.name + " / " + desc.displayName + " " + position + " / " + pd.name, units = pd.unit, scope = DynamicScope.Effect, target = new ParameterTarget { kind = ParameterKind.Device, trackId = t.id, deviceId = node.uid, index = index, parameter = (index + 1).ToString() }, min = pd.min, max = pd.max, lo = pd.min, hi = pd.max, decimals = pd.max - pd.min > 100 ? 0 : 2 };
        }

        static CommandTarget InstrumentTarget(ZTrackerInstrument asset, TrackerParameter p)
        {
            var d = asset.model; bool sampler = d.family == InstrumentFamily.Sampler, fm = !sampler && d.synthMode == SynthMode.FM;
            if ((p >= TrackerParameter.Op0Ratio || p == TrackerParameter.FMFeedback) && !fm) return null;
            if ((p == TrackerParameter.UnisonDetune || p == TrackerParameter.UnisonSpread || p == TrackerParameter.PulseWidth || p == TrackerParameter.WaveBRatio) && (sampler || fm)) return null;
            if ((p == TrackerParameter.Blend || p == TrackerParameter.PMDepth) && (fm || sampler && !d.sampler.zones.Any(z => z.blend?.pcmB != null))) return null;
            string key = ParameterName(p);
            string label = p >= TrackerParameter.Op0Ratio ? "Operator " + TrackerFmSetups.Letter(TrackerParameters.OperatorOf(p)) + " " + FieldLabel(key.Substring(key.LastIndexOf('.') + 1)) : FieldLabel(key);
            var r = CommandRange(p);
            return new CommandTarget { label = InstrumentTitle(asset) + " / " + label, units = TrackerParameters.Units(p), scope = DynamicScope.Instrument, target = new ParameterTarget { kind = sampler ? ParameterKind.Sample : ParameterKind.Synth, instrumentId = d.id, parameter = key, units = TrackerParameters.Units(p) }, min = r.x, max = r.y, lo = TrackerParameters.Clamp(p, -1e9f), hi = Math.Min(TrackerParameters.Clamp(p, 1e9f), r.y * 8 + 1), decimals = 2 };
        }

        static CommandTarget MacroTarget(ZTrackerInstrument asset, int macro)
            => new CommandTarget { label = InstrumentTitle(asset) + " / Macro " + (macro + 1) + (asset.model.macros[macro] != null && asset.model.macros[macro].name != "" && asset.model.macros[macro].name != "Macro " + (macro + 1) ? " " + asset.model.macros[macro].name : ""), units = "0–1", scope = DynamicScope.Instrument, target = new ParameterTarget { kind = ParameterKind.InstrumentMacro, instrumentId = asset.model.id, index = macro, parameter = "macro" }, min = 0, max = 1, lo = 0, hi = 1 };

        /// <summary>The useful part of an instrument parameter's range, in its own units (what 00 and FF mean by default).</summary>
        static Vector2 CommandRange(TrackerParameter p)
        {
            switch (p)
            {
                case TrackerParameter.Volume: return new Vector2(0, 2);
                case TrackerParameter.Pan: return new Vector2(-1, 1);
                case TrackerParameter.FineTune: return new Vector2(-1200, 1200);
                case TrackerParameter.FilterCutoff: return new Vector2(20, 20000);
                case TrackerParameter.FilterResonance: return new Vector2(.1f, 10);
                case TrackerParameter.PulseWidth: return new Vector2(.01f, .99f);
                case TrackerParameter.WaveBRatio: return new Vector2(0, 16);
                case TrackerParameter.PMDepth: return new Vector2(0, 8);
                case TrackerParameter.UnisonDetune: case TrackerParameter.VibratoDepth: return new Vector2(0, 100);
                case TrackerParameter.VibratoRate: return new Vector2(0, 12);
                case TrackerParameter.Attack: case TrackerParameter.Decay: case TrackerParameter.Release: case TrackerParameter.GlideSeconds: case TrackerParameter.VibratoFadeIn: return new Vector2(0, 2);
                case TrackerParameter.FMFeedback: return new Vector2(0, 8);
            }
            if (p >= TrackerParameter.Op0Ratio && !TrackerParameters.IsOperatorFeedback(p))
            {
                int n = ((int)p - (int)TrackerParameter.Op0Ratio) % 7;
                return n == 0 ? new Vector2(0, 16) : n == 1 ? new Vector2(0, 2000) : n == 2 ? new Vector2(0, 2) : n == 5 ? new Vector2(0, 1) : new Vector2(0, 2);
            }
            return new Vector2(0, 1);
        }

        /// <summary>Every setting in the song that can be given a command id.</summary>
        List<CommandTarget> CommandTargets()
        {
            var list = new List<CommandTarget> { SongTempoTarget() };
            foreach (var t in Data.tracks.Where(t => t != null))
            {
                foreach (var parameter in ZTrackerCommandRegistry.TrackParameters) list.Add(TrackTarget(t, parameter));
                foreach (var node in Records(t.devices?.nodes))
                {
                    var desc = ZoundEffectDescriptors.Get(node.type); if (desc == null || string.IsNullOrEmpty(node.uid)) continue;
                    for (int p = 0; p < desc.parameters.Length; p++) { var target = EffectTarget(t, node, p); if (target != null) list.Add(target); }
                }
            }
            foreach (var asset in Data.instruments.Where(i => i != null && i.schemaVersion == 1 && i.model != null).Distinct())
            {
                foreach (TrackerParameter p in Enum.GetValues(typeof(TrackerParameter))) { if (p == TrackerParameter.Count) continue; var target = InstrumentTarget(asset, p); if (target != null) list.Add(target); }
                for (int m = 0; m < Math.Min(8, asset.model.macros?.Length ?? 0); m++) if (asset.model.macros[m] != null) list.Add(MacroTarget(asset, m));
            }
            return list;
        }

        CommandTarget TargetOf(DynamicParameterData p) => p == null ? null : CommandTargets().FirstOrDefault(t => t.scope == p.scope && ZTrackerCommandRegistry.SameTarget(t.target, p.target));

        static string Hex(float u) => Mathf.RoundToInt(Mathf.Clamp01(u) * 255).ToString("X2");

        // ───────────── right-click on a setting ─────────────

        /// <summary>Adds "Command id…" (or rename/remove) to the control's right-click menu. Returns the control.</summary>
        T CommandMenu<T>(T control, Func<CommandTarget> target) where T : VisualElement
        {
            if (control == null) return null;
            // The ZuiValueControl inside an instrument parameter's host owns the right-click menu.
            VisualElement owner = control is ZuiValueControl || control is ZuiMicroSlider ? control : (VisualElement)control.Q<ZuiValueControl>() ?? control.Q<ZuiMicroSlider>() ?? (VisualElement)control;
            ZuiContextExtras.Add(owner, menu => { var t = target(); if (t != null) AddCommandItems(menu, owner, t); });
            return control;
        }

        void AddCommandItems(ZuiMenu menu, VisualElement anchor, CommandTarget t)
        {
            if (Data == null) return;
            var existing = ZTrackerCommandRegistry.FindByTarget(Data, t.scope, t.target);
            if (existing == null)
                menu.Item("Command id…", "Give this setting a two-character command id and a name, so a row in a track's FX column can set it.", () => ShowCommandEditor(anchor, t, null));
            else
            {
                menu.Item("Command " + existing.id + "…", "Rename command " + existing.id + " (" + existing.name + "); every pattern cell using it follows.", () => ShowCommandEditor(anchor, t, existing));
                menu.Item("Remove command " + existing.id, "Stop this setting from having a command id. Cells that use " + existing.id + " stay in the pattern and are flagged until the id is given again.", () => RemoveCommand(existing));
            }
        }

        // ───────────── id + name editor ─────────────

        internal ZuiPopover commandEditor;

        /// <summary>The id and the name, typed together and checked as they are typed. Creates or renames in one Undo step.</summary>
        internal void ShowCommandEditor(VisualElement anchor, CommandTarget t, DynamicParameterData existing)
        {
            commandEditor?.Close();
            commandEditor = Z.Popover(anchor, panel =>
            {
                panel.name = "command-editor";
                var title = Z.Text(t.label, tooltip: (existing == null ? "Give this setting a command id. " : "Rename this command. ") + "00 sets " + Format(t.min, t) + " and FF sets " + Format(t.max, t) + (existing != null ? "; the range is changed in Song › Commands." : "; change the range later in Song › Commands."));
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                panel.Add(title);
                TextField id = null, name = null;
                Button confirm = null;
                var status = Z.Text("", tooltip: "Why the id or name cannot be used yet.");
                status.name = "command-editor-status";
                status.style.height = 16; status.style.whiteSpace = WhiteSpace.NoWrap; status.style.overflow = Overflow.Hidden; status.style.textOverflow = TextOverflow.Ellipsis; status.style.maxWidth = 300;
                string Problem() => ZTrackerCommandRegistry.ValidateId(Data, id.value, existing?.uid, out _) ?? ZTrackerCommandRegistry.ValidateName(name.value);
                void Refresh()
                {
                    string problem = Problem();
                    status.text = problem == null ? "" : "⚠ " + problem;
                    status.tooltip = problem ?? "The id and name can be used.";
                    confirm.SetEnabled(problem == null);
                }
                void Submit()
                {
                    if (Problem() != null) { Refresh(); return; }
                    string typedId = id.value, typedName = name.value;
                    commandEditor?.Close(); commandEditor = null;
                    if (existing == null) DefineCommand(t, typedId, typedName); else RenameCommand(existing, typedId, typedName);
                }
                id = Named(Z.TextInput(existing?.id ?? "", "Two characters, letters A–Z or digits 0–9 (typed in either case). Ids starting with 0 or Z belong to built-in commands.", _ => Refresh(), 44), "command-editor-id");
                id.maxLength = 2;
                name = Named(Z.TextInput(existing?.name ?? DefaultCommandName(t), "A short name that says what the command does (at most " + ZTrackerCommandRegistry.MaxNameLength + " characters).", _ => Refresh(), 170), "command-editor-name");
                confirm = Named(Z.CompactButton(existing == null ? "Create" : "Rename", existing == null ? "Give the setting this id and name." : "Change the id and name; pattern cells using the old id follow.", Submit), "command-editor-confirm");
                foreach (var field in new[] { id, name }) field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Submit(); e.StopPropagation(); } }, TrickleDown.TrickleDown);
                panel.Add(Flow(id, name, confirm));
                panel.Add(status);
                Refresh();
                panel.schedule.Execute(() => id.Focus());
            }, new ZuiPopover.Options { minWidth = 280 });
        }

        static string DefaultCommandName(CommandTarget t)
        {
            string last = t.label.Substring(t.label.LastIndexOf('/') + 1).Trim();
            return last.Length > ZTrackerCommandRegistry.MaxNameLength ? last.Substring(0, ZTrackerCommandRegistry.MaxNameLength) : last;
        }

        static string Format(float v, CommandTarget t) => v.ToString(t.decimals == 0 ? "0" : "0.##") + (string.IsNullOrEmpty(t.units) || t.units == "normalized" ? "" : " " + t.units);

        internal void DefineCommand(CommandTarget t, string id, string name)
        {
            string error = null;
            SongEdit("give a setting a command id", () =>
            {
                // A track effect made in this session may not have written its identity yet; give it the one the editor shows.
                if (t.scope == DynamicScope.Effect) { var track = Data.tracks.Find(x => x.id == t.target.trackId); var node = Records(track?.devices?.nodes).FirstOrDefault(n => n.uid == t.target.deviceId); if (node == null) throw new InvalidOperationException("The effect is gone"); }
                if (ZTrackerCommandRegistry.Define(Data, t.scope, t.target, id, name, t.min, t.max, out error) == null) throw new InvalidOperationException(error);
            }, true);
        }

        internal void RenameCommand(DynamicParameterData p, string id, string name)
        {
            string uid = p.uid;
            SongEdit("rename command", () => { var live = ZTrackerCommandRegistry.FindByUid(Data, uid); string error = ZTrackerCommandRegistry.Rename(Data, live, id, name); if (error != null) throw new InvalidOperationException(error); }, true);
        }

        internal void RemoveCommand(DynamicParameterData p)
        {
            string uid = p.uid;
            SongEdit("remove command", () => ZTrackerCommandRegistry.Remove(Data, ZTrackerCommandRegistry.FindByUid(Data, uid)), true);
        }

        // ───────────── command colours (view only) ─────────────

        IVisualElementScheduledItem colorCommit;
        string colorCommitUid; Color colorCommitValue;

        /// The colour picker reports every drag step: show each at once on the chip, record one Undo step once it settles.
        void PreviewCommandColor(string uid, Label chip, Color c)
        {
            c.a = 1;
            CommandColors.Paint(chip, new CommandLook(true, true, true, CommandKind.None, c, null));
            if (colorCommitUid != null && colorCommitUid != uid) CommitCommandColor();
            colorCommitUid = uid; colorCommitValue = c;
            if (colorCommit == null) colorCommit = rootVisualElement.schedule.Execute(CommitCommandColor);
            colorCommit.ExecuteLater(250);
        }

        void CommitCommandColor()
        {
            colorCommit?.Pause(); colorCommit = null;
            string uid = colorCommitUid; colorCommitUid = null;
            if (uid != null) SetCommandColor(uid, colorCommitValue, rebuild: false);
        }

        /// Sets a command's colour (clear = automatic) as one Undo step. Colours are view-only, so the playing song is not touched.
        internal void SetCommandColor(string uid, Color c, bool rebuild = true)
        {
            var p = ZTrackerCommandRegistry.FindByUid(Data, uid);
            if (p == null || p.color == c) return;
            Change(song, "command colour", () => ZTrackerCommandRegistry.FindByUid(Data, uid).color = c, rebuild, live: false);
        }

        // ───────────── Song › Commands ─────────────

        void BuildCommandList(VisualElement root)
        {
            var box = Z.BoxKeyed("Commands", "Settings given a two-character command id. Type the id in a track's FX column to set the setting from the pattern: 00 is the left end of the range, FF the right end. Right-click a slider (mixer, effect, instrument, tempo) to give it an id.", "tracker.song.commands");
            Button add = null;
            add = Named(Button("Add…", "Pick a setting of this song to give a command id.", () =>
            {
                var menu = Z.Menu(add).Search("Find a setting…");
                foreach (var t in CommandTargets())
                {
                    var target = t;
                    var existing = ZTrackerCommandRegistry.FindByTarget(Data, t.scope, t.target);
                    menu.Item(t.label + (existing != null ? "  (" + existing.id + ")" : ""), existing != null ? "Already command " + existing.id + " (" + existing.name + ")." : "Give this setting a command id.", () => ShowCommandEditor(add, target, null), enabled: existing == null);
                }
                menu.Show();
            }), "command-add");
            box.Add(Flow(add));
            var definitions = Records(Data.dynamicParameters).OrderBy(p => p.id, StringComparer.Ordinal).ToList();
            if (Data.commandModelVersion > ZTrackerCommandRegistry.CurrentVersion) { box.Add(Z.Text("⚠ Made by a newer ZTracker", tooltip: "This song's commands were saved by a newer ZTracker; they are shown read-only.")); add.SetEnabled(false); }
            if (definitions.Count == 0) box.Add(Named(Z.Text("None declared", tooltip: "No setting has a command id yet. Use Add… or right-click a slider."), "commands-empty"));
            var targets = CommandTargets();
            foreach (var p in definitions)
            {
                var definition = p;
                var t = targets.FirstOrDefault(x => x.scope == p.scope && ZTrackerCommandRegistry.SameTarget(x.target, p.target));
                int uses = ZTrackerCommandRegistry.Uses(Data, p.id);
                var chip = Z.Text(p.id, tooltip: p.id + " — " + p.scope + " command, used in " + uses + " pattern cell" + (uses == 1 ? "" : "s") + ". Drawn as dark text on its colour wherever it appears; the colour stays with it through a rename. Double-click for an automatic colour no other command uses.");
                chip.AddToClassList("tracker-command-chip");
                chip.name = "command-chip-" + p.id;
                chip.style.width = 28; chip.style.unityTextAlign = TextAnchor.MiddleCenter; chip.style.unityFontStyleAndWeight = FontStyle.Bold;
                CommandColors.Paint(chip, new CommandLook(true, true, true, CommandKind.None, ZTrackerCommandRegistry.ColorOf(p), p));
                chip.style.borderTopLeftRadius = chip.style.borderTopRightRadius = chip.style.borderBottomLeftRadius = chip.style.borderBottomRightRadius = 3;
                chip.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0 && e.clickCount == 2) SetCommandColor(definition.uid, ZTrackerCommandRegistry.PickColor(Data, definition.uid)); });
                var swatch = Named(Z.Color(ZTrackerCommandRegistry.ColorOf(p), "Colour of " + p.id + " in the pattern, bound columns and suggestions (view only: it never changes what plays). Double-click the id chip for an automatic colour.", c => PreviewCommandColor(definition.uid, chip, c), showAlpha: false), "command-color-" + p.id);
                swatch.AddToClassList("tracker-command-color");
                var nameLabel = Z.Text(p.name, tooltip: "Friendly name of " + p.id + ".");
                nameLabel.style.width = 140; nameLabel.style.overflow = Overflow.Hidden; nameLabel.style.textOverflow = TextOverflow.Ellipsis; nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
                string where = t?.label ?? "⚠ Target removed";
                var targetLabel = Z.Text(where, tooltip: t != null ? "What " + p.id + " sets." : "The setting this command pointed at no longer exists; " + p.id + " does nothing until it is removed.");
                targetLabel.style.width = 210; targetLabel.style.overflow = Overflow.Hidden; targetLabel.style.textOverflow = TextOverflow.Ellipsis; targetLabel.style.whiteSpace = WhiteSpace.NoWrap;
                var row = Flow(chip, swatch, nameLabel, targetLabel);
                if (t != null)
                {
                    float defaultLow = t.min, defaultHigh = t.max;
                    var range = Z.MicroMinMax("00–FF", p.min, p.max, t.lo, t.hi, "What 00 and FF set, in " + (string.IsNullOrEmpty(t.units) ? "the setting's units" : t.units) + ". Double-click a handle to reset it.", (a, b) => SongEdit("command range", () => { var live = ZTrackerCommandRegistry.FindByUid(Data, definition.uid); if (live != null) { live.min = a; live.max = b; } }), 190, lowDefault: defaultLow, highDefault: defaultHigh, decimals: t.decimals);
                    row.Add(Named(range, "command-range-" + p.id));
                }
                VisualElement rename = null;
                rename = Named(Z.IconButton("pencil", "Rename " + p.id + " (id and name together); pattern cells using it follow.", () => ShowCommandEditor(rename, t ?? new CommandTarget { label = where, scope = p.scope, target = p.target, min = p.min, max = p.max }, definition)), "command-rename-" + p.id);
                row.Add(rename);
                row.Add(Named(Z.IconButton("trash", "Remove " + p.id + ". Cells that use it stay in the pattern and are flagged until the id is given again.", () => RemoveCommand(definition)), "command-remove-" + p.id));
                row.name = "command-row-" + p.id;
                box.Add(row);
            }
            root.Add(box);
        }
    }
}
