// ChunkWindow.CuesCard — the card for Cues (a coordinator; the recipe's non-visual moments).
//
// One row per marker: when it fires, whether it raises a named code hook or plays a Zound, and that event's
// own identity — a text field for Code (the name is DECLARED here, so typing it is correct) or the shared
// Zound picker hook for Zound (a Zound is a REFERENCE, so it is always picked, never typed — the picker
// button says "unavailable" rather than degrading to a text field when no audio tool is registered, per
// ui-layout-rules: "degrading to a text field when the option list is empty is the failure mode"). Time is a
// plain scrub field, not a slider: its only honest ceiling is the clock's own length, which a cue is one of
// the things that decides — same reasoning as the shell's own DelayRow.
//
// Markers feed the Timing ruler for free: Dial already calls SyncTiming() on every edit (ChunkWindow.cs), and
// FillLanes (ChunkWindow.Timing.cs) already reads cap.cues off this same list — nothing here has to push a
// marker onto the ruler by hand.
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildCuesCard(VisualElement body, ChunkSpec c, Cues cap)
        {
            string id = cap.id;
            cap.cues ??= new System.Collections.Generic.List<ChunkCue>();

            var list = new VisualElement();
            for (int i = 0; i < cap.cues.Count; i++)
            {
                var cue = cap.cues[i];
                if (cue == null) continue;
                int index = i;

                var row = Z.Row(
                    Z.Field("At", "Seconds from the start of the recipe at which this fires.",
                        Z.Float(cue.time, "Seconds from the start of the recipe at which this fires.",
                                v => Dial("Edit Cue Time", () => cue.time = Mathf.Max(0f, v)), 60f)),
                    Z.HSpace(6f),
                    Z.Segmented((int)cue.kind, new[] { "Code", "Zound" },
                        "Code raises a named hook your game subscribes to (ChunkTimelineEvents.CodeEvent). " +
                        "Zound plays a sound from the Zounds library.",
                        v => DialAndRebuildCard(id, "Set Cue Kind", () => cue.kind = (ChunkEventKind)v)).W(110f),
                    Z.HSpace(6f),
                    cue.kind == ChunkEventKind.Zound ? BuildZoundSlot(cue) : BuildCodeSlot(cue),
                    Z.Flexible(),
                    SmallButton("×", "Remove this cue.", true,
                        () => DialAndRebuildCard(id, "Remove Cue", () => cap.cues.RemoveAt(index))));
                row.style.flexWrap = Wrap.NoWrap;
                list.Add(row);
            }
            body.Add(list);

            var add = Z.Button("Add cue",
                "Add a moment on the recipe's clock that raises a named code hook or plays a Zound.",
                () => DialAndRebuildCard(id, "Add Cue", () => cap.cues.Add(new ChunkCue())));
            add.style.width = 90f;
            add.style.alignSelf = Align.FlexStart;
            body.Add(add);
        }

        VisualElement BuildCodeSlot(ChunkCue cue)
        {
            var f = Z.TextInput(cue.codeName,
                "The hook name raised for game code. This is where the name is DECLARED, so it is typed here " +
                "and picked everywhere else.",
                v => Dial("Set Cue Name", () => cue.codeName = v), 150f);
            f.isDelayed = true;   // commits on Enter/blur, not per keystroke
            return f;
        }

        VisualElement BuildZoundSlot(ChunkCue cue)
        {
            if (!ChunkZoundPickerHook.Available)
            {
                var unavailable = Z.Button("Zound picker unavailable",
                    "No audio tool is registered in this project, so there is nothing to pick from. With " +
                    "Zounds present the ChunksZounds bridge registers the picker automatically and this " +
                    "becomes a browser.", null);
                unavailable.style.width = 190f;
                unavailable.SetEnabled(false);
                return unavailable;
            }

            string Current() => string.IsNullOrEmpty(cue.zoundName) ? "(none)" : cue.zoundName;
            var button = Z.Button(Current(), "Click to pick a Zound. Right-click to hear the current one.", null);
            button.style.width = 150f;
            button.clicked += () =>
            {
                // Screen position derived from the window's own rect rather than GUIUtility.GUIToScreenPoint:
                // this runs from a UI Toolkit callback, where there is no active IMGUI context for that
                // conversion to be measured against. Mirrors the recovered ChunkWindow.Timeline.cs pattern.
                var wb = button.worldBound;
                var screen = new Vector2(position.x + wb.x, position.y + wb.yMax);
                ChunkZoundPickerHook.Show(screen, picked =>
                {
                    Dial("Pick Zound", () => cue.zoundName = picked);
                    button.text = Current();
                    ChunkZoundPickerHook.Preview?.Invoke(picked);   // hear what you just chose, immediately
                });
            };

            // Right-click auditions it — a sound field you cannot hear from is a name you have to trust.
            button.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1 || string.IsNullOrEmpty(cue.zoundName)) return;
                ChunkZoundPickerHook.Preview?.Invoke(cue.zoundName);
            });
            return button;
        }
    }
}
