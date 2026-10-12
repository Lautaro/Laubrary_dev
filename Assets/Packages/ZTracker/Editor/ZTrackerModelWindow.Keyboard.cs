using System;
using System.Collections.Generic;
using Laubrary.ZTracker.Model;
using Laubrary.ZTracker.Engine;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        readonly List<KeyCode> instrumentKeys = new List<KeyCode>();
        readonly Dictionary<KeyCode, long> instrumentKeyTokens = new Dictionary<KeyCode, long>();
        long nextInstrumentKey;
        ZTrackerInstrument keyboardInstrument;
        Label keyboardStatus;

        void WireInstrumentKeyboard(VisualElement root)
        {
            root.focusable = true;
            root.UnregisterCallback<PointerDownEvent>(InstrumentPointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(InstrumentPointerDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<FocusInEvent>(InstrumentFocusChanged);
            root.RegisterCallback<FocusInEvent>(InstrumentFocusChanged);
            root.UnregisterCallback<KeyDownEvent>(InstrumentKeyDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<KeyUpEvent>(InstrumentKeyUp, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(InstrumentKeyDown, TrickleDown.TrickleDown);
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
            keyboardStatus.text=typing?"Typing":"Keyboard: Z–M / Q–U";
        }
        VisualElement InstrumentKeyboardStatus()
        {
            keyboardStatus=Z.Text("Keyboard: Z–M / Q–U",tooltip:"The Instrument tab plays the selected sound with Z–M and Q–U, including chords. S, D, G, H, J and 2, 3, 5, 6, 7 are black keys. Only editing text or a number temporarily uses keys for typing. Enter or Escape finishes typing; clicking elsewhere also returns to audition.");
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
                Named(Z.MicroSlider("Octave", octave, 0, 8, "Starting octave for computer-keyboard audition.", v => { ReleaseInstrumentKeys(); octave = Mathf.RoundToInt(v); }, 145, defaultValue: 5, decimals: 0), "instrument-keyboard-octave"),
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
            if (instrumentKeys.Contains(e.keyCode)) return; // OS auto-repeat is not another strike.
            if (keyboardInstrument != instrument) ReleaseInstrumentKeys();
            keyboardInstrument = instrument; instrumentKeys.Add(e.keyCode);
            int first = Instrument.family == InstrumentFamily.DrumKit ? drumFirstOctave : octave;
            long token=++nextInstrumentKey;instrumentKeyTokens[e.keyCode]=token;
            PlayEditingInstrument(Math.Min(119, first * 12 + offset),token);
        }
        void PlayEditingInstrument(int note,long token)
        {
            StopDrumSourcePreview();
            if(!playing&&drumNeedsRefresh) { StopPreview(); drumNeedsRefresh=false; }
            // A refused structural refresh must not leave the next editing audition on old data.
            if (!playing && lastError != null) StopPreview();
            try { EnsurePreview(); if(entryInstrument>=0&&entryInstrument<Data.instruments.Count)host.SendCommand(TrackerCommand.AuditionKey(entryInstrument,note,token,FirstSequencer())); }
            catch(Exception error) { lastError=error.Message; RefreshTransport(); }
        }
        void InstrumentKeyUp(KeyUpEvent e)
        {
            int at = instrumentKeys.IndexOf(e.keyCode);
            if (at < 0) return;
            instrumentKeys.RemoveAt(at); e.StopPropagation(); e.PreventDefault();
            if(instrumentKeyTokens.TryGetValue(e.keyCode,out var token)) { host?.SendCommand(TrackerCommand.ReleaseKey(token)); instrumentKeyTokens.Remove(e.keyCode); }
        }
        void ReleaseInstrumentKeys()
        {
            foreach(var token in instrumentKeyTokens.Values)host?.SendCommand(TrackerCommand.ReleaseKey(token));
            instrumentKeyTokens.Clear();
            instrumentKeys.Clear(); keyboardInstrument = null;
        }
        void OnLostFocus() => ReleaseInstrumentKeys();
        void OnFocus()
        {
            if(pane==3) { rootVisualElement.Focus(); RefreshKeyboardStatus(); }
        }
    }
}
