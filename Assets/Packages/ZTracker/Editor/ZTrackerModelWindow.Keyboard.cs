using System;
using System.Collections.Generic;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    // The computer keyboard in the Instrument and drum-kit views. It is one more source of keys into the live-note
    // layer, beside the MIDI keyboard: the keys play the edited instrument on the clean preview channel, and Panic,
    // chords over the channel's columns and per-key release work the same for both.
    public sealed partial class ZTrackerModelWindow
    {
        // Held keys and the note each one plays; a key already held is OS auto-repeat, not another strike.
        readonly Dictionary<KeyCode, int> instrumentKeys = new Dictionary<KeyCode, int>();
        ZTrackerInstrument keyboardInstrument;
        Label keyboardStatus;
        [NonSerialized] string keyboardProblem;

        void WireInstrumentKeyboard(VisualElement root)
        {
            root.focusable = true;
            root.UnregisterCallback<PointerDownEvent>(InstrumentPointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(InstrumentPointerDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<FocusInEvent>(InstrumentFocusChanged);
            root.RegisterCallback<FocusInEvent>(InstrumentFocusChanged);
            root.UnregisterCallback<KeyDownEvent>(InstrumentKeyDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<KeyUpEvent>(InstrumentKeyUp, TrickleDown.TrickleDown);
            root.UnregisterCallback<KeyUpEvent>(TrackerShortcutKeyUp, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(InstrumentKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyUpEvent>(TrackerShortcutKeyUp, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyUpEvent>(InstrumentKeyUp, TrickleDown.TrickleDown);
        }
        void InstrumentPointerDown(PointerDownEvent e)
        {
            if(pane!=3 || IsTyping(e.target as VisualElement))return;
            // Empty space, labels and non-focusable controls must end an old typing focus too.
            if(IsTyping(rootVisualElement.focusController?.focusedElement as VisualElement))rootVisualElement.Focus();
        }
        void InstrumentFocusChanged(FocusInEvent e) => RefreshKeyboardStatus();
        void RefreshKeyboardStatus()
        {
            if(keyboardStatus==null)return;
            bool typing=IsTyping(rootVisualElement.focusController?.focusedElement as VisualElement);
            keyboardStatus.text=typing?"Typing":keyboardProblem!=null?"⚠ No preview":"Keyboard: Z–M / Q–U";
            keyboardStatus.tooltip=keyboardProblem!=null?"The preview channel could not start: "+keyboardProblem:"The Instrument tab plays the selected sound with Z–M and Q–U, including chords, on the same clean preview channel as a MIDI keyboard; Panic silences both. S, D, G, H, J and 2, 3, 5, 6, 7 are black keys. Only editing text or a number temporarily uses keys for typing. Enter or Escape finishes typing; clicking elsewhere also returns to audition.";
        }
        VisualElement InstrumentKeyboardStatus()
        {
            keyboardStatus=Z.Text("Keyboard: Z–M / Q–U",tooltip:"");
            keyboardStatus.name="instrument-keyboard-status";
            keyboardStatus.style.width=170;
            RefreshKeyboardStatus();
            return keyboardStatus;
        }
        static bool IsTyping(VisualElement element)
        {
            for (var node = element; node != null; node = node.parent)
                for (var type = node.GetType(); type != null; type = type.BaseType)
                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(TextInputBaseField<>)) return true;
            return false;
        }
        void BuildInstrumentKeyboard(VisualElement root)
        {
            stage.focusable = true;
            root.Add(Flow(
                Named(Z.ScrubNumber("Octave", octave, 0, 8, "Starting octave for computer-keyboard audition.", v => { ReleaseInstrumentKeys(); octave = v; }, 100, defaultValue: 5), "instrument-keyboard-octave"),
                InstrumentKeyboardStatus()));
        }
        void InstrumentKeyDown(KeyDownEvent e)
        {
            if (pane != 3 || Instrument == null || e.ctrlKey || e.commandKey || e.altKey) return;
            if (IsTyping(e.target as VisualElement) || IsTyping(rootVisualElement.focusController?.focusedElement as VisualElement))
            {
                if(e.keyCode==KeyCode.Return||e.keyCode==KeyCode.KeypadEnter||e.keyCode==KeyCode.Escape)
                    rootVisualElement.schedule.Execute(()=> { rootVisualElement.Focus(); RefreshKeyboardStatus(); });
                return;
            }
            if (e.keyCode == KeyCode.Escape) { ReleaseInstrumentKeys(); e.StopPropagation(); e.PreventDefault(); return; }
            int offset = PianoOffset(e.keyCode);
            if (offset < 0) return;
            e.StopPropagation(); e.PreventDefault();
            if (instrumentKeys.ContainsKey(e.keyCode)) return;
            if (keyboardInstrument != instrument) ReleaseInstrumentKeys();
            int first = Instrument.family == InstrumentFamily.DrumKit ? drumFirstOctave : octave;
            int note = Math.Min(119, first * 12 + offset);
            keyboardInstrument = instrument; instrumentKeys[e.keyCode] = note;
            PlayPreviewKey(note);
        }
        // Instrument and drum-kit keys (typed or clicked on the drum map) all start here.
        void PlayPreviewKey(int note)
        {
            StopDrumSourcePreview();
            RouteLiveKeys();
            string problem = midiRouter.ComputerKeyDown(note) ? null : midiTargetText.TrimStart('→', ' ');
            if (problem != keyboardProblem) { keyboardProblem = problem; RefreshKeyboardStatus(); }
        }
        // A button has no hold: the key sounds for half a second, unless the same note is held on the keyboard by then.
        void TapPreviewKey(int note)
        {
            PlayPreviewKey(note);
            rootVisualElement.schedule.Execute(() => { if (!instrumentKeys.ContainsValue(note)) midiRouter.ComputerKeyUp(note); }).StartingIn(500);
        }
        void InstrumentKeyUp(KeyUpEvent e)
        {
            if (!instrumentKeys.TryGetValue(e.keyCode, out int note)) return;
            instrumentKeys.Remove(e.keyCode); e.StopPropagation(); e.PreventDefault();
            midiRouter.ComputerKeyUp(note);
        }
        void ReleaseInstrumentKeys()
        {
            foreach (var note in instrumentKeys.Values) midiRouter.ComputerKeyUp(note);
            instrumentKeys.Clear(); keyboardInstrument = null;
        }
        void OnLostFocus(){ReleaseInstrumentKeys();ResetOctaveShortcutLatch();}
        void OnFocus()
        {
            if(pane==3) { rootVisualElement.Focus(); RefreshKeyboardStatus(); }
        }
    }
}
