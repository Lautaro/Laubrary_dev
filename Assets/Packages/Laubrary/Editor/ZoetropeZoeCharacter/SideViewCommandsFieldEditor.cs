using System.Collections.Generic;
using System.Linq;
using Laubrary.Zoetrope;
using Laubrary.Zoetrope.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Laubrary.ZoetropeZoeCharacter.Editor
{
    /// <summary>
    /// Draws a side-view controller's command list in the Zoe window: one row per command — the button
    /// (picked from the controls asset's Player map, or the stick alone), a direction pad relative to the
    /// facing, the action (picked from this character's declared actions), the stick hold time and whether
    /// the action turns the character when it finishes.
    /// </summary>
    sealed class SideViewCommandsFieldEditor : IZoeFieldEditor
    {
        const string StickOnly = "(stick only)";

        // Pad layout as the player sees it with the character facing right: forward is right.
        static readonly StickDirection[,] Pad =
        {
            { StickDirection.BackUp, StickDirection.Up, StickDirection.ForwardUp },
            { StickDirection.Back, StickDirection.Neutral, StickDirection.Forward },
            { StickDirection.BackDown, StickDirection.Down, StickDirection.ForwardDown },
        };
        static readonly string[,] PadGlyph = { { "↖", "↑", "↗" }, { "←", "•", "→" }, { "↙", "↓", "↘" } };

        public bool TryBuild(VisualElement host, SerializedProperty prop, object owner, Zoe zoe, ZoeFieldEditContext ctx)
        {
            if (!(owner is SideViewPlayerControllerSpec spec)) return false;
            if (prop.name == nameof(SideViewPlayerControllerSpec.directionThreshold))
            {
                // A bounded value: a slider, like every other ranged setting.
                string path = prop.propertyPath;
                host.Add(Z.MicroSlider("Direction threshold", prop.floatValue, 0.2f, 0.9f,
                    "How far the stick must lean before it counts as a direction for a command.",
                    v => ctx.Commit(path, p => p.floatValue = v), 200f, decimals: 2));
                return true;
            }
            if (prop.name != nameof(SideViewPlayerControllerSpec.commands)) return false;

            string listPath = prop.propertyPath;
            var buttons = new List<string> { StickOnly };
            var map = spec.controls != null ? spec.controls.FindActionMap("Player") : null;
            if (map != null)
                foreach (var a in map.actions)
                    if (a.type == InputActionType.Button && a.name != "Fire") buttons.Add(a.name);
            var actions = zoe.EventIds != null ? zoe.EventIds.ToList() : new List<string>();

            const string listTip = "Buttons and stick directions, each playing one of this character's declared " +
                "actions. Directions are relative to the facing: → is toward where the character faces.";
            var add = Z.Button("+ Add command", "Add a command row.", () =>
            {
                ctx.Commit(listPath, p =>
                {
                    p.arraySize++;
                    var e = p.GetArrayElementAtIndex(p.arraySize - 1);
                    e.FindPropertyRelative("button").stringValue = buttons.Count > 1 ? buttons[1] : "";
                    e.FindPropertyRelative("direction").enumValueIndex = (int)StickDirection.Any;
                    e.FindPropertyRelative("eventId").stringValue = "";
                    e.FindPropertyRelative("holdSeconds").floatValue = 0.12f;
                    e.FindPropertyRelative("turnWhenDone").boolValue = false;
                });
                ctx.Rebuild();
            });
            host.Add(Z.Row(Z.Text($"Commands  ({prop.arraySize})", ZuiText.Subtle, listTip), Z.HSpace(), add, Z.Flexible()));
            if (map == null)
                host.Add(Z.Text("Assign Controls to pick buttons.", ZuiText.Subtle,
                    "The command buttons are the button actions of the controls asset's Player map."));

            for (int i = 0; i < prop.arraySize; i++)
                host.Add(BuildRow(prop.GetArrayElementAtIndex(i), listPath, i, buttons, actions, ctx));
            return true;
        }

        VisualElement BuildRow(SerializedProperty e, string listPath, int index, List<string> buttons, List<string> actions,
                               ZoeFieldEditContext ctx)
        {
            var buttonProp = e.FindPropertyRelative("button");
            var dirProp = e.FindPropertyRelative("direction");
            var eventProp = e.FindPropertyRelative("eventId");
            var holdProp = e.FindPropertyRelative("holdSeconds");
            var turnProp = e.FindPropertyRelative("turnWhenDone");
            string buttonPath = buttonProp.propertyPath, dirPath = dirProp.propertyPath, eventPath = eventProp.propertyPath;
            string holdPath = holdProp.propertyPath, turnPath = turnProp.propertyPath;
            bool stickOnly = string.IsNullOrEmpty(buttonProp.stringValue);

            var row = Z.Row();
            row.AddToClassList("zui-row--wrap");

            // Button: the controls' button actions, or the stick alone.
            var buttonChoices = new List<string>(buttons);
            string currentButton = stickOnly ? StickOnly : buttonProp.stringValue;
            if (!buttonChoices.Contains(currentButton)) buttonChoices.Add(currentButton);   // keep a stale name visible
            const string buttonTip = "The button that triggers this command, from the controls asset's Player map. " +
                "(stick only) = moving the stick into the direction triggers it.";
            row.Add(Z.Dropdown(buttonChoices.IndexOf(currentButton), buttonChoices, buttonTip, k =>
            {
                string picked = buttonChoices[Mathf.Clamp(k, 0, buttonChoices.Count - 1)];
                ctx.Commit(buttonPath, p => p.stringValue = picked == StickOnly ? "" : picked);
                ctx.Rebuild();   // hold time shows only for stick-only rows
            }, 110f));
            row.Add(Z.HSpace());

            // Direction: a 3×3 pad (forward = right) plus Any.
            var dir = (StickDirection)dirProp.enumValueIndex;
            var pad = new VisualElement();
            pad.style.flexDirection = FlexDirection.Column;
            for (int r = 0; r < 3; r++)
            {
                var padRow = new VisualElement();
                padRow.style.flexDirection = FlexDirection.Row;
                for (int c = 0; c < 3; c++)
                {
                    var d = Pad[r, c];
                    string tip = d == StickDirection.Neutral ? "Stick at rest." : $"Stick {Nice(d)} (relative to the facing).";
                    var cell = Z.Toggle(PadGlyph[r, c], tip, dir == d, _ => { ctx.Commit(dirPath, p => p.enumValueIndex = (int)d); ctx.Rebuild(); });
                    cell.style.width = 18f; cell.style.height = 15f;
                    cell.style.fontSize = 10f;
                    cell.style.paddingLeft = cell.style.paddingRight = 0f;
                    cell.style.paddingTop = cell.style.paddingBottom = 0f;
                    cell.style.marginLeft = cell.style.marginRight = 0.5f;
                    cell.style.marginTop = cell.style.marginBottom = 0.5f;
                    padRow.Add(cell);
                }
                pad.Add(padRow);
            }
            row.Add(pad);
            var any = Z.Toggle("Any", "Any stick direction (used when no command matches the exact direction).",
                dir == StickDirection.Any, _ => { ctx.Commit(dirPath, p => p.enumValueIndex = (int)StickDirection.Any); ctx.Rebuild(); });
            row.Add(any);
            row.Add(Z.HSpace());

            // Action: picked from the character's declared actions.
            const string actionTip = "The declared action this command plays (from this character's event list).";
            string currentAction = eventProp.stringValue ?? "";
            var actionChoices = actions.Count > 0 ? new List<string>(actions) : new List<string> { "None declared" };
            if (!string.IsNullOrEmpty(currentAction) && !actionChoices.Contains(currentAction)) actionChoices.Add(currentAction);
            if (string.IsNullOrEmpty(currentAction)) actionChoices.Insert(0, "(pick)");
            var actionField = Z.Dropdown(Mathf.Max(0, actionChoices.IndexOf(string.IsNullOrEmpty(currentAction) ? "(pick)" : currentAction)),
                actionChoices, actionTip, k =>
                {
                    string picked = actionChoices[Mathf.Clamp(k, 0, actionChoices.Count - 1)];
                    if (picked == "(pick)" || picked == "None declared") return;
                    ctx.Commit(eventPath, p => p.stringValue = picked);
                }, 140f);
            // No field label: a label here is padded to line up with the controller's own field labels above,
            // which opens a wide gap mid-row. The arrow says "plays" and the tooltip carries the rest.
            row.Add(Z.Text("▶", ZuiText.Subtle, actionTip));
            row.Add(actionField);
            row.Add(Z.HSpace());

            // Hold time: only meaningful for a stick-only command; the slot stays reserved either way.
            const string holdTip = "Seconds the stick must stay in this direction before it fires. Pressing a " +
                "button in that time cancels it, so a button command on the same direction can win.";
            var hold = Z.Field("Hold s", holdTip, Z.Float(holdProp.floatValue, holdTip,
                v => ctx.Commit(holdPath, p => p.floatValue = Mathf.Max(0f, v)), 44f));
            hold.style.visibility = stickOnly ? Visibility.Visible : Visibility.Hidden;
            row.Add(hold);
            row.Add(Z.HSpace());

            row.Add(Z.Toggle("Turn when done",
                "Face the other way when this action finishes (not when something interrupts it) — for a " +
                "turnaround whose art ends facing the other way.",
                turnProp.boolValue, v => ctx.Commit(turnPath, p => p.boolValue = v)));
            row.Add(Z.Flexible());

            row.Add(Z.Button("×", "Remove this command (undoable).", () =>
            {
                ctx.Commit(listPath, p => p.DeleteArrayElementAtIndex(index));
                ctx.Rebuild();
            }).W(22f));
            return row;
        }

        static string Nice(StickDirection d) => d switch
        {
            StickDirection.ForwardUp => "forward-up",
            StickDirection.BackUp => "back-up",
            StickDirection.BackDown => "back-down",
            StickDirection.ForwardDown => "forward-down",
            _ => d.ToString().ToLowerInvariant(),
        };
    }
}
