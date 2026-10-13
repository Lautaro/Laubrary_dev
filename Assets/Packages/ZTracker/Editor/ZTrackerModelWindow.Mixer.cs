using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Audio;
using Laubrary.Audio.Editor;
using Laubrary.Zounds.Dsp;
using Laubrary.Zounds.Uitk;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        static readonly TrackData MixerTrackDefaults=new TrackData();
        static readonly SendDestination MixerSendDefaults=new SendDestination();
        [SerializeField] int mixerTab;
        readonly Dictionary<string,Vector2> authoringScroll=new Dictionary<string,Vector2>();
        ScrollView AuthoringScroll(VisualElement root,string key)
        {
            var scroll=new ScrollView { name="authoring-"+key };scroll.style.flexGrow=1;scroll.style.minHeight=0;root.Add(scroll);scroll.RegisterCallback<DetachFromPanelEvent>(_=>authoringScroll[key]=scroll.scrollOffset);if(authoringScroll.TryGetValue(key,out var offset))scroll.schedule.Execute(()=>scroll.scrollOffset=offset);return scroll;
        }
        partial void BuildMixer(VisualElement root)
        {
            BuildTrackSelector(controls);
            var scroll=AuthoringScroll(root,"mixer."+mixerTab);var t=SelectedTrack;if(t==null)return;
            scroll.Add(Flow(Named(Z.TextInput(t.name,"Track name.",v=>SongEdit("track name",()=>t.name=v),175),"track-name"),Z.Color(t.color,"Track colour.",v=>SongEdit("track colour",()=>t.color=v),90),Z.Text(t.kind.ToString(),tooltip:"Track role. The Master is the final output; other roles own their pattern records and devices.")));
            if(t.kind==TrackKind.Sequencer)scroll.Add(Named(Z.Segmented(t.drumTrack?1:0,new[]{"Notes","Drums"},"Show pitches alone or add percussion icons from the note's drum-kit mapping. Pitch and octave stay visible in either mode.",v=>SongEdit("track display",()=>t.drumTrack=v==1,true)),"track-note-display"));
            scroll.Add(Z.Segmented(mixerTab,new[]{"Levels","Devices","Sources"},"Choose this track's controls.",v=>{mixerTab=v;BuildPane();}));
            if(mixerTab==1)
            {
                string trackId = t.id;
                TrackData Owner() => Data.tracks.Find(item => item.id == trackId);
                var chainHost = new AudioDataChainEditorHost(
                    () => Owner().devices, (label, edit) => SongEdit(label, edit),
                    BeginMixerGesture, () => ChainGestureChanged(song), EndMixerGesture,
                    before => {
                        var owner = Owner();
                        foreach (var uid in before) if (!owner.devices.nodes.Any(n => n.uid == uid)) FlagRemovedDevice(owner, uid);
                        RepairChainReferences(owner, before);
                    });
                // Right-click an effect setting: give it a command id (or rename/remove the one it has).
                ChainEditorTK chainEditor = null;
                chainHost.ParamMenu = (uid, index, pd, item) =>
                {
                    var owner = Owner(); var node = owner?.devices.nodes.FirstOrDefault(n => n.uid == uid); var target = node != null ? EffectTarget(owner, node, index) : null;
                    if (target == null) return;
                    var anchor = (VisualElement)chainEditor?.Q("chain-param-" + owner.devices.nodes.IndexOf(node) + "-" + index) ?? chainEditor;
                    AddChainCommandItems(item, anchor, target);
                };
                chainEditor = new ChainEditorTK(chainHost);
                scroll.Add(chainEditor);
                return;
            }
            if(mixerTab==2){BuildSources(scroll,t);return;}
            scroll.Add(Flow(CommandMenu(Named(DialSong("Pre gain",t.preVolume,0,16,"Amplitude multiplier before effects. Right-click to give it a command id.",v=>t.preVolume=v,decimals:3,defaultValue:MixerTrackDefaults.preVolume),"mixer-pre-volume"),()=>TrackTarget(t,"preVolume")),CommandMenu(DialSong("Pre pan",t.prePan,-1,1,"Stereo balance before effects. Right-click to give it a command id.",v=>t.prePan=v,decimals:3,defaultValue:MixerTrackDefaults.prePan),()=>TrackTarget(t,"prePan")),CommandMenu(DialSong("Width",t.preWidth,0,4,"Stereo width before effects. Right-click to give it a command id.",v=>t.preWidth=v,decimals:3,defaultValue:MixerTrackDefaults.preWidth),()=>TrackTarget(t,"width"))));
            scroll.Add(Flow(CommandMenu(Named(DialSong("Post gain",t.postVolume,0,16,"Amplitude multiplier after effects. Right-click to give it a command id.",v=>t.postVolume=v,decimals:3,defaultValue:MixerTrackDefaults.postVolume),"mixer-post-volume"),()=>TrackTarget(t,"postVolume")),CommandMenu(DialSong("Post pan",t.postPan,-1,1,"Stereo balance after effects. Right-click to give it a command id.",v=>t.postPan=v,decimals:3,defaultValue:MixerTrackDefaults.postPan),()=>TrackTarget(t,"postPan"))));
            scroll.Add(Flow(Named(Z.Toggle("Output mute","Mute this track's output while its effects continue processing.",t.outputMute,v=>SongEdit("output mute",()=>t.outputMute=v)),"mixer-output-mute"),Z.Toggle("Trigger mute","Suppress new notes and track commands.",t.triggerMute,v=>SongEdit("trigger mute",()=>t.triggerMute=v)),Z.Toggle("Solo","Hear this track and its necessary routing buses.",t.solo,v=>SongEdit("solo",()=>t.solo=v))));
            if(t.kind!=TrackKind.Master)
            {
                scroll.Add(ReferencePicker("Group",t.parentGroupId,Data.tracks.Where(x=>x.kind==TrackKind.Group&&x.id!=t.id&&RouteAllowed(t,x.id,true)).ToList(),"No group","Choose the containing group. Cyclic nesting is excluded.",v=>SongEdit("group track",()=>t.parentGroupId=v,true)));
                scroll.Add(ReferencePicker("Output",t.outputTrackId,Data.tracks.Where(x=>(x.kind==TrackKind.Group||x.kind==TrackKind.Send||x.kind==TrackKind.Master)&&x.id!=t.id&&RouteAllowed(t,x.id,false)).ToList(),"Group / Master","Choose the output bus. Feedback routes are excluded.",v=>SongEdit("track output",()=>t.outputTrackId=v,true)));
            }
            var beat=Flow(Z.Toggle("Beat events","Emit beat events for this track.",t.beatTicks,v=>SongEdit("track beat events",()=>t.beatTicks=v,true)));
            if(t.beatTicks)beat.Add(Named(DialSong("Interval",t.beatIntervalLines,1,512,"Lines between this track's beat events.",v=>t.beatIntervalLines=(int)v,defaultValue:MixerTrackDefaults.beatIntervalLines),"mixer-beat-interval"));
            scroll.Add(beat);
            var sends=Z.BoxKeyed("Sends","Tap this track before or after its fader into a Send bus.","tracker.mixer.sends");
            var destinations=Data.tracks.Where(x=>x.kind==TrackKind.Send&&x.id!=t.id&&SendAllowed(t,x.id)).ToList();
            sends.Add(Named(Button("Add send","Add a send to an available Send bus.",()=>{if(destinations.Count>0)SongEdit("add send",()=>t.sends.Add(new SendDestination{trackId=destinations[0].id,devicePosition=t.devices.nodes.Count}),true);},"add-send"),"add-send"));
            if(destinations.Count==0)sends.Add(Button("New Send bus","Create a destination bus for sends.",()=>AddTrack(TrackKind.Send),"new-send-bus"));
            foreach(var s in t.sends.ToArray())
            {
                var options=Data.tracks.Where(x=>x.kind==TrackKind.Send&&x.id!=t.id&&SendAllowed(t,x.id,s)).ToList();sends.Add(ReferencePicker("Destination",s.trackId,options,"None","Choose the Send bus.",v=>{if(v!="")SongEdit("send destination",()=>s.trackId=v,true);}));
                sends.Add(Flow(DialSong("Gain",s.gain,0,16,"Send amplitude multiplier.",v=>s.gain=v,decimals:3,defaultValue:MixerSendDefaults.gain),DialSong("After FX",s.devicePosition,0,t.devices.nodes.Count,"Zero taps before the first effect; each following position taps after that effect.",v=>s.devicePosition=(int)v,true,defaultValue:MixerSendDefaults.devicePosition),Z.Toggle("Post fader","Take the send after the track fader.",s.postfader,v=>SongEdit("send tap",()=>s.postfader=v,true)),Z.IconButton("trash","Remove this send.",()=>SongEdit("remove send",()=>t.sends.Remove(s),true))));
            }
            scroll.Add(sends);
        }
        void BuildTrackSelector(VisualElement root)
        {
            VisualElement add=null;add=Button("Add track…","Choose a track role to add.",()=>{var menu=Z.Menu(add);menu.Item("Notes","Add a sequencer track for pitched notes.",()=>AddTrack(TrackKind.Sequencer));menu.Item("Drums","Add a sequencer track displayed as percussion.",()=>AddTrack(TrackKind.Sequencer,true));menu.Item("Group","Add a group bus.",()=>AddTrack(TrackKind.Group));menu.Item("Send","Add a send bus.",()=>AddTrack(TrackKind.Send));menu.Item("Event","Add an event track.",()=>AddTrack(TrackKind.Event));if(!Data.tracks.Any(t=>t.kind==TrackKind.Master))menu.Item("Master","Add the song's output track.",()=>AddTrack(TrackKind.Master));menu.Show();},"add-track");root.Add(Flow(add,Button("Clone","Clone this track and all its pattern records.",CloneTrack,"clone-track"),Button("Remove","Remove this track, clear routes and flag references; Undo restores everything.",RemoveTrack,"remove-track")));
            var list=Z.BoxKeyed("Tracks","Select a track; drag a row up or down to reorder it.","tracker.mixer.tracks");var rows=new ZuiReorderList(ZuiReorderLayout.Vertical,ReorderTracks);for(int i=0;i<Data.tracks.Count;i++){int at=i;var t=Data.tracks[i];var b=Named(Z.Text(t.name,tooltip:"Select "+t.kind+" track; drag up or down to reorder."),"track-select-"+i);b.AddToClassList("tracker-track-row");b.focusable=true;b.style.width=Length.Percent(100);b.style.paddingLeft=6;b.style.borderLeftWidth=4;b.style.borderLeftColor=t.color;b.style.backgroundColor=at==track?new Color(.18f,.42f,.62f):new Color(.14f,.14f,.14f);b.style.color=Color.white;b.EnableInClassList("tracker-picked",at==track);b.RegisterCallback<PointerDownEvent>(e=>{if(e.button==0)track=at;});b.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Return||e.keyCode==KeyCode.KeypadEnter){track=at;BuildPane();e.StopPropagation();}});rows.Add(b,onClick:()=>{track=at;BuildPane();});list.Add(b);}root.Add(list);
        }
        void AddTrack(TrackKind kind,bool drums=false)
        {
            if(kind==TrackKind.Master&&Data.tracks.Any(x=>x.kind==TrackKind.Master)){lastError="A song has exactly one Master";RefreshTransport();return;}
            SongEdit("add track",()=>{var t=new TrackData{id=Id(),name=(drums?"Drums":kind.ToString())+" "+(Data.tracks.Count+1),kind=kind,drumTrack=drums,visibleNoteColumns=kind==TrackKind.Sequencer?1:0};Data.tracks.Add(t);foreach(var p in Data.patterns)p.tracks.Add(new PatternTrack{trackId=t.id});track=Data.tracks.Count-1;},true);
        }
        void CloneTrack()
        {
            var src=SelectedTrack;if(src==null||src.kind==TrackKind.Master)return;
            SongEdit("clone track",()=>
            {
                var copy=Clone(src);copy.id=Id();copy.name+=" copy";var remap=new Dictionary<string,string>();
                foreach(var n in Records(copy.devices?.nodes)){string old=n.uid;n.uid=Id();if(!string.IsNullOrEmpty(old))remap[old]=n.uid;}
                foreach(var m in Records(copy.devices?.modifiers))m.uid=Id();
                foreach(var s in Records(copy.sourceDevices)){string old=s.id;if(old==null)continue;if(!remap.TryGetValue(old,out var nid)){nid=Id();remap[old]=nid;}s.id=nid;foreach(var p in Records(s.parameters)){if(p.target==null)continue;if(p.target.trackId==src.id)p.target.trackId=copy.id;if(p.target.deviceId!=null&&remap.TryGetValue(p.target.deviceId,out var id))p.target.deviceId=id;}}
                foreach(var e in Records(copy.externalSources)){if(e.id==null)continue;if(!remap.TryGetValue(e.id,out var id)){id=Id();remap[e.id]=id;}e.id=id;}
                Data.tracks.Insert(track+1,copy);
                foreach(var pattern in Data.patterns){var original=pattern.tracks.Find(p=>p.trackId==src.id);var pt=original!=null?Clone(original):new PatternTrack();pt.trackId=copy.id;foreach(var l in Records(pt.automation)){l.id=Id();if(l.target==null)continue;if(l.target.trackId==src.id)l.target.trackId=copy.id;if(l.target.deviceId!=null&&remap.TryGetValue(l.target.deviceId,out var id))l.target.deviceId=id;}pattern.tracks.Add(pt);}track++;
            },true);
        }
        void RemoveTrack()
        {
            var t=SelectedTrack;if(t==null||t.kind==TrackKind.Master)return;SongEdit("remove track",()=>{Data.tracks.Remove(t);foreach(var other in Data.tracks){if(other.parentGroupId==t.id)other.parentGroupId=t.parentGroupId;if(other.outputTrackId==t.id)other.outputTrackId="";other.sends?.RemoveAll(s=>s!=null&&s.trackId==t.id);}foreach(var p in Data.patterns){p.tracks.RemoveAll(pt=>pt.trackId==t.id);foreach(var pt in p.tracks)foreach(var l in Records(pt.automation))if(l.target?.trackId==t.id){l.unsupported=true;l.diagnostic="Target track removed";l.target.unresolved=true;}}foreach(var s in Data.sequence)s.mutedTrackIds?.Remove(t.id);track=Math.Max(0,track-1);},true);
        }
        bool RouteAllowed(TrackData t,string dest,bool group)
        {
            var copy=Clone(Data);var candidate=copy.tracks.Find(x=>x.id==t.id);if(group)candidate.parentGroupId=dest;else candidate.outputTrackId=dest;var error=ZTrackerModelValidation.Validate(copy);return error==null;
        }
        bool SendAllowed(TrackData t,string dest,SendDestination existing=null)
        {
            var copy=Clone(Data);var candidate=copy.tracks.Find(x=>x.id==t.id);if(existing==null)candidate.sends.Add(new SendDestination{trackId=dest});else candidate.sends[t.sends.IndexOf(existing)].trackId=dest;return ZTrackerModelValidation.Validate(copy)==null;
        }
        static VisualElement ReferencePicker(string label,string id,List<TrackData> values,string none,string tip,Action<string> changed)
        {
            var ids=new List<string>{""};ids.AddRange(values.Select(x=>x.id));var names=new List<string>{none};names.AddRange(values.Select(x=>x.name));if(id!=""&&!ids.Contains(id)){ids.Add(id);names.Add("Unavailable destination");}return Z.Field(label,tip,Z.MiniRadio(Math.Max(0,ids.IndexOf(id)),names.ToArray(),tip,v=>changed(ids[v]),wrap:true));
        }
        void FlagRemovedDevice(TrackData t,string uid)
        {
            foreach(var p in Data.patterns)foreach(var pt in p.tracks)foreach(var lane in Records(pt.automation))if(lane.target!=null&&(lane.target.trackId==t.id||lane.target.trackId==""&&pt.trackId==t.id)&&lane.target.deviceId==uid){lane.unsupported=true;lane.target.unresolved=true;lane.diagnostic="Target effect removed";}
            foreach(var s in Records(t.sourceDevices)){if(s.id==uid)s.kind=SourceDeviceKind.Unsupported;foreach(var p in Records(s.parameters))if(p.target?.deviceId==uid)p.target.unresolved=true;}
        }
        void RepairChainReferences(TrackData t,AudioEffectNodeData[] before)
            => RepairChainReferences(t, before.Select(n => n.uid).ToArray());

        void RepairChainReferences(TrackData t,string[] before)
        {
            foreach(var send in Records(t.sends)){if(send.devicePosition>0&&send.devicePosition<=before.Length){int n=t.devices.nodes.FindIndex(node=>node.uid==before[send.devicePosition-1]);send.devicePosition=n>=0?n+1:Math.Min(send.devicePosition,t.devices.nodes.Count);}}
            foreach(var source in Records(t.sourceDevices).Where(s=>s.kind==SourceDeviceKind.AudioChain))foreach(var p in Records(source.parameters)){if(p.target==null)continue;var node=Records(t.devices.nodes).FirstOrDefault(n=>n.uid==p.target.deviceId);if(node?.p!=null&&p.target.index>=0&&p.target.index<node.p.Length)p.defaultValue=Mathf.InverseLerp(p.min,p.max,node.p[p.target.index]);}
            // Source ordinals belong to the authored command profile, not mutable native node order.
        }

        int mixerGestureGroup = -1;
        double nextChainPreview;
        void BeginMixerGesture(string label)
        {
            if (mixerGestureGroup >= 0) return;
            Undo.IncrementCurrentGroup(); mixerGestureGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Tracker: " + label); CompleteUndo(song, "Tracker: " + label);
        }
        void ChainGestureChanged(UnityEngine.Object target)
        {
            EditorUtility.SetDirty(target);
            if (EditorApplication.timeSinceStartup < nextChainPreview) return;
            nextChainPreview = EditorApplication.timeSinceStartup + .08;
            RefreshLive(target as ZTrackerInstrument);
        }
        void EndMixerGesture()
        {
            if (mixerGestureGroup < 0) return;
            EditorUtility.SetDirty(song);
            song.serializedNulls = ZTrackerMigration.NullPaths(song);
            song.serializedNulls.Remove(nameof(song.serializedNulls));
            mixerGestureGroup = -1;
            RefreshLive(); RefreshTransport();
        }
    }
}
