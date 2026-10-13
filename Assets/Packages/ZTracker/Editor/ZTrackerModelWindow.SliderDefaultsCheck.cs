using System;
using System.Linq;
using System.Reflection;
using Laubrary.ZTracker.Model;
using Laubrary.ZTracker.Engine;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        public static string CheckSliderDefaults()
        {
            var window=CreateInstance<ZTrackerModelWindow>();var asset=CreateInstance<ZTrackerInstrument>();var songAsset=CreateInstance<ZTrackerSong>();
            var clip=AudioClip.Create("Reset probe",4800,1,48000,false);
            int inspected=0,resets=0;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void Need(bool ok,string why){if(!ok)throw new Exception(why);}
            void Layout(){var panel=window.rootVisualElement.panel;for(int i=0;i<3;i++)panel.GetType().GetMethod("ValidateLayout",flags|BindingFlags.Public)?.Invoke(panel,null);}
            void Inventory(string context)
            {
                Layout();
                foreach(var slider in window.rootVisualElement.Query<ZuiMicroSlider>().ToList())
                {Need(typeof(ZuiMicroSlider).GetField("_default",flags).GetValue(slider)!=null,context+" slider missing default: "+slider.name+" "+slider.tooltip);inspected++;}
                foreach(var range in window.rootVisualElement.Query<ZuiMicroMinMax>().ToList())
                {Need(typeof(ZuiMicroMinMax).GetField("_lowDefault",flags).GetValue(range)!=null&&typeof(ZuiMicroMinMax).GetField("_highDefault",flags).GetValue(range)!=null,context+" range missing defaults: "+range.name);inspected++;}
            }
            void DoubleClick(VisualElement element)
            {
                Need(element!=null,"Reset control missing");Layout();
                var root=element.panel.visualTree;var at=element.worldBound.center;
                using(var e=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,clickCount=2,mousePosition=at})){e.target=element;element.SendEvent(e);}
                PointerRelease(root,at);
                resets++;
            }
            try
            {
                asset.schemaVersion=songAsset.schemaVersion=1;asset.model=NewInstrumentData();songAsset.model=TrackerEngineCheck.FixtureSong(asset);
                window.song=songAsset;window.instrument=asset;window.pane=3;window.Show();window.CreateGUI();
                foreach(var mode in new[]{SynthMode.Subtractive,SynthMode.FM})
                {
                    asset.model.synthMode=mode;asset.model.parameters.blendEnvelope=true;asset.model.parameters.glideEnabled=true;asset.model.parameters.legatoEnabled=true;asset.model.parameters.legatoVelocity=LegatoVelocity.Slide;asset.model.parameters.unisonVoices=3;asset.model.parameters.vibratoDepth=20;
                    for(int tab=0;tab<5;tab++){window.instrumentTab=tab;window.BuildPane();Inventory(mode+" tab "+tab);}
                }
                asset.model.family=InstrumentFamily.Sampler;
                asset.model.sampler.samples.Add(new SampleData{id="sample",pcm=clip,regionEndFrame=clip.samples,loopEndFrame=clip.samples,loop=SampleLoop.Forward});
                asset.model.sampler.zones.Add(new Keyzone{id="zone",blend=new SampleBlendExtension{pcmB=clip}});
                asset.model.modulation.Add(new ModulationSet{id="mod",devices={new ModulationDevice{id="adsr",kind=ModulationDeviceKind.AHDSR}}});
                for(int tab=0;tab<6;tab++){window.instrumentTab=tab;window.BuildPane();Inventory("Sampler tab "+tab);}
                asset.model.family=InstrumentFamily.DrumKit;asset.model.drumKit.mappings.Add(new DrumMapping{id="drum",synth=asset});window.drumId="drum";window.BuildPane();Inventory("Drums");
                foreach(int pane in new[]{0,1,2,4}){window.pane=pane;window.BuildPane();Inventory("Workspace "+pane);}
                for(int pass=0;pass<2;pass++)
                {
                    asset.model.family=InstrumentFamily.Synth;asset.model.synthMode=SynthMode.FM;window.pane=3;window.instrumentTab=0;
                    asset.model.parameters.volume=.63f;asset.model.parameters.fmOperators[1].level=.82f;asset.model.parameters.fmOperators[1].attack=.8f;window.BuildPane();Layout();
                    DoubleClick(window.rootVisualElement.Q<ZuiValueControl>("synth-volume").Q<ZuiMicroSlider>());
                    Need(Mathf.Approximately(asset.model.parameters.volume,NewInstrumentData().parameters.volume),"Volume percent reset units");
                    Undo.FlushUndoRecordObjects();Undo.PerformUndo();Need(Mathf.Approximately(asset.model.parameters.volume,.63f),"Reset Undo");Undo.PerformRedo();Need(Mathf.Approximately(asset.model.parameters.volume,NewInstrumentData().parameters.volume),"Reset Redo");
                    window.BuildPane();Layout();
                    DoubleClick(window.rootVisualElement.Q<ZuiValueControl>("fmOperators.1.level").Q<ZuiMicroSlider>());
                    Need(Mathf.Approximately(asset.model.parameters.fmOperators[1].level,NewInstrumentData().parameters.fmOperators[1].level),"FM per-operator level default");
                    DoubleClick(window.rootVisualElement.Q<ZuiAdsr>("fm-adsr-1").Q<ZuiMicroSlider>("adsr-input-attack"));
                    Need(Mathf.Approximately(asset.model.parameters.fmOperators[1].attack,NewInstrumentData().parameters.fmOperators[1].attack),"Milliseconds reset units");
                    window.pane=4;songAsset.model.bpm=177;window.BuildPane();Layout();
                    DoubleClick(window.rootVisualElement.Query<ZuiMicroSlider>().ToList().First(s=>s.tooltip.StartsWith("Song tempo")));
                    Need(songAsset.model.bpm==new SongData().bpm,"Song tempo reset");
                }
                return "PASS "+inspected+" slider/range instances have defaults; "+resets+" dispatched double-clicks; percent/millisecond conversion, per-operator defaults, Undo/Redo and rebuilt-view repeat";
            }
            finally{window.StopPreview();window.Close();Undo.ClearUndo(asset);Undo.ClearUndo(songAsset);DestroyImmediate(asset);DestroyImmediate(songAsset);DestroyImmediate(clip);}
        }
    }
}
