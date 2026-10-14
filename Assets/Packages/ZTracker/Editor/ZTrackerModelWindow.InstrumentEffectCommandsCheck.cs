using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.Audio;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>
        /// T-0606 in the real attached window, on a disposable in-memory song: an instrument chain effect's setting gets a
        /// command id by right-click (one definition, one copy per track in the engine), the sampler's Volume/Pan and a
        /// preset's override value offer the same entry, and the Alt+B bind menus draw every command in its colours.
        /// </summary>
        public static string CheckInstrumentEffectCommands()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            var window = CreateInstance<ZTrackerModelWindow>(); var song = CreateInstance<ZTrackerSong>();
            var clip = AudioClip.Create("instrument-effect-commands", 4800, 1, 48000, false); var raw = new float[4800]; for (int i = 0; i < raw.Length; i++) raw[i] = .1f; clip.SetData(raw, 0);
            var instrument = TrackerEngineCheck.FixtureInstrument(clip);
            void Need(bool okay, string why) { if (!okay) throw new InvalidOperationException(why); }
            void Check(string title, Action test) { try { test(); passed++; report.AppendLine("PASS " + title); } catch (Exception e) { failed++; report.AppendLine("FAIL " + title + ": " + e.Message); } }
            VisualElement PanelRoot() => window.rootVisualElement.panel.visualTree;
            List<string> MenuLabels() => PanelRoot().Query<VisualElement>(className: "zui-menu__item").ToList().SelectMany(e => e.Query<Label>().ToList()).Select(l => l.text).ToList();
            void CloseMenus() { foreach (var m in PanelRoot().Query<VisualElement>(className: "zui-menu").ToList()) m.parent?.parent?.RemoveFromHierarchy(); }
            void Define(string id, string name) { var root = PanelRoot(); root.Q<TextField>("command-editor-id").value = id; root.Q<TextField>("command-editor-name").value = name; ClickButton(root.Q<Button>("command-editor-confirm")); }
            void Show(int pane, int tab) { CloseMenus(); window.pane = pane; window.instrumentTab = tab; window.BuildPane(); LayoutChain(window); }
            try
            {
                instrument.model.name = "Lead";
                var chain = new AudioEffectChainData(); chain.nodes.Add(new AudioEffectNodeData { type = Laubrary.Zounds.ZoundEffectType.Gain, uid = "inst-gain", p = new[] { 1f } });
                instrument.model.fxChains.Add(chain); instrument.model.sampler.samples[0].fxChain = 0;
                song.schemaVersion = 1; song.model = TrackerEngineCheck.FixtureSongNew(instrument, 1, 16);
                song.model.tracks.Insert(1, new TrackData { id = "notes2", name = "Second", visibleNoteColumns = 1, visibleEffectColumns = 1 });
                song.model.patterns[0].tracks.Insert(1, new PatternTrack { trackId = "notes2" });
                song.model.tracks[0].name = "First";
                song.model.patterns[0].tracks[0].WriteLine(new PatternLine { line = 0, notes = new List<NoteCell> { TrackerEngineCheck.Note() } });
                window.song = song; window.instrument = instrument; window.entryInstrument = 0; window.titleContent = new GUIContent("ZTracker instrument effect commands check");
                window.Show(); window.position = new Rect(60, 60, 1250, 820); window.CreateGUI();

                Check("Right-click an instrument chain effect setting gives it a command id (scope: instrument effect)", () =>
                {
                    Show(3, 3);
                    var setting = window.rootVisualElement.Q("instrument-chain-editor")?.Q("chain-param-0-0");
                    Need(setting != null, "the chain setting is missing");
                    RightClick(setting);
                    var labels = MenuLabels();
                    var item = PanelRoot().Query<VisualElement>(className: "zui-menu__item").ToList().FirstOrDefault(e => e.Query<Label>().ToList().Any(l => l.text == "Command id…"));
                    Need(item != null, "no Command id… item; have " + string.Join(" | ", labels));
                    Need(item.tooltip.Contains("only the copy of the track it is typed on"), "the item does not say a command changes only its track's copy: " + item.tooltip);
                    PickMenuItem(window, "Command id…");
                    Need(window.commandEditor != null, "the id editor did not open");
                    Need(PanelRoot().Q<TextField>("command-editor-name").value == "Gain", "name not prefilled from the setting: " + PanelRoot().Q<TextField>("command-editor-name").value);
                    Define("ig", "Lead gain");
                    var p = ZTrackerCommandRegistry.Find(song.model, "IG");
                    Need(p != null && p.scope == DynamicScope.InstrumentEffect && p.target.instrumentId == instrument.model.id && p.target.deviceId == "inst-gain" && p.target.index == 0, "definition not created as expected");
                    Undo.PerformUndo(); Need(ZTrackerCommandRegistry.Find(song.model, "IG") == null, "Undo did not remove the command");
                    Undo.PerformRedo(); Need(ZTrackerCommandRegistry.Find(song.model, "IG") != null, "Redo did not restore the command");
                    Show(3, 3); RightClick(window.rootVisualElement.Q("instrument-chain-editor").Q("chain-param-0-0"));
                    Need(MenuLabels().Contains("Command IG…") && MenuLabels().Contains("Remove command IG"), "a second right-click does not offer rename/remove: " + string.Join(" | ", MenuLabels()));
                });

                Check("The engine compiles one record per track's copy; Song › Commands lists it with its target", () =>
                {
                    using (var prepared = TrackerPreparedSong.Prepare(song.model)) Need(prepared.state.dynamicCount == 2 && !prepared.diagnostics.Any(d => d.StartsWith("DYNAMIC")), "records " + prepared.state.dynamicCount + ", diagnostics " + string.Join("; ", prepared.diagnostics.Where(d => d.StartsWith("DYNAMIC"))));
                    Show(4, 0);
                    var row = window.rootVisualElement.Q("command-row-IG");
                    Need(row != null, "no IG row in Song › Commands");
                    Need(row.Query<Label>().ToList().Any(l => l.text.Contains("Lead") && l.text.Contains("Gain 1")), "the row does not name the instrument effect: " + string.Join(" | ", row.Query<Label>().ToList().Select(l => l.text)));
                });

                Check("Suggestions and the help line name the scope and the per-track copy", () =>
                {
                    Show(0, 0);
                    var s = window.SuggestionsFor(window.fields.First(f => f.kind == FieldKind.EffectId && f.track == 0), 2).FirstOrDefault(x => x.id == "IG");
                    Need(s != null, "IG not suggested on a row where Lead plays");
                    Need(s.scope == "Instrument effect", "scope label " + s.scope);
                    Need(s.tooltip.Contains("this track's copy"), "tooltip does not say it changes this track's copy: " + s.tooltip);
                });

                Check("Sampler Volume and Pan offer the command entry", () =>
                {
                    Show(3, 0);
                    foreach (var name in new[] { "sampler-volume", "sampler-pan" })
                    {
                        CloseMenus();
                        var control = window.rootVisualElement.Q(name)?.Q<Laubrary.Zui.ZuiValueControl>();
                        Need(control != null, name + " missing");
                        RightClick(control);
                        Need(MenuLabels().Contains("Command id…"), name + " right-click has no Command id…: " + string.Join(" | ", MenuLabels()));
                    }
                    CloseMenus();
                    RightClick(window.rootVisualElement.Q("sampler-pan").Q<Laubrary.Zui.ZuiValueControl>()); PickMenuItem(window, "Command id…"); Define("pn", "Lead pan");
                    var p = ZTrackerCommandRegistry.Find(song.model, "PN");
                    Need(p != null && p.scope == DynamicScope.Instrument && p.target.parameter == "pan", "sampler pan command not created");
                });

                Check("A preset's override value offers the instrument parameter's command (one id for Base and presets)", () =>
                {
                    window.CreatePreset(); window.presetIndex = 0;
                    Show(3, 5);
                    var volume = window.rootVisualElement.Q("preset-volume")?.Q<Laubrary.Zui.ZuiValueControl>();
                    Need(volume != null, "preset volume missing");
                    RightClick(volume);
                    Need(MenuLabels().Contains("Command id…"), "preset volume right-click has no Command id…: " + string.Join(" | ", MenuLabels()));
                    PickMenuItem(window, "Command id…"); Define("pv", "Lead volume");
                    var p = ZTrackerCommandRegistry.Find(song.model, "PV");
                    Need(p != null && p.scope == DynamicScope.Instrument && p.target.parameter == "volume", "preset volume command not created as the instrument's volume");
                    CloseMenus(); RightClick(window.rootVisualElement.Q("preset-pan").Q<Laubrary.Zui.ZuiValueControl>());
                    Need(MenuLabels().Contains("Command PN…"), "the preset's pan does not show the instrument's pan command: " + string.Join(" | ", MenuLabels()));
                    Show(3, 0); CloseMenus(); RightClick(window.rootVisualElement.Q("sampler-volume").Q<Laubrary.Zui.ZuiValueControl>());
                    Need(MenuLabels().Contains("Command PV…"), "Base volume does not show the same command: " + string.Join(" | ", MenuLabels()));
                });

                Check("Alt+B menus draw every command in its colours (song commands dark on their colour, built-ins coloured text)", () =>
                {
                    Show(0, 0); CloseMenus();
                    window.ShowBindMenu(0, 0); LayoutChain(window);
                    var chips = PanelRoot().Query<Label>(className: "zui-menu__chip").ToList();
                    Need(chips.Count > 10, "only " + chips.Count + " coloured chips in the FX bind menu");
                    foreach (var id in new[] { "IG", "PN", "PV" })
                    {
                        var chip = chips.FirstOrDefault(c => c.text == id); var def = ZTrackerCommandRegistry.Find(song.model, id);
                        Need(chip != null, id + " has no chip");
                        Need(chip.resolvedStyle.backgroundColor == ZTrackerCommandRegistry.ColorOf(def), id + " chip background " + chip.resolvedStyle.backgroundColor + " is not its colour " + ZTrackerCommandRegistry.ColorOf(def));
                    }
                    var builtin = chips.FirstOrDefault(c => c.text == "0L");
                    Need(builtin != null && builtin.resolvedStyle.color == CommandColors.KindColor(CommandColors.KindOf("0L")), "0L chip is not drawn in the Volume colour");
                    var search = PanelRoot().Query<TextField>(className: "zui-menu__search").ToList().LastOrDefault();
                    search.value = "ig"; using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return, character = '\n' })) { e.target = search; search.SendEvent(e); }
                    Need(song.model.tracks[0].effectColumnViews != null && song.model.tracks[0].effectColumnViews.Any(v => v != null && v.command == ZTrackerCommandRegistry.Find(song.model, "IG").uid), "typing the id and Enter did not bind the column");
                    CloseMenus(); window.SetFxColumns(0, 1);
                    song.model.tracks[0].envelopeColumns.Add(new EnvelopeColumnData { uid = "env-check" }); window.BuildPane(); LayoutChain(window);
                    window.ShowEnvelopeMenu(0, 0); LayoutChain(window);
                    chips = PanelRoot().Query<Label>(className: "zui-menu__chip").ToList();
                    Need(chips.Any(c => c.text == "IG" && c.resolvedStyle.backgroundColor == ZTrackerCommandRegistry.ColorOf(ZTrackerCommandRegistry.Find(song.model, "IG"))), "envelope menu chip for IG not coloured");
                    Need(chips.Any(c => c.text == "0P" && c.resolvedStyle.color == CommandColors.KindColor(CommandColors.KindOf("0P"))), "envelope menu chip for 0P not coloured");
                    CloseMenus();
                });
            }
            finally
            {
                window.commandEditor?.Close(); window.StopPreview(); window.Close(); DestroyImmediate(window);
                Undo.ClearUndo(song); Undo.ClearUndo(instrument); DestroyImmediate(song); DestroyImmediate(instrument); DestroyImmediate(clip);
            }
            report.Insert(0, "TOTAL passed=" + passed + " failed=" + failed + "\n");
            return report.ToString();
        }
    }
}
