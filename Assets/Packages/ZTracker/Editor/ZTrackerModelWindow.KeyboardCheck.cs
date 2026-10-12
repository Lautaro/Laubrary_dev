using System;
using Laubrary.ZTracker.Engine;
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
                for(int run=0;run<2;run++)
                {
                    var root=window.rootVisualElement;var button=root.Q<Button>("save");button.Focus();
                    Key(button,KeyCode.Z,true);Key(button,KeyCode.X,true);Key(button,KeyCode.C,true);Key(button,KeyCode.Z,true);
                    Need(window.instrumentKeys.Count==3,"Button focus or repeat suppression blocked chord");
                    Key(button,KeyCode.X,false);Need(window.instrumentKeys.Count==2,"Independent key-up failed");
                    var text=root.Q<TextField>();text.Focus();Key(text,KeyCode.V,true);Need(window.instrumentKeys.Count==2,"Typing triggered a note");
                    using(var click=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown})) { click.target=window.stage;window.InstrumentPointerDown(click); }
                    Need(!IsTyping(root.focusController.focusedElement as VisualElement),"Blank-space click left stale typing focus");
                    Key(root,KeyCode.V,true);Need(window.instrumentKeys.Count==3,"Audition did not resume after typing");
                    window.OnLostFocus();Need(window.instrumentKeys.Count==0,"Focus loss left held notes");
                    Need(string.IsNullOrEmpty(window.lastError),window.lastError);
                    window.BuildPane();
                }
                return "PASS: two attached-view walks, button-focus chords, repeat suppression, typing exclusion, blank-space return, focus-loss release";
            }
            finally { window.Close();DestroyImmediate(song);DestroyImmediate(instrument);if(previous!=null)previous.Focus(); }
        }
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
                var song = TrackerEngineCheck.FixtureSong(instrument);
                using (var engine = new TrackerOffline(TrackerPreparedSong.Prepare(song)))
                {
                    void Need(bool ok, string message) { if (!ok) throw new Exception(message); }
                    int Held(long key) { int count = 0; foreach(var v in engine.Snapshot.voices) if(v.active && !v.released && v.cohort == -key) count++; return count; }
                    engine.SendCommand(TrackerCommand.Audition(0, 48)); engine.Render(left, right, 256);
                    long foreground = engine.Snapshot.columns[0].cohort;
                    engine.SendCommand(TrackerCommand.AuditionKey(0, 60, 1));
                    engine.SendCommand(TrackerCommand.AuditionKey(0, 64, 2));
                    engine.SendCommand(TrackerCommand.AuditionKey(0, 67, 3)); engine.Render(left, right, 256);
                    Need(engine.Compiled, "Expected compiled audio renderer");
                    Need(Held(1)>0 && Held(2)>0 && Held(3)>0, "Cut setting prevented held chord");
                    Need(engine.Snapshot.columns[0].cohort == foreground, "Audition changed tracker column");
                    engine.SendCommand(TrackerCommand.ReleaseKey(2)); engine.Render(left, right, 256);
                    Need(Held(1)>0 && Held(2)==0 && Held(3)>0, "Middle-key release affected another note");
                    engine.SendCommand(TrackerCommand.AuditionKey(0, 64, 4)); engine.SendCommand(TrackerCommand.ReleaseKey(2)); engine.Render(left, right, 256);
                    Need(Held(4)>0, "Stale release silenced re-pressed key");
                    engine.SendCommand(new TrackerCommand { kind = TrackerCommandKind.AuditionOff, a = 0, b = 0 }); engine.Render(left, right, 256);
                    Need(Held(1)>0 && Held(3)>0 && Held(4)>0, "Tracker note-off affected held audition keys");
                    engine.SendCommand(TrackerCommand.ReleaseKey(1)); engine.SendCommand(TrackerCommand.ReleaseKey(3)); engine.SendCommand(TrackerCommand.ReleaseKey(4)); engine.Render(left, right, 256);
                    Need(Held(1)+Held(2)+Held(3)+Held(4)==0, "Held notes survived all-key release");
                }
                song.voiceCapacity=2;
                using(var engine=new TrackerOffline(TrackerPreparedSong.Prepare(song)))
                {
                    engine.SendCommand(TrackerCommand.AuditionKey(0,60,1)); engine.SendCommand(TrackerCommand.AuditionKey(0,64,2)); engine.SendCommand(TrackerCommand.AuditionKey(0,67,3)); engine.Render(left,right,256);
                    int active=0; foreach(var voice in engine.Snapshot.voices)if(voice.active)active++;
                    if(active!=2)throw new Exception("Song voice capacity was not respected");
                }
                return "PASS: compiled chord with Cut, independent release, stale release, tracker isolation, all-key release, voice capacity";
            }
            finally { left.Dispose(); right.Dispose(); DestroyImmediate(instrument); }
        }
    }
}
