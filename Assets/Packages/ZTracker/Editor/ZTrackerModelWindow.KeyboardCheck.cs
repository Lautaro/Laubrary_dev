using System;
using System.Linq;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Midi;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        public static string CheckKeyboardFocus()
        {
            var previous=focusedWindow;
            var window=CreateInstance<ZTrackerModelWindow>();
            var song=CreateInstance<ZTrackerSong>();var instrument=CreateInstance<ZTrackerInstrument>();
            try
            {
                instrument.schemaVersion=1;instrument.model=NewInstrumentData();song.schemaVersion=1;song.model=TrackerEngineCheck.FixtureSong(instrument);
                window.song=song;window.instrument=instrument;window.entryInstrument=0;window.pane=3;window.Show();window.CreateGUI();
                void Need(bool ok,string why){if(!ok)throw new Exception(why);}
                void Key(VisualElement target,KeyCode code,bool down){using(var e=down?(EventBase)KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=code}):KeyUpEvent.GetPooled(new Event{type=EventType.KeyUp,keyCode=code})){target.SendEvent(e);}}
                // Keys reach the clean preview's live queue synchronously; the queue counts every event written to it.
                long Written()=>window.MidiPreviewHost?.LiveInput?.Written??-1;
                for(int run=0;run<2;run++)
                {
                    var root=window.rootVisualElement;var button=root.Q<Button>("save");button.Focus();
                    Key(button,KeyCode.Z,true);long start=Written()-1;
                    Key(button,KeyCode.X,true);Key(button,KeyCode.C,true);Key(button,KeyCode.Z,true);
                    Need(window.instrumentKeys.Count==3&&Written()-start==3,"Button focus or repeat suppression blocked chord (written "+(Written()-start)+")");
                    Need(window.MidiPreviewHost!=null&&window.SongHost==null,"Keys did not go to the clean preview channel, or started the song renderer");
                    Key(button,KeyCode.X,false);Need(window.instrumentKeys.Count==2&&Written()-start==4,"Independent key-up failed");
                    var text=root.Q<TextField>();text.Focus();Key(text,KeyCode.V,true);Need(window.instrumentKeys.Count==2&&Written()-start==4,"Typing triggered a note");
                    using(var click=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown})) { click.target=window.stage;window.InstrumentPointerDown(click); }
                    Need(!IsTyping(root.focusController.focusedElement as VisualElement),"Blank-space click left stale typing focus");
                    Key(root,KeyCode.V,true);Need(window.instrumentKeys.Count==3,"Audition did not resume after typing");
                    window.OnLostFocus();Need(window.instrumentKeys.Count==0&&Written()-start==8,"Focus loss left held notes");
                    Need(window.keyboardProblem==null,window.keyboardProblem);
                    window.BuildPane();
                }
                return "PASS: two attached-view walks, button-focus chords on the clean preview, repeat suppression, typing exclusion, blank-space return, focus-loss release";
            }
            finally { window.Close();DestroyImmediate(song);DestroyImmediate(instrument);if(previous!=null)previous.Focus(); }
        }
        // The computer keyboard's path on the same clean channel the window builds: chords over its columns even with
        // the Cut new-note action, independent release, a re-pressed key, and the song's voice capacity.
        public static string CheckKeyboardPolyphony()
        {
            var instrument = CreateInstance<ZTrackerInstrument>();
            var left = new NativeArray<float>(256, Allocator.TempJob);
            var right = new NativeArray<float>(256, Allocator.TempJob);
            try
            {
                instrument.schemaVersion = 1; instrument.model = NewInstrumentData();
                var p = instrument.model.parameters; p.attack = p.decay = 0; p.sustain = 1; p.release = 1;
                instrument.model.sampler.nna = NewNoteAction.Cut;
                void Need(bool ok, string message) { if (!ok) throw new Exception(message); }
                foreach (int capacity in new[] { 0, 2 })
                    using (var channel = PlaybackSnapshot.CreateClean(instrument, 12))
                    {
                        if (capacity > 0) channel.Data.voiceCapacity = capacity;
                        using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(channel.Data)))
                        {
                            var router = new ZTrackerMidiRouter(); router.SetRoute(engine.LiveInput, 0, 0, 0);
                            int Held(int note) => engine.Snapshot.voices.ToArray().Count(v => v.active && !v.released && v.note == note);
                            foreach (int n in new[] { 60, 64, 67 }) Need(router.ComputerKeyDown(n), "Key refused");
                            engine.Render(left, right, 256);
                            Need(engine.Compiled, "Expected compiled audio renderer");
                            if (capacity > 0) { Need(engine.Snapshot.voices.ToArray().Count(v => v.active) == capacity, "Song voice capacity was not respected"); continue; }
                            Need(Held(60) > 0 && Held(64) > 0 && Held(67) > 0, "Cut setting prevented held chord");
                            router.ComputerKeyUp(64); engine.Render(left, right, 256);
                            Need(Held(60) > 0 && Held(64) == 0 && Held(67) > 0, "Middle-key release affected another note");
                            router.ComputerKeyDown(64); engine.Render(left, right, 256);
                            Need(Held(64) > 0, "Re-pressed key did not sound");
                            foreach (int n in new[] { 60, 64, 67 }) router.ComputerKeyUp(n);
                            engine.Render(left, right, 256);
                            Need(Held(60) + Held(64) + Held(67) == 0, "Held notes survived all-key release");
                        }
                    }
                return "PASS: compiled chord with Cut on the clean channel, independent release, re-pressed key, all-key release, voice capacity";
            }
            finally { left.Dispose(); right.Dispose(); DestroyImmediate(instrument); }
        }
    }
}
