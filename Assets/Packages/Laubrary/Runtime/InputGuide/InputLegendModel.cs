using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using ZuiRuntime;

namespace Laubrary.InputGuide
{
    public sealed class InputLegendModel
    {
        public GamepadMap Gamepad;
        public KeyboardMap Keyboard;
        public readonly List<string> Overflow = new List<string>();
    }

    public static class InputLegendBuilder
    {
        public static InputLegendModel Build(InputGuideRuntime guide, InputSchemeKind scheme)
        {
            var result = new InputLegendModel();
            if (guide == null || guide.Catalog == null || guide.Actions == null) return result;

            var keys = new List<KeyboardKeyPrompt>();
            var face = new FaceButtonPrompt();
            var dpad = new DpadPrompt();
            var left = new ShoulderPrompt("LB", null, "LT", null);
            var right = new ShoulderPrompt("RB", null, "RT", null);
            string leftStick = null, rightStick = null, select = null, start = null;
            string mouseLeft = null, mouseMiddle = null, mouseRight = null, mouseMove = null, mouseScroll = null;

            var descriptions = guide.Catalog.ActionDescriptions;
            for (int d = 0; d < descriptions.Count; d++)
            {
                var metadata = descriptions[d];
                if (metadata == null || !metadata.showInGuide || metadata.action == null) continue;
                var action = guide.Resolve(metadata.action);
                if (action == null) continue;
                string caption = metadata.DisplayName;

                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var binding = action.bindings[i];
                    if (binding.isComposite || !guide.BindingBelongsToScheme(action, i, scheme)) continue;
                    string path = binding.effectivePath;
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    if (scheme == InputSchemeKind.Gamepad)
                    {
                        if (!MapGamepad(path, caption, ref face, ref dpad, ref left, ref right,
                                ref leftStick, ref rightStick, ref select, ref start))
                            AddUnique(result.Overflow, action.GetBindingDisplayString(i) + " — " + caption);
                    }
                    else if (path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase))
                    {
                        string key = path.Substring(path.LastIndexOf('/') + 1);
                        AddKey(keys, key, caption);
                    }
                    else if (path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Contains(path, "leftButton")) Append(ref mouseLeft, caption);
                        else if (Contains(path, "middleButton")) Append(ref mouseMiddle, caption);
                        else if (Contains(path, "rightButton")) Append(ref mouseRight, caption);
                        else if (Contains(path, "scroll")) Append(ref mouseScroll, caption);
                        else if (Contains(path, "position") || Contains(path, "delta")) Append(ref mouseMove, caption);
                        else AddUnique(result.Overflow, action.GetBindingDisplayString(i) + " — " + caption);
                    }
                    else AddUnique(result.Overflow, action.GetBindingDisplayString(i) + " — " + caption);
                }
            }

            result.Gamepad = new GamepadMap
            {
                Face = face,
                Dpad = dpad,
                Left = left,
                Right = right,
                LeftStick = leftStick,
                RightStick = rightStick,
                Select = select,
                Start = start
            };
            result.Keyboard = new KeyboardMap
            {
                Keys = keys.ToArray(),
                MouseLeft = mouseLeft,
                MouseMiddle = mouseMiddle,
                MouseRight = mouseRight,
                MouseMove = mouseMove,
                MouseScroll = mouseScroll
            };
            return result;
        }

        static bool MapGamepad(string path, string caption, ref FaceButtonPrompt face, ref DpadPrompt dpad,
            ref ShoulderPrompt left, ref ShoulderPrompt right, ref string leftStick, ref string rightStick,
            ref string select, ref string start)
        {
            if (Contains(path, "buttonSouth")) Append(ref face.A, caption);
            else if (Contains(path, "buttonEast")) Append(ref face.B, caption);
            else if (Contains(path, "buttonWest")) Append(ref face.X, caption);
            else if (Contains(path, "buttonNorth")) Append(ref face.Y, caption);
            else if (Contains(path, "dpad/up")) Append(ref dpad.Up, caption);
            else if (Contains(path, "dpad/down")) Append(ref dpad.Down, caption);
            else if (Contains(path, "dpad/left")) Append(ref dpad.Left, caption);
            else if (Contains(path, "dpad/right")) Append(ref dpad.Right, caption);
            else if (Contains(path, "leftShoulder")) Append(ref left.BumperCaption, caption);
            else if (Contains(path, "rightShoulder")) Append(ref right.BumperCaption, caption);
            else if (Contains(path, "leftTrigger")) Append(ref left.TriggerCaption, caption);
            else if (Contains(path, "rightTrigger")) Append(ref right.TriggerCaption, caption);
            else if (Contains(path, "leftStickPress")) Append(ref leftStick, "Press: " + caption);
            else if (Contains(path, "rightStickPress")) Append(ref rightStick, "Press: " + caption);
            else if (Contains(path, "leftStick")) Append(ref leftStick, caption);
            else if (Contains(path, "rightStick")) Append(ref rightStick, caption);
            else if (Contains(path, "select") || Contains(path, "back")) Append(ref select, caption);
            else if (Contains(path, "start")) Append(ref start, caption);
            else return false;
            return true;
        }

        static void AddKey(List<KeyboardKeyPrompt> keys, string key, string caption)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (!string.Equals(keys[i].Key, key, StringComparison.OrdinalIgnoreCase)) continue;
                var item = keys[i];
                item.Caption = Join(item.Caption, caption);
                keys[i] = item;
                return;
            }
            keys.Add(new KeyboardKeyPrompt(key, caption));
        }

        static bool Contains(string source, string value) => source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        static void Append(ref string target, string value) => target = Join(target, value);
        static string Join(string current, string value) => string.IsNullOrWhiteSpace(current) ? value : current.Contains(value) ? current : current + " / " + value;
        static void AddUnique(List<string> list, string value) { if (!list.Contains(value)) list.Add(value); }
    }
}
