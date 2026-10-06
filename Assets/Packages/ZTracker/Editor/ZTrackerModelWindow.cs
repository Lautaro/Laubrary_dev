using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.ZTracker.Model;
using Laubrary.ZTracker.Engine;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>Canonical model authoring. View changes never recreate or stop the audio graph.</summary>
    public sealed partial class ZTrackerModelWindow : ZuiWindow
    {
        [SerializeField] ZTrackerSong song;
        [SerializeField] ZTrackerInstrument instrument;
        [SerializeField] int order,row,track,sub,octave=5,step=1,pane,entryInstrument;
        [SerializeField] bool follow=true,editMode=true;
        TrackerSapGenerator host;
        GameObject preview;
        bool playing;
        bool pendingLive;
        string lastError;
        VisualElement controls,stage;
        ScrollView controlScroll,grid;
        Label status,help;
        SongData Data=>song!=null&&song.schemaVersion==1?song.model:null;
        PatternData Pattern=>Data==null||Data.sequence.Count==0?null:Data.patterns.Find(p=>p.id==Data.sequence[Mathf.Clamp(order,0,Data.sequence.Count-1)].patternId);
        TrackData SelectedTrack=>Data==null||Data.tracks.Count==0?null:Data.tracks[Mathf.Clamp(track,0,Data.tracks.Count-1)];
        [MenuItem("Laubrary/ZTracker")]
        public static void Open()=>GetWindow<ZTrackerModelWindow>("ZTracker");
        protected override void BuildUI(VisualElement root)
        {
            minSize=new Vector2(760,440);
            var path=AssetDatabase.FindAssets("ZTracker t:StyleSheet").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p=>p.EndsWith("/ZTracker.uss"));
            if(path!=null)root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(path));
            root.Add(Flow(Named(Z.Object(song,"Choose a song to edit.",v=>{StopPreview();song=v;order=row=track=sub=0;Upgrade();Rebuild();},200),"song"),Button("New song","Create a saved empty version-1 song.",CreateSong,"new-song"),Button("Save","Save this song and its edited instrument.",Save,"save"),
                Named(Z.IconButton("arrow-counter-clockwise","Undo the last authored change.",Undo.PerformUndo),"undo"),Named(Z.IconButton("arrow-clockwise","Redo the last undone change.",Undo.PerformRedo),"redo")));
            status=Z.Text("Idle",tooltip:"Preview status.");status.name="transport-status";status.style.width=210;status.style.height=24;status.style.whiteSpace=WhiteSpace.NoWrap;status.style.overflow=Overflow.Hidden;
            root.Add(Flow(Button("Play","Start the song from its first row.",()=>Play(false),"play"),Button("From cursor","Start at the selected sequence slot and row.",()=>Play(true),"play-cursor"),Button("Stop","Stop preview and release its owned audio buffers.",StopPreview,"stop"),Z.Toggle("Follow","Scroll the pattern to each row reached by playback.",follow,v=>follow=v),status));
            root.Add(Z.Segmented(pane,new[]{"Pattern","Mixer","Automation","Instrument"},"Choose the authoring workspace.",v=>{pane=v;BuildPane();}));
            controlScroll=new ScrollView();controls=new VisualElement();controlScroll.Add(controls);
            stage=new VisualElement();stage.style.flexGrow=1;stage.style.minWidth=0;var workspace=Z.Split("tracker.model.workspace",270,controlScroll,stage);workspace.style.flexGrow=1;workspace.style.minHeight=0;root.Add(workspace);
            BuildPane();root.schedule.Execute(Pump).Every(30);
        }
        void Upgrade()
        {
            if(song==null)return;
            if(song.schemaVersion==0){Undo.RegisterCompleteObjectUndo(song,"Tracker: migrate song");ZTrackerMigration.Upgrade(song,out _);EditorUtility.SetDirty(song);}
            if(Data!=null)foreach(var i in Data.instruments)if(i!=null&&i.schemaVersion==0){Undo.RegisterCompleteObjectUndo(i,"Tracker: migrate instrument");ZTrackerMigration.Upgrade(i,out _);EditorUtility.SetDirty(i);}
            lastError=ZTrackerMigration.VersionError(song.schemaVersion);
        }
        void BuildPane()
        {
            if(controls==null)return;var offset=controlScroll.scrollOffset;controls.Clear();stage.Clear();
            if(Data==null){controls.Add(Button("New song","Create the first song.",CreateSong));RefreshTransport();return;}
            order=Mathf.Clamp(order,0,Math.Max(0,Data.sequence.Count-1));track=Mathf.Clamp(track,0,Math.Max(0,Data.tracks.Count-1));
            if(pane==0){BuildSongControls(controls);BuildPattern();}
            else if(pane==1)BuildMixer(stage);
            else if(pane==2)BuildAutomation(stage);
            else {BuildInstrumentSelector(controls);BuildInstrument(stage);}
            controlScroll.schedule.Execute(()=>controlScroll.scrollOffset=offset);RefreshTransport();
        }
        partial void BuildMixer(VisualElement root);
        partial void BuildAutomation(VisualElement root);
        partial void BuildInstrument(VisualElement root);
        void Change(UnityEngine.Object target,string label,Action edit,bool rebuild,TrackerCommand? command=null)
        {
            if(target==null)return;
            // Complete snapshots restore sparse removals, cropped rows and nested serialized values.
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Tracker: "+label);Undo.RegisterCompleteObjectUndo(target,"Tracker: "+label);
            try{edit();EditorUtility.SetDirty(target);Undo.CollapseUndoOperations(group);if(command.HasValue&&host!=null){if(!host.SendCommand(command.Value))lastError="Live update pending: command was not accepted";}else RefreshLive();if(rebuild)BuildPane();else RefreshCells();}
            catch(Exception ex){Undo.RevertAllDownToGroup(group);lastError=ex.Message;BuildPane();}
            RefreshTransport();
        }
        void SongEdit(string label,Action edit,bool rebuild=false)=>Change(song,label,edit,rebuild);
        void InstrumentEdit(string label,Action edit,bool rebuild=false,bool liveScalar=false)=>Change(instrument,label,edit,rebuild);
        void InstrumentScalarEdit(string label,TrackerParameter parameter,float value,Action edit,bool rebuild=false)=>Change(instrument,label,edit,rebuild,TrackerCommand.SetParameter(entryInstrument,parameter,value));
        void InstrumentMacroEdit(string label,int macro,float value,Action edit,bool rebuild=false)=>Change(instrument,label,edit,rebuild,TrackerCommand.SetMacro(entryInstrument,macro,value));
        void RefreshLive()
        {
            if(host==null||Data==null)return;
            TrackerPreparedSong next=null;
            try{next=TrackerPreparedSong.Prepare(Data,AudioSettings.outputSampleRate);if(host.RefreshPrepared(next,out var reason)){next=null;lastError=null;pendingLive=false;}else {lastError=reason;pendingLive=reason!=null&&reason.Contains("still starting");}}
            catch(Exception ex){lastError="Live update pending: "+ex.Message;}
            finally{next?.Dispose();}
        }
        bool SendLive(TrackerCommand command)=>host!=null&&host.SendCommand(command);
        void Play(bool cursor)
        {
            try{EnsurePreview();host.SendCommand(TrackerCommand.Play());if(cursor)host.SendCommand(new TrackerCommand{kind=TrackerCommandKind.Seek,a=order,b=row});playing=true;lastError=null;}
            catch(Exception ex){lastError=ex.Message;}RefreshTransport();
        }
        void EnsurePreview()
        {
            if(host!=null)return;if(Data==null)throw new InvalidOperationException("Choose or create a song");
            var prepared=TrackerPreparedSong.Prepare(Data,AudioSettings.outputSampleRate);
            try{preview=new GameObject("ZTracker model preview"){hideFlags=HideFlags.HideAndDontSave};host=preview.AddComponent<TrackerSapGenerator>();host.Configure(prepared,false);prepared=null;var source=preview.GetComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;source.generator=host;source.Play();}
            catch{if(preview!=null)DestroyImmediate(preview);host=null;throw;}
            finally{prepared?.Dispose();}
        }
        void Audition(int pitch)
        {
            try{EnsurePreview();if(entryInstrument>=0&&entryInstrument<Data.instruments.Count)host.SendCommand(TrackerCommand.Audition(entryInstrument,pitch,127,FirstSequencer()));}
            catch(Exception ex){lastError=ex.Message;}RefreshTransport();
        }
        int FirstSequencer()=>SelectedTrack!=null&&SelectedTrack.kind==TrackKind.Sequencer?track:Data.tracks.FindIndex(t=>t.kind==TrackKind.Sequencer);
        void AuditionOff(){if(host!=null)host.SendCommand(new TrackerCommand{kind=TrackerCommandKind.AuditionOff,a=FirstSequencer(),b=0});}
        void StopPreview(){playing=false;pendingLive=false;if(preview!=null)DestroyImmediate(preview);preview=null;host=null;lastError=null;RefreshTransport();}
        void Pump()
        {
            if(host!=null){host.PollRetirement();if(pendingLive&&host.RenderedBlocks>0)RefreshLive();while(host.ReadEvent(out var e)){if(e.kind==TrackerEventKind.Stopped)playing=false;if(e.kind==TrackerEventKind.Row&&follow&&playing){bool changed=e.sequence!=order;order=e.sequence;if(changed&&pane==0){BuildPane();}HighlightPlaying(e.row);}}
            }RefreshTransport();
        }
        void RefreshTransport(){if(status==null)return;status.text=lastError!=null?"⚠ Live update pending":playing?"Playing":"Idle";status.tooltip=lastError??(playing?"The Burst song engine is playing. Edits update its existing audio stream.":"Preview is stopped; Play starts the authored song.");}
        void OnEnable(){Undo.undoRedoPerformed+=OnUndo;Upgrade();}
        void OnUndo(){RefreshLive();BuildPane();}
        protected override void OnDisable(){Undo.undoRedoPerformed-=OnUndo;StopPreview();base.OnDisable();}
        // ZuiWindow may rebuild on domain reload/view changes; it must not stop a healthy graph.
        protected override void OnBeforeRebuild(){}
        void Save(){if(song!=null)AssetDatabase.SaveAssetIfDirty(song);if(instrument!=null)AssetDatabase.SaveAssetIfDirty(instrument);}
        static string Id()=>Guid.NewGuid().ToString("N");
        static ColumnVisibility Visibility(TrackData t,int column){while(t.columns.Count<=column)t.columns.Add(new ColumnVisibility());return t.columns[column];}
        static void Folder(){if(!AssetDatabase.IsValidFolder("Assets/ZTracker"))AssetDatabase.CreateFolder("Assets","ZTracker");}
        void CreateSong()
        {
            StopPreview();Folder();song=CreateInstance<ZTrackerSong>();song.schemaVersion=1;song.model=new SongData{id=Id(),name="Untitled"};
            song.model.tracks.Add(new TrackData{id=Id(),name="Track 1",columns=new List<ColumnVisibility>{new ColumnVisibility{volume=true,pan=true,delay=true,sampleFx=true}}});song.model.tracks.Add(new TrackData{id=Id(),name="Master",kind=TrackKind.Master,visibleNoteColumns=0});
            var pattern=NewPattern("Pattern 1");song.model.patterns.Add(pattern);song.model.sequence.Add(new SequenceSlot{id=Id(),patternId=pattern.id});
            AssetDatabase.CreateAsset(song,AssetDatabase.GenerateUniqueAssetPath("Assets/ZTracker/Song.asset"));Undo.RegisterCreatedObjectUndo(song,"Tracker: create song");AssetDatabase.SaveAssetIfDirty(song);order=row=track=sub=0;pane=0;instrument=null;Rebuild();
        }
        PatternData NewPattern(string name){var p=new PatternData{id=Id(),name=name};foreach(var t in Data.tracks)p.tracks.Add(new PatternTrack{trackId=t.id});return p;}
        void BuildSongControls(VisualElement root)
        {
            root.Add(Z.Field("Name","Song title.",Z.TextInput(Data.name,"Rename the song.",v=>SongEdit("song name",()=>Data.name=v),175)));
            root.Add(Flow(DialSong("BPM",(float)Data.bpm,32,999,"Song tempo in beats per minute.",v=>Data.bpm=v,decimals:2),DialSong("LPB",Data.linesPerBeat,1,256,"Lines per beat.",v=>Data.linesPerBeat=(int)v),DialSong("TPL",Data.ticksPerLine,1,16,"Effect ticks per line.",v=>Data.ticksPerLine=(int)v)));
            var sequence=Z.BoxKeyed("Sequence","Drag slots to change their order.","tracker.model.sequence");var slots=Flow();
            for(int i=0;i<Data.sequence.Count;i++){int at=i;var slot=Data.sequence[i];var p=Data.patterns.Find(x=>x.id==slot.patternId);var b=Button($"{i:00} {p?.name}","Select this slot; drag to another slot to reorder.",()=>{order=at;row=0;BuildPane();});b.EnableInClassList("tracker-picked",i==order);Reorder(b,"model-sequence",i,(a,z)=>SongEdit("reorder sequence",()=>Move(Data.sequence,a,z),true));slots.Add(b);}sequence.Add(slots);
            sequence.Add(Flow(Button("Append","Append another use of the selected pattern.",()=>SongEdit("append slot",()=>Data.sequence.Add(new SequenceSlot{id=Id(),patternId=Pattern.id}),true)),Button("Remove","Remove the selected sequence slot while keeping its pattern.",()=>{if(Data.sequence.Count>1)SongEdit("remove slot",()=>Data.sequence.RemoveAt(order),true);})));root.Add(sequence);
            var patterns=Z.BoxKeyed("Pattern","Choose or create patterns independently of their sequence slots.","tracker.model.pattern");
            if(Pattern!=null){patterns.Add(Z.MiniRadio(Data.patterns.IndexOf(Pattern),Data.patterns.Select(p=>p.name).ToArray(),"Choose the pattern used in this sequence slot.",v=>SongEdit("assign pattern",()=>Data.sequence[order].patternId=Data.patterns[v].id,true),wrap:true));patterns.Add(Flow(Z.TextInput(Pattern.name,"Rename this pattern.",v=>SongEdit("pattern name",()=>Pattern.name=v),160),DialSong("Rows",Pattern.lineCount,1,512,"Resize this pattern; Undo restores all removed data.",v=>ZTrackerPatternOperations.Resize(Pattern,(int)v),true)));
            patterns.Add(Flow(Button("New pattern","Create and append an empty pattern.",()=>SongEdit("new pattern",()=>{var p=NewPattern("Pattern "+(Data.patterns.Count+1));Data.patterns.Add(p);Data.sequence.Add(new SequenceSlot{id=Id(),patternId=p.id});order=Data.sequence.Count-1;row=0;},true)),Button("Clone","Append a deep copy with its own identity.",()=>SongEdit("clone pattern",()=>{var p=ZTrackerMigration.Copy(Pattern);p.id=Id();p.name+=" copy";foreach(var t in p.tracks)foreach(var l in t.automation)l.id=Id();Data.patterns.Add(p);Data.sequence.Add(new SequenceSlot{id=Id(),patternId=p.id});order=Data.sequence.Count-1;},true))));}root.Add(patterns);
            BuildInstrumentSelector(root);
            if(SelectedTrack!=null){var t=SelectedTrack;var columns=Z.BoxKeyed("Columns","Choose visible columns without deleting authored cells.","tracker.model.columns");columns.Add(Z.MiniRadio(track,Data.tracks.Select(x=>x.name).ToArray(),"Choose a track for column display settings.",v=>{track=v;BuildPane();},wrap:true));
                if(t.kind==TrackKind.Sequencer)columns.Add(DialSong("Notes",t.visibleNoteColumns,1,12,"Visible note columns; hidden data remains stored.",v=>t.visibleNoteColumns=(int)v,true));columns.Add(DialSong("FX",t.visibleEffectColumns,0,8,"Visible effect columns; hidden effects remain stored.",v=>t.visibleEffectColumns=(int)v,true));
                for(int c=0;c<t.visibleNoteColumns;c++){int at=c;var visibility=c<t.columns.Count?t.columns[c]:new ColumnVisibility();columns.Add(Flow(Z.Text((c+1).ToString(),tooltip:"Note column number."),Z.Toggle("V","Show volume and volume commands.",visibility.volume,v=>SongEdit("volume display",()=>Visibility(t,at).volume=v,true)),Z.Toggle("P","Show pan and pan commands.",visibility.pan,v=>SongEdit("pan display",()=>Visibility(t,at).pan=v,true)),Z.Toggle("D","Show fractional note delay.",visibility.delay,v=>SongEdit("delay display",()=>Visibility(t,at).delay=v,true)),Z.Toggle("FX","Show the column-local sample effect.",visibility.sampleFx,v=>SongEdit("local FX display",()=>Visibility(t,at).sampleFx=v,true))));}root.Add(columns);}
        }
        void BuildInstrumentSelector(VisualElement root)
        {
            var box=Z.BoxKeyed("Instruments","Choose the instrument used by note entry and audition.","tracker.model.instruments");
            box.Add(Flow(Button("New instrument","Create and add a new instrument.",CreateInstrument,"new-instrument"),Named(Z.Object<ZTrackerInstrument>(null,"Add an existing instrument.",v=>{if(v==null)return;Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();if(v.schemaVersion==0){Undo.RegisterCompleteObjectUndo(v,"Tracker: migrate instrument");ZTrackerMigration.Upgrade(v,out _);EditorUtility.SetDirty(v);}SongEdit("add instrument",()=>Data.instruments.Add(v),true);Undo.CollapseUndoOperations(group);},155),"add-instrument")));
            if(Data.instruments.Count>0)box.Add(Z.MiniRadio(entryInstrument,Data.instruments.Select((v,i)=>$"{i:X2} {v?.name??"Missing"}").ToArray(),"Select an instrument; the preview button auditions it.",v=>{entryInstrument=v;instrument=Data.instruments[v];if(pane==3)BuildPane();},wrap:true));
            box.Add(Flow(Button("Edit","Open the selected instrument.",()=>{if(Data.instruments.Count>0){instrument=Data.instruments[Mathf.Clamp(entryInstrument,0,Data.instruments.Count-1)];pane=3;BuildPane();}}),Button("▶","Audition the selected instrument at the current octave.",()=>Audition(octave*12)),Button("■","Release instrument audition notes.",AuditionOff)));root.Add(box);
        }
        void CreateInstrument()
        {
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Folder();instrument=CreateInstance<ZTrackerInstrument>();instrument.type=InstrumentType.Synth;instrument.volume=.25f;ZTrackerMigration.Upgrade(instrument,out _);AssetDatabase.CreateAsset(instrument,AssetDatabase.GenerateUniqueAssetPath("Assets/ZTracker/Instrument.asset"));Undo.RegisterCreatedObjectUndo(instrument,"Tracker: create instrument");SongEdit("add instrument",()=>Data.instruments.Add(instrument));Undo.CollapseUndoOperations(group);entryInstrument=Data.instruments.Count-1;pane=3;BuildPane();
        }
        VisualElement DialSong(string label,float value,float min,float max,string tip,Action<float> apply,bool rebuild=false,int decimals=0)=>Dial(label,value,min,max,tip,v=>SongEdit(label,()=>apply(v),rebuild),decimals);
        static VisualElement Dial(string label,float value,float min,float max,string tip,Action<float> changed,int decimals=2,float width=124)=>Z.MicroSlider(label,value,min,max,tip,changed,width,decimals:decimals);
        static VisualElement Flow(params VisualElement[] children){var r=Z.Row();r.style.flexWrap=Wrap.Wrap;r.style.flexShrink=0;foreach(var c in children)if(c!=null)r.Add(c);return r;}
        static VisualElement Button(string label,string tip,Action action,string name=null)=>Named(Z.Button(label,tip,action),name);
        static T Named<T>(T element,string name) where T:VisualElement{if(name!=null)element.name=name;return element;}
        static T Clone<T>(T value)=>JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        static void Move<T>(IList<T> list,int from,int to){var value=list[from];list.RemoveAt(from);list.Insert(to,value);}
        static void Reorder(VisualElement item,string kind,int index,Action<int,int> moved)
        {
            item.RegisterCallback<PointerDownEvent>(e=>{if(e.button==0){DragAndDrop.PrepareStartDrag();DragAndDrop.SetGenericData("tracker."+kind,index);}});
            item.RegisterCallback<PointerMoveEvent>(e=>{if((e.pressedButtons&1)!=0&&DragAndDrop.GetGenericData("tracker."+kind)is int)DragAndDrop.StartDrag("Reorder "+kind);});
            item.RegisterCallback<DragUpdatedEvent>(e=>{if(DragAndDrop.GetGenericData("tracker."+kind)is int){DragAndDrop.visualMode=DragAndDropVisualMode.Move;e.StopPropagation();}});
            item.RegisterCallback<DragPerformEvent>(e=>{if(DragAndDrop.GetGenericData("tracker."+kind)is int from){DragAndDrop.AcceptDrag();DragAndDrop.SetGenericData("tracker."+kind,null);if(from!=index)moved(from,index);e.StopPropagation();}});
        }
    }
}
