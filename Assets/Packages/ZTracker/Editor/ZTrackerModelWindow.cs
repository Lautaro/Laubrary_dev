using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.AssetKit.Editor;
using Laubrary.ZTracker.Model;
using Laubrary.ZTracker.Engine;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>
    /// Canonical model authoring. View changes never recreate or stop the audio graph. The song is a LauAsset: the
    /// shared AssetKit window base gives it the same toolbar (pick, New, Browse, Duplicate, Rename, Delete), the
    /// thumbnail browser (shown when no song is open) and Tags as every other Laubrary tool.
    /// </summary>
    public sealed partial class ZTrackerModelWindow : ZuiAssetWindow<ZTrackerSong>
    {
        // The edited song is the AssetKit base's asset. A window saved before the move kept it in a field named
        // "song"; that value is read once from here and handed to the base, so an open window keeps its song.
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("song")] ZTrackerSong legacySong;
        ZTrackerSong song { get => asset; set => asset = value; }
        [SerializeField] ZTrackerInstrument instrument;
        [SerializeField] int order,row,track,sub,octave=DefaultPatternOctave,step=DefaultPatternStep,pane,entryInstrument;
        [SerializeField] bool follow=true,editMode=true;
        TrackerSapGenerator host;
        GameObject preview;
        bool playing;
        bool pendingLive;
        PlaybackSnapshot playbackSnapshot;
        SongData legacyView;
        ZTrackerSong legacyViewSource;
        readonly Dictionary<ZTrackerInstrument,InstrumentData> legacyInstrumentViews=new Dictionary<ZTrackerInstrument,InstrumentData>();
        string lastError;
        VisualElement controls,stage;
        ScrollView controlScroll,grid;
        Label status,help;
        IVisualElementScheduledItem pumpSchedule;
        SongData Data
        {
            get
            {
                if(song==null)return null;
                if(song.schemaVersion==1)return song.model;
                if(song.schemaVersion!=0)return null;
                if(legacyViewSource!=song||legacyView==null){legacyView=ZTrackerMigration.Convert(ZTrackerMigration.Capture(song));legacyViewSource=song;}
                return legacyView;
            }
        }
        PatternData Pattern=>Data==null||Data.sequence.Count==0?null:Data.patterns.Find(p=>p.id==Data.sequence[Mathf.Clamp(order,0,Data.sequence.Count-1)].patternId);
        TrackData SelectedTrack=>Data==null||Data.tracks.Count==0?null:Data.tracks[Mathf.Clamp(track,0,Data.tracks.Count-1)];
        [MenuItem("Laubrary/ZTracker")]
        public static void Open(){var window=GetWindow<ZTrackerModelWindow>("ZTracker");window.EnableMidiPreferenceRestore();window.rememberView=true;if(!window.viewRestored){window.viewRestored=true;if(window.RestoreViewState())window.Rebuild();}}
        /// <summary>Opens ZTracker on <paramref name="value"/> (LauAsset Edit, a double-click in the Project window).</summary>
        public static void OpenFor(ZTrackerSong value){Open();var window=GetWindow<ZTrackerModelWindow>("ZTracker");if(value!=null)window.SetAsset(value);}
        protected override string TypeLabel=>"Song";
        protected override string NewAssetName=>"Song";
        protected override string DefaultFolder=>"Assets/ZTracker";
        protected override bool AutoInsertTagsSection=>false;
        protected override void InitializeNewAsset(ZTrackerSong item){SeedSong(item);item.model.name=item.name;}
        protected override void InitializeDuplicate(ZTrackerSong copy){if(copy.schemaVersion==1&&copy.model!=null){copy.model.id=Id();copy.model.name=copy.name;}}
        // One name for a song: its title follows a rename of its file (an Undo step; the file rename itself is file work).
        protected override void OnRenamed(ZTrackerSong item){if(item==song&&Data!=null&&Data.name!=item.name)SongEdit("song name",()=>Data.name=item.name);}
        protected override void OnAssetChanged(){StopPreview();order=row=track=sub=0;instrument=null;Upgrade();}
        // Saving, importing or reimporting any asset raises projectChanged; only the browser needs that rescan.
        protected override void OnProjectChanged(){if(song==null||IsBrowsing)base.OnProjectChanged();}
        // The compact ZTracker skin on the whole window, with or without a song open (the library looks the same as the editor).
        void ApplyTrackerSkin()
        {
            var window=rootVisualElement;
            window.AddToClassList("tracker-compact");
            minSize=new Vector2(760,440);
            var path=AssetDatabase.FindAssets("ZTracker t:StyleSheet").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p=>p.EndsWith("/ZTracker.uss"));
            if(path!=null){var sheet=AssetDatabase.LoadAssetAtPath<StyleSheet>(path);if(!window.styleSheets.Contains(sheet))window.styleSheets.Add(sheet);}
        }
        protected override void BuildToolbarExtras(VisualElement row)
        {
            ApplyTrackerSkin();
            // Save, Undo and Redo wrap together on a narrow window.
            var edit=Z.Row(Button("Save","Save this song and its edited instrument.",Save,"save"),Named(Z.IconButton("arrow-counter-clockwise","Undo the last authored change.",Undo.PerformUndo),"undo"),Named(Z.IconButton("arrow-clockwise","Redo the last undone change.",Undo.PerformRedo),"redo"));
            edit.style.flexShrink=0;row.Add(edit);
            row.Add(BuildMidiControls());
        }
        protected override void BuildAsset(VisualElement root,ZTrackerSong edited)
        {
            var window=rootVisualElement;
            window.UnregisterCallback<KeyDownEvent>(PatternShortcutFromControls,TrickleDown.TrickleDown);
            window.RegisterCallback<KeyDownEvent>(PatternShortcutFromControls,TrickleDown.TrickleDown);
            WireInstrumentKeyboard(window);
            ApplyTrackerSkin();
            root.style.flexGrow=1;root.style.minHeight=0;
            status=Z.Text("Idle",tooltip:"Live transport and selected track state.");status.name="transport-status";status.style.flexGrow=1;status.style.minWidth=280;status.style.height=24;status.style.whiteSpace=WhiteSpace.NoWrap;status.style.overflow=Overflow.Hidden;status.style.textOverflow=TextOverflow.Ellipsis;
            root.Add(Flow(Named(Z.IconButton("play","Start the song from its first row. Space toggles play/stop.",()=>Play(false)),"play"),Named(Z.IconButton("play-circle","Start at the selected sequence slot and row.",()=>Play(true)),"play-cursor"),Named(Z.IconButton("stop","Stop preview and release its owned audio buffers.",StopPreview),"stop"),Z.Toggle("Follow","Scroll the pattern to each row reached by playback.",follow,v=>follow=v),status));
            root.Add(Z.Segmented(pane,new[]{"Pattern","Mixer","Automation","Instrument","Song"},"Choose the authoring workspace.",v=>{pane=v;BuildPane();if(pane==3)rootVisualElement.Focus();}));
            controlScroll=new ScrollView { name="workspace-sidebar" };controls=new VisualElement();controlScroll.Add(controls);
            stage=new VisualElement();stage.style.flexGrow=1;stage.style.minWidth=0;stage.style.minHeight=0;stage.name="tracker-stage";var workspace=Z.Split("tracker.model.workspace.compact",188,controlScroll,stage);workspace.style.flexGrow=1;workspace.style.minHeight=0;root.Add(workspace);
            help=Z.Text("",tooltip:"Meaning of the command under the pattern cursor.");help.name="command-help";help.AddToClassList("tracker-status-line");root.Add(help);
            BuildPane();pumpSchedule=root.schedule.Execute(Pump).Every(30);
        }
        void Upgrade()
        {
            if(song==null)return;
            lastError=ZTrackerMigration.VersionError(song.schemaVersion);
        }
        InstrumentData ReadInstrument(ZTrackerInstrument value)
        {
            if(value==null)return null;
            if(value.schemaVersion==1)return value.model;
            if(value.schemaVersion!=0)return null;
            if(!legacyInstrumentViews.TryGetValue(value,out var view)){view=ZTrackerMigration.Convert(ZTrackerMigration.Capture(value),value.name);legacyInstrumentViews.Add(value,view);}
            return view;
        }
        void UpgradeForEdit(UnityEngine.Object target)
        {
            if(target is ZTrackerSong s)
            {
                var view=s==song?Data:null;
                if(!ZTrackerMigration.Upgrade(s,out var error))throw new InvalidOperationException(error);
                if(view!=null&&s==legacyViewSource)s.model=view;
            }
            if(target is ZTrackerInstrument i)
            {
                var view=ReadInstrument(i);
                bool legacy=i.schemaVersion==0;
                if(!ZTrackerMigration.Upgrade(i,out var error))throw new InvalidOperationException(error);
                if(legacy&&view!=null)i.model=view;
            }
        }
        void BuildPane()
        {
            if(pane!=3||keyboardInstrument!=instrument)ReleaseInstrumentKeys();
            if(controls==null)return;CaptureWorkspace();var offset=controlScroll.scrollOffset;controls.Clear();stage.Clear();
            if(Data==null){displayedWorkspace=null;controls.Add(Button("New song","Create the first song.",CreateSong));RefreshTransport();return;}
            order=Mathf.Clamp(order,0,Math.Max(0,Data.sequence.Count-1));track=Mathf.Clamp(track,0,Math.Max(0,Data.tracks.Count-1));
            if(pane==0){BuildSongControls(controls);BuildPattern();}
            else if(pane==1)BuildMixer(stage);
            else if(pane==2)BuildAutomation(stage);
            else if(pane==3){BuildInstrumentSelector(controls);BuildInstrument(stage);}
            else BuildSongSettings(AuthoringScroll(stage,"song"));
            controlScroll.schedule.Execute(()=>controlScroll.scrollOffset=offset);RestoreWorkspace();RefreshTransport();
        }
        partial void BuildMixer(VisualElement root);
        partial void BuildAutomation(VisualElement root);
        partial void BuildInstrument(VisualElement root);
        void Change(UnityEngine.Object target,string label,Action edit,bool rebuild,TrackerCommand? command=null,bool live=true)
        {
            if(target==null)return;
            // Complete snapshots restore sparse removals, cropped rows and nested serialized values.
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Tracker: "+label);
            try{CompleteUndo(target,"Tracker: "+label);edit();EditorUtility.SetDirty(target);CollapseUndo(group,target);if(command.HasValue&&host!=null){if(!host.SendCommand(command.Value))lastError="Live update pending: command was not accepted";}else if(live)RefreshLive(target as ZTrackerInstrument);if(rebuild)BuildPane();else RefreshCells();}
            catch(Exception ex){Undo.RevertAllDownToGroup(group);lastError=ex.Message;BuildPane();}
            RefreshTransport();
        }
        void SongEdit(string label,Action edit,bool rebuild=false)=>Change(song,label,edit,rebuild);
        void InstrumentEdit(string label,Action edit,bool rebuild=false,bool liveScalar=false)=>Change(instrument,label,edit,rebuild);
        // Publish authored defaults as well as the audible change. A command-only edit leaves
        // the prepared base stale, so PreserveSwap would mistake Undo for an unrelated override.
        void InstrumentScalarEdit(string label,TrackerParameter parameter,float value,Action edit,bool rebuild=false)=>Change(instrument,label,edit,rebuild);
        void InstrumentMacroEdit(string label,int macro,float value,Action edit,bool rebuild=false)=>Change(instrument,label,edit,rebuild);
        void RefreshLive(ZTrackerInstrument changedInstrument=null)
        {
            RefreshMidiPreview(changedInstrument);
            if(host==null||Data==null)return;
            TrackerPreparedSong next=null;
            PlaybackSnapshot candidate=null;
            try{candidate=playbackSnapshot.CreateRefresh(changedInstrument);next=TrackerPreparedSong.Prepare(candidate.Data,AudioSettings.outputSampleRate);if(host.RefreshPrepared(next,out var reason)){next=null;var previous=playbackSnapshot;playbackSnapshot=candidate;candidate=null;previous.Dispose();lastError=null;pendingLive=false;}else {lastError=reason;pendingLive=reason!=null&&reason.Contains("still starting");}}
            catch(Exception ex){lastError="Live update pending: "+ex.Message;}
            finally{candidate?.Dispose();next?.Dispose();}
        }
        bool SendLive(TrackerCommand command)=>host!=null&&host.SendCommand(command);
        void Play(bool cursor)
        {
            if(ZTrackerPlayback.Current!=null&&ZTrackerPlayback.Current.IsPlaying&&ZTrackerPlayback.Current.SourceSong==song)return;
            try{EnsurePreview();host.SendCommand(TrackerCommand.Play());if(cursor)host.SendCommand(new TrackerCommand{kind=TrackerCommandKind.Seek,a=order,b=row});playing=true;lastError=null;}
            catch(Exception ex){lastError=ex.Message;}RefreshTransport();
        }
        void EnsurePreview()
        {
            // A device/buffer change closes the old stream before Unity replaces its audio graph.
            // A retained component is not proof that its renderer can still accept notes.
            if(host!=null&&(host.LiveInput==null||!host.LiveInput.IsOpen))StopPreview();
            if(host!=null)return;if(Data==null)throw new InvalidOperationException("Choose or create a song");
            PlaybackSnapshot candidate=null;TrackerPreparedSong prepared=null;
            candidate=PlaybackSnapshot.Create(song,null,-1);
            try{prepared=TrackerPreparedSong.Prepare(candidate.Data,AudioSettings.outputSampleRate);}
            catch{candidate.Dispose();throw;}
            try{preview=new GameObject("ZTracker model preview"){hideFlags=HideFlags.HideAndDontSave};host=preview.AddComponent<TrackerSapGenerator>();host.Configure(prepared,false);prepared=null;var source=preview.GetComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;source.generator=host;source.Play();}
            catch{if(preview!=null)DestroyImmediate(preview);host=null;throw;}
            finally{prepared?.Dispose();if(host!=null){playbackSnapshot=candidate;}else candidate.Dispose();}
        }
        void Audition(int pitch)
        {
            try{EnsurePreview();if(entryInstrument>=0&&entryInstrument<Data.instruments.Count)host.SendCommand(TrackerCommand.Audition(entryInstrument,pitch,127,FirstSequencer()));}
            catch(Exception ex){lastError=ex.Message;}RefreshTransport();
        }
        int FirstSequencer()=>SelectedTrack!=null&&SelectedTrack.kind==TrackKind.Sequencer?track:Data.tracks.FindIndex(t=>t.kind==TrackKind.Sequencer);
        void AuditionOff(){if(host!=null)host.SendCommand(new TrackerCommand{kind=TrackerCommandKind.AuditionOff,a=FirstSequencer(),b=0});}
        void StopPreview(){playing=false;pendingLive=false;if(preview!=null)DestroyImmediate(preview);preview=null;host=null;playbackSnapshot?.Dispose();playbackSnapshot=null;lastError=null;RefreshTransport();}
        void Pump()
        {
            if(host!=null){host.PollRetirement();if(pendingLive&&host.RenderedBlocks>0)RefreshLive();while(host.ReadEvent(out var e)){if(e.kind==TrackerEventKind.Stopped)playing=false;if(e.kind==TrackerEventKind.Row&&follow&&playing){bool changed=e.sequence!=order;order=e.sequence;if(changed&&pane==0){BuildPane();}HighlightPlaying(e.row);}}
            }MidiPump();RefreshTransport();
        }
        void RefreshTransport(){RefreshLiveReadout();}
        protected override void OnEnable(){if(asset==null&&legacySong!=null)asset=legacySong;legacySong=null;if(asset!=null)viewRestored=true;base.OnEnable();EditorApplication.quitting-=OnViewQuitting;EditorApplication.quitting+=OnViewQuitting;Undo.undoRedoPerformed-=OnUndo;Undo.undoRedoPerformed+=OnUndo;Upgrade();}
        void OnUndo(){legacyView=null;legacyViewSource=null;legacyInstrumentViews.Clear();RefreshLive();BuildPane();}
        protected override void OnDisable(){EditorApplication.quitting-=OnViewQuitting;SaveViewState();LauAssetGridGUI.ClearCache(chipThumbs);CaptureWorkspace();ReleaseInstrumentKeys();pumpSchedule?.Pause();pumpSchedule=null;Undo.undoRedoPerformed-=OnUndo;DisposeDrumPreview();MidiShutdown();StopPreview();if(patternFont!=null)DestroyImmediate(patternFont);patternFont=null;base.OnDisable();}
        // ZuiWindow may rebuild on domain reload/view changes; it must not stop a healthy graph.
        protected override void OnBeforeRebuild(){CaptureWorkspace();displayedWorkspace=null;pumpSchedule?.Pause();pumpSchedule=null;}
        void Save(){if(song!=null)AssetDatabase.SaveAssetIfDirty(song);if(Data!=null)foreach(var linked in Data.instruments.Where(i=>i!=null).Distinct())AssetDatabase.SaveAssetIfDirty(linked);if(instrument!=null)AssetDatabase.SaveAssetIfDirty(instrument);}
        static string Id()=>Guid.NewGuid().ToString("N");
        static ColumnVisibility Visibility(TrackData t,int column){while(t.columns.Count<=column)t.columns.Add(new ColumnVisibility());return t.columns[column];}
        static void Folder(){if(!AssetDatabase.IsValidFolder("Assets/ZTracker"))AssetDatabase.CreateFolder("Assets","ZTracker");}
        void CreateSong()
        {
            StopPreview();Folder();var created=CreateInstance<ZTrackerSong>();SeedSong(created);
            AssetDatabase.CreateAsset(created,AssetDatabase.GenerateUniqueAssetPath("Assets/ZTracker/Song.asset"));Undo.RegisterCreatedObjectUndo(created,"Tracker: create song");AssetDatabase.SaveAssetIfDirty(created);pane=0;SetAsset(created);
        }
        /// <summary>A new song: version 1, one note track and the master, one empty pattern in the sequence.</summary>
        internal static void SeedSong(ZTrackerSong target)
        {
            target.schemaVersion=1;target.model=new SongData{id=Id(),name="Untitled"};
            target.model.tracks.Add(new TrackData{id=Id(),name="Track 1",columns=new List<ColumnVisibility>{new ColumnVisibility{volume=true,pan=true,delay=true,sampleFx=true}}});target.model.tracks.Add(new TrackData{id=Id(),name="Master",kind=TrackKind.Master,visibleNoteColumns=0});
            var pattern=NewPattern(target.model,"Pattern 1");target.model.patterns.Add(pattern);target.model.sequence.Add(new SequenceSlot{id=Id(),patternId=pattern.id});
        }
        PatternData NewPattern(string name)=>NewPattern(Data,name);
        static PatternData NewPattern(SongData data,string name){var p=new PatternData{id=Id(),name=name};foreach(var t in data.tracks)p.tracks.Add(new PatternTrack{trackId=t.id});return p;}
        void BuildSongControls(VisualElement root)
        {
            BuildPatternSidebar(root);
        }
        void BuildSongSettings(VisualElement root)
        {
            root.Add(Flow(Z.Field("Name","Song title.",Z.TextInput(Data.name,"Rename the song.",v=>SongEdit("song name",()=>Data.name=v),175)),SongTags()));
            root.Add(Flow(CommandMenu(DialSong("BPM",(float)Data.bpm,32,999,"Song tempo in beats per minute. Right-click to give it a command id.",v=>Data.bpm=v,decimals:2,defaultValue:(float)new SongData().bpm),SongTempoTarget),DialSong("LPB",Data.linesPerBeat,1,256,"Lines per beat.",v=>Data.linesPerBeat=(int)v,defaultValue:new SongData().linesPerBeat),Z.ScrubNumber("TPL",Data.ticksPerLine,1,16,"Effect ticks per line.",v=>SongEdit("ticks per line",()=>Data.ticksPerLine=v),85,6)));
            root.Add(Flow(Z.Field("Voices","Maximum simultaneous voices.",Z.Int(Data.voiceCapacity,"Voice pool capacity; changing this takes effect on next Play.",v=>SongEdit("voice capacity",()=>Data.voiceCapacity=Math.Max(1,v)),85)),Z.Field("Seed","Deterministic random seed.",Z.TextInput(Data.seed.ToString(),"Unsigned 64-bit random seed.",v=>{if(ulong.TryParse(v,out var seed))SongEdit("random seed",()=>Data.seed=seed);},110)),Z.Toggle("Beat events","Emit song beat events.",Data.beatTicks,v=>SongEdit("song beat events",()=>Data.beatTicks=v,true)),Data.beatTicks?DialSong("Beat interval",Data.beatIntervalLines,1,512,"Lines between song beat events.",v=>Data.beatIntervalLines=(int)v,defaultValue:new SongData().beatIntervalLines):null));
            BuildSlotMutes(root);
            BuildCommandList(root);
        }
        void BuildPatternSidebar(VisualElement root)
        {
            var sequence=Z.BoxKeyed("Sequence","Drag slots to change their order.","tracker.model.sequence");var slots=Flow();var slotOrder=new ZuiReorderList(ZuiReorderLayout.Wrap,(a,z)=>SongEdit("reorder sequence",()=>Move(Data.sequence,a,z),true));
            for(int i=0;i<Data.sequence.Count;i++){int at=i;var slot=Data.sequence[i];var p=Data.patterns.Find(x=>x.id==slot.patternId);var b=Button($"{i:00} {p?.name}","Select this slot; drag to another slot to reorder.",()=>{order=at;row=0;BuildPane();});b.EnableInClassList("tracker-picked",i==order);slotOrder.Add(b);slots.Add(b);}sequence.Add(slots);
            sequence.Add(Flow(Button("Append","Append another use of the selected pattern.",()=>SongEdit("append slot",()=>Data.sequence.Add(new SequenceSlot{id=Id(),patternId=Pattern.id}),true)),Button("Remove","Remove the selected sequence slot while keeping its pattern.",()=>{if(Data.sequence.Count>1)SongEdit("remove slot",()=>Data.sequence.RemoveAt(order),true);})));root.Add(sequence);
            BuildInstrumentSelector(root);
            var patterns=Z.BoxKeyed("Pattern","Choose or create patterns independently of their sequence slots.","tracker.model.pattern");
            if(Pattern!=null){patterns.Add(Z.MiniRadio(Data.patterns.IndexOf(Pattern),Data.patterns.Select(p=>p.name).ToArray(),"Choose the pattern used in this sequence slot.",v=>SongEdit("assign pattern",()=>Data.sequence[order].patternId=Data.patterns[v].id,true),wrap:true));patterns.Add(Z.TextInput(Pattern.name,"Rename this pattern.",v=>SongEdit("pattern name",()=>Pattern.name=v),160));
            patterns.Add(Flow(Button("New pattern","Create and append an empty pattern.",()=>SongEdit("new pattern",()=>{var p=NewPattern("Pattern "+(Data.patterns.Count+1));Data.patterns.Add(p);Data.sequence.Add(new SequenceSlot{id=Id(),patternId=p.id});order=Data.sequence.Count-1;row=0;},true)),Button("Clone","Append a deep copy with its own identity.",()=>SongEdit("clone pattern",()=>{var p=ZTrackerMigration.Copy(Pattern);p.id=Id();p.name+=" copy";foreach(var t in p.tracks)foreach(var l in t.automation)l.id=Id();Data.patterns.Add(p);Data.sequence.Add(new SequenceSlot{id=Id(),patternId=p.id});order=Data.sequence.Count-1;},true))));}root.Add(patterns);
            if(SelectedTrack!=null){var t=SelectedTrack;var columns=Z.BoxKeyed("Columns","Choose visible columns without deleting authored cells.","tracker.model.columns");columns.Add(Z.MiniRadio(track,Data.tracks.Select(x=>x.name).ToArray(),"Choose a track for column display settings.",v=>{track=v;BuildPane();},wrap:true));
                if(t.kind==TrackKind.Sequencer)for(int c=0;c<t.visibleNoteColumns;c++){int at=c;var visibility=c<t.columns.Count?t.columns[c]:new ColumnVisibility();var flags=Flow(Z.Text((c+1).ToString(),tooltip:"Note column number."),Z.Toggle("V","Show volume and volume commands.",visibility.volume,v=>SongEdit("volume display",()=>Visibility(t,at).volume=v,true)),Z.Toggle("P","Show pan and pan commands.",visibility.pan,v=>SongEdit("pan display",()=>Visibility(t,at).pan=v,true)),Z.Toggle("D","Show fractional note delay.",visibility.delay,v=>SongEdit("delay display",()=>Visibility(t,at).delay=v,true)),Z.Toggle("FX","Show the column-local sample effect.",visibility.sampleFx,v=>SongEdit("local FX display",()=>Visibility(t,at).sampleFx=v,true)));flags.AddToClassList("tracker-visibility-toggles");columns.Add(flags);}root.Add(columns);}
        }
        void BuildSlotMutes(VisualElement root){if(Data.sequence.Count==0)return;var slot=Data.sequence[order];var box=Z.BoxKeyed("Sequence slot mutes","Suppress selected tracks only in the currently selected sequence occurrence.","tracker.sequence.mutes");var buttons=Flow();foreach(var t in Data.tracks){var item=t;buttons.Add(Named(Z.Toggle(t.name,"Mute this track in the selected sequence slot.",slot.mutedTrackIds.Contains(t.id),v=>SongEdit("sequence track mute",()=>{if(v){if(!slot.mutedTrackIds.Contains(item.id))slot.mutedTrackIds.Add(item.id);}else slot.mutedTrackIds.Remove(item.id);})) ,"slot-mute-"+t.id));}box.Add(buttons);root.Add(box);}
        void BuildInstrumentSelector(VisualElement root)
        {
            var box=Z.BoxKeyed("Instruments","Choose the instrument used by note entry and audition.","tracker.model.instruments");
            box.Add(Flow(Button("+ Instrument","Create and add a new instrument.",CreateInstrument,"new-instrument"),Button("+ Drumkit","Create a drum kit and open its key map.",CreateDrumKit,"new-drum-kit"),AddInstrumentButton()));
            if(Data.instruments.Count>0)box.Add(Named(Z.MiniRadio(entryInstrument,Data.instruments.Select((v,i)=>$"{i:X2} {InstrumentTitle(v)}").ToArray(),"Select an instrument; the preview button auditions it.",v=>{entryInstrument=v;instrument=Data.instruments[v];if(pane==3)BuildPane();},wrap:true),"instrument-selector"));
            if(Data.instruments.Count>0)box.Add(Flow(Z.IconButton("pencil","Open the selected instrument.",()=>{instrument=Data.instruments[Mathf.Clamp(entryInstrument,0,Data.instruments.Count-1)];pane=3;Rebuild();}),Z.IconButton("play","Audition the selected instrument at the current octave.",()=>Audition(octave*12)),Z.IconButton("stop","Release instrument audition notes.",AuditionOff)));root.Add(box);
        }
        static string InstrumentTitle(ZTrackerInstrument value)=>value==null?"Missing":value.model?.name??value.name;
        void RefreshInstrumentNames()
        {
            var picker=rootVisualElement.Q("instrument-selector");if(picker==null||Data==null)return;
            var buttons=picker.Query<Button>().ToList();for(int i=0;i<Math.Min(buttons.Count,Data.instruments.Count);i++)buttons[i].text=$"{i:X2} {InstrumentTitle(Data.instruments[i])}";
        }
        void CreateInstrument()
        {
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Folder();instrument=CreateInstance<ZTrackerInstrument>();instrument.schemaVersion=1;instrument.model=NewInstrumentData();AssetDatabase.CreateAsset(instrument,AssetDatabase.GenerateUniqueAssetPath("Assets/ZTracker/Instrument.asset"));Undo.RegisterCreatedObjectUndo(instrument,"Tracker: create instrument");SongEdit("add instrument",()=>Data.instruments.Add(instrument));Undo.CollapseUndoOperations(group);entryInstrument=Data.instruments.Count-1;pane=3;Rebuild();
        }
        VisualElement DialSong(string label,float value,float min,float max,string tip,Action<float> apply,bool rebuild=false,int decimals=0,float? defaultValue=null)=>Dial(label,value,min,max,tip,v=>SongEdit(label,()=>apply(v),rebuild),decimals,defaultValue:defaultValue);
        static VisualElement Dial(string label,float value,float min,float max,string tip,Action<float> changed,int decimals=2,float width=124,float? defaultValue=null)
            =>Z.MicroSlider(label,value,min,max,tip,changed,Math.Max(width,label.Length*6.5f+(decimals>0?50:35)),defaultValue:defaultValue,decimals:decimals);
        static VisualElement Flow(params VisualElement[] children){var r=Z.Row();r.AddToClassList("tracker-flow");r.style.flexWrap=Wrap.Wrap;r.style.height=StyleKeyword.Auto;r.style.minHeight=StyleKeyword.Auto;r.style.flexShrink=0;foreach(var c in children)if(c!=null)r.Add(c);return r;}
        static UnityEngine.UIElements.Button Button(string label,string tip,Action action,string name=null)
        {
            var button=Named(Z.CompactButton(label,tip,action),name);button.style.alignSelf=Align.FlexStart;button.style.flexGrow=0;button.style.flexShrink=0;return button;
        }
        static T Named<T>(T element,string name) where T:VisualElement{if(name!=null)element.name=name;return element;}
        static T Clone<T>(T value)=>ZTrackerMigration.Copy(value);
        void CompleteUndo(UnityEngine.Object target,string label)=>CompleteUndo(new[]{target},label);
        void CompleteUndo(UnityEngine.Object[] targets,string label)
        {
            targets=targets.Where(t=>t!=null).Distinct().ToArray();
            // Preflight the whole authoring transaction before recording or
            // migrating anything. Selection and transport never reach this path.
            foreach(var target in targets)
            {
                string error=null;
                if(target is ZTrackerSong s){error=ZTrackerMigration.VersionError(s.schemaVersion);if(error==null&&s.schemaVersion==0)error=ZTrackerModelValidation.Validate(ZTrackerMigration.Convert(ZTrackerMigration.Capture(s)));}
                if(target is ZTrackerInstrument ins){error=ZTrackerMigration.VersionError(ins.schemaVersion);if(error==null&&ins.schemaVersion==0)error=ZTrackerModelValidation.Validate(ZTrackerMigration.Convert(ZTrackerMigration.Capture(ins),ins.name));}
                if(error!=null)throw new InvalidOperationException(error);
            }
            // Unity's snapshot serialization fabricates inline null records on the
            // original object too. Restore them immediately, as the legacy transaction does.
            var paths=targets.Select(t=>ZTrackerMigration.NullPaths(t)).ToArray();
            Undo.RegisterCompleteObjectUndo(targets,label);
            for(int i=0;i<targets.Length;i++)ZTrackerMigration.RestoreNulls(targets[i],paths[i]);
            foreach(var target in targets)UpgradeForEdit(target);
        }
        static void CollapseUndo(int group,params UnityEngine.Object[] targets)
        {
            var paths=targets.Select(t=>ZTrackerMigration.NullPaths(t)).ToArray();
            Undo.CollapseUndoOperations(group);
            for(int i=0;i<targets.Length;i++)
            {
                ZTrackerMigration.RestoreNulls(targets[i],paths[i]);
                if(targets[i] is ZTrackerSong s){s.serializedNulls=ZTrackerMigration.NullPaths(s);s.serializedNulls.Remove(nameof(s.serializedNulls));}
                if(targets[i] is ZTrackerInstrument ins){ins.serializedNulls=ZTrackerMigration.NullPaths(ins);ins.serializedNulls.Remove(nameof(ins.serializedNulls));}
            }
        }
        // Imported unsupported records may carry null lists or neighbors. Filtering is a
        // view only; it must never fill those holes in the authored payload.
        static IEnumerable<T> Records<T>(IEnumerable<T> values) where T:class=>values?.Where(v=>v!=null)??Enumerable.Empty<T>();
        static void Move<T>(IList<T> list,int from,int to){var value=list[from];list.RemoveAt(from);list.Insert(to,value);}
    }
}
