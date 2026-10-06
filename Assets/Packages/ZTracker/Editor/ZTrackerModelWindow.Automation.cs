using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        int automationLane, automationTarget, automationPoint;
        sealed class Destination
        {
            public string label;
            public ParameterTarget target;
            public Action declare;
        }
        List<Destination> AutomationDestinations(TrackData owner)
        {
            var list=new List<Destination>();
            foreach(var tr in Data.tracks)
            {
                foreach(var parameter in new[]{"preVolume","prePan","postVolume","postPan"})list.Add(new Destination{label=tr.name+" / "+parameter,target=new ParameterTarget{kind=ParameterKind.Mixer,trackId=tr.id,parameter=parameter,units="normalized"}});
                foreach(var node in Records(tr.devices?.nodes))
                {
                    var desc=ZoundEffectDescriptors.Get(node.type);if(desc==null)continue;
                    for(int p=0;p<desc.parameters.Length;p++)
                    {
                        int pi=p;var pd=desc.parameters[p];if(!pd.automatable||pd.IsChoice||pd.curve==ParamCurve.Toggle||pd.curve==ParamCurve.Integer||node.type==ZoundEffectType.Delay&&p==3)continue;
                        var target=new ParameterTarget{kind=ParameterKind.Device,trackId=tr.id,deviceId=node.uid,parameter=(p+1).ToString(),index=p,units=TrackerEffectMetadata.ParameterUnits(node.type,p)};
                        list.Add(new Destination{label=tr.name+" / "+desc.displayName+" "+(tr.devices.nodes.IndexOf(node)+1)+" / "+pd.name+" ("+pd.unit+")",target=target,declare=()=>{DeclareNativeSource(tr,node);target.deviceId=node.uid;}});
                    }
                }
                foreach(var source in Records(tr.sourceDevices).Where(s=>s.kind!=SourceDeviceKind.Unsupported))
                {
                    if(Records(tr.devices?.nodes).Any(n=>n.uid==source.id))continue;
                    var linked=Data.instruments.Where(i=>i!=null&&i.model?.id==source.instrumentId).ToList();
                    if(linked.Count!=1)continue;
                    foreach(var sp in Records(source.parameters)){if(source.kind==SourceDeviceKind.InstrumentAutomation&&!Records(linked[0].model.externalParameters).Any(e=>e.externalId==sp.externalId)||source.kind==SourceDeviceKind.InstrumentMacros&&(sp.ordinal<1||sp.ordinal>8))continue;list.Add(new Destination{label=tr.name+" / "+(string.IsNullOrEmpty(source.pluginId)?"Device "+source.ordinal:source.pluginId)+" / "+(string.IsNullOrEmpty(sp.externalId)?sp.ordinal.ToString():sp.externalId),target=new ParameterTarget{kind=ParameterKind.Device,trackId=tr.id,deviceId=source.id,parameter=sp.ordinal.ToString(),index=sp.ordinal-1,units="normalized"}});}
                }
            }
            foreach(var i in Data.instruments.Where(i=>i!=null&&i.model!=null))for(int m=0;m<Math.Min(8,i.model.macros?.Length??0);m++)if(i.model.macros[m]!=null)list.Add(new Destination{label=i.name+" / Macro "+(m+1)+" "+i.model.macros[m].name,target=new ParameterTarget{kind=ParameterKind.InstrumentMacro,trackId=owner.id,instrumentId=i.model.id,index=m,parameter="macro",units="normalized"}});
            return list;
        }
        void DeclareNativeSource(TrackData tr,Laubrary.Audio.AudioEffectNodeData node)
        {
            if(string.IsNullOrEmpty(node.uid))node.uid=Id();var source=Records(tr.sourceDevices).FirstOrDefault(s=>s.id==node.uid);
            if(source!=null)return;int ordinal=Enumerable.Range(1,34).FirstOrDefault(i=>!Records(tr.sourceDevices).Any(s=>s.ordinal==i)&&!Records(tr.externalSources).Any(s=>s.sourceOrdinal==i));if(ordinal==0)throw new InvalidOperationException("All command device ordinals are in use");
            source=new SourceDeviceData{id=node.uid,ordinal=ordinal,kind=SourceDeviceKind.AudioChain,pluginId=ZoundEffectDescriptors.Get(node.type).displayName};var ds=ZoundEffectDescriptors.Get(node.type).parameters;
            for(int p=0;p<ds.Length;p++){var d=ds[p];if(!d.automatable||d.IsChoice||d.curve==ParamCurve.Toggle||d.curve==ParamCurve.Integer||node.type==ZoundEffectType.Delay&&p==3)continue;float value=node.p!=null&&p<node.p.Length?node.p[p]:d.def;source.parameters.Add(new SourceParameterData{ordinal=p+1,parameter=d.name,min=d.min,max=d.max,defaultValue=Mathf.InverseLerp(d.min,d.max,value),units=TrackerEffectMetadata.ParameterUnits(node.type,p),explicitEquivalence=true,target=new ParameterTarget{kind=ParameterKind.Device,trackId=tr.id,deviceId=node.uid,index=p,parameter=(p+1).ToString(),units=TrackerEffectMetadata.ParameterUnits(node.type,p)}});}if(tr.sourceDevices==null)tr.sourceDevices=new List<SourceDeviceData>();tr.sourceDevices.Add(source);
        }
        partial void BuildAutomation(VisualElement root)
        {
            if(Pattern!=null){Button sequence=null;sequence=(Button)Button("Slot "+order+" / "+Pattern.name,"Choose the sequence occurrence whose pattern lanes to edit.",()=>{var menu=Z.Menu(sequence).Search();for(int s=0;s<Data.sequence.Count;s++){int at=s;var p=Data.patterns.Find(p=>p.id==Data.sequence[s].patternId);menu.Item(s+" / "+p?.name,"Edit this occurrence's pattern lanes.",()=>{order=at;automationLane=automationPoint=0;BuildPane();},@checked:s==order);}menu.Show();},"automation-sequence");sequence.style.width=225;controls.Add(sequence);}
            BuildTrackSelector(controls);if(Pattern==null||SelectedTrack==null)return;var owner=SelectedTrack;var pt=Pattern.tracks.Find(p=>p.trackId==owner.id);if(pt==null)return;
            var scroll=AuthoringScroll(root,"automation");var choices=AutomationDestinations(owner);automationTarget=Mathf.Clamp(automationTarget,0,Math.Max(0,choices.Count-1));
            var picker=Z.BoxKeyed("Destination","Choose a declared mixer, effect or instrument macro parameter.","tracker.automation.destination");
            if(choices.Count>0){Button destination=null;destination=(Button)Button(choices[automationTarget].label,"Choose a destination by track, device, parameter and units.",()=>{var menu=Z.Menu(destination).Search("Find a parameter…").Width(320);for(int i=0;i<choices.Count;i++){int at=i;menu.Item(choices[i].label,"Use this declared destination.",()=>{automationTarget=at;destination.text=choices[at].label;},@checked:i==automationTarget);}menu.Show();},"automation-target");destination.style.width=300;destination.style.maxWidth=Length.Percent(100);picker.Add(destination);}
            picker.Add(Button("Add lane","Create a lane for the selected destination.",()=>{if(choices.Count==0)return;var choice=choices[automationTarget];SongEdit("add automation lane",()=>{choice.declare?.Invoke();var target=Clone(choice.target);if(target.kind==ParameterKind.Device&&target.deviceId==""){var tr=Data.tracks.Find(t=>t.id==target.trackId);target.deviceId=Records(tr.devices?.nodes).First(n=>!string.IsNullOrEmpty(n.uid)).uid;}if(pt.automation==null)pt.automation=new List<AutomationLane>();pt.automation.Add(new AutomationLane{id=Id(),target=target,interpolation=AutomationInterpolation.Linear});automationLane=pt.automation.Count-1;automationPoint=0;},true);},"add-automation-lane"));scroll.Add(picker);
            if(pt.automation==null){scroll.Add(Z.Text("⚠ Lane metadata",tooltip:"The imported lane list is null and remains preserved. Adding a lane explicitly initializes it."));return;}
            if(pt.automation.Count==0)return;automationLane=Mathf.Clamp(automationLane,0,pt.automation.Count-1);
            scroll.Add(Named(Z.MiniRadio(automationLane,pt.automation.Select((l,i)=>i+" "+TargetName(l?.target)+(l==null||l.unsupported?" ⚠":"")).ToArray(),"Select a lane to edit.",v=>{automationLane=v;automationPoint=0;BuildPane();},wrap:true),"automation-lanes"));var lane=pt.automation[automationLane];
            if(lane==null){scroll.Add(Flow(Z.Text("⚠ Null lane",tooltip:"This imported null lane is retained unchanged."),Button("Remove lane","Remove this null lane record; Undo restores it.",()=>SongEdit("remove null lane",()=>pt.automation.RemoveAt(automationLane),true),"remove-automation-lane")));return;}
            scroll.Add(Flow(Named(Z.Toggle("On","Enable this lane.",lane.enabled,v=>SongEdit("automation enabled",()=>lane.enabled=v)),"automation-enabled"),Named(Z.Segmented((int)lane.interpolation,new[]{"Step","Linear"},"Hold each value or interpolate between neighbouring points.",v=>SongEdit("automation interpolation",()=>lane.interpolation=(AutomationInterpolation)v,true)),"automation-interpolation"),Button("Remove lane","Remove this lane and its points; Undo restores it.",()=>SongEdit("remove automation lane",()=>pt.automation.Remove(lane),true),"remove-automation-lane")));
            if(lane.unsupported||lane.target==null||lane.target.unresolved||lane.points==null||lane.points.Any(p=>p==null)){scroll.Add(Z.Text("⚠ Preserved lane",tooltip:lane.diagnostic+"\n"+lane.rawSource+"\nUnsupported or null target/point metadata remains unchanged."));return;}
            scroll.Add(Z.Text(TargetName(lane.target),tooltip:lane.target.units+"; lane values are normalized 0–1. Pan: 0 left, 0.5 centre, 1 right. Mixer gain: 0–1 linear amplitude. Effects: maps to the declared parameter range."));
            var canvas=new VisualElement{name="automation-canvas",tooltip="Double-click to add a point; drag an existing point to move it. Horizontal: line; vertical: normalized value."};canvas.style.height=200;canvas.style.minWidth=200;canvas.style.flexShrink=0;canvas.style.backgroundColor=new Color(.08f,.1f,.13f);scroll.Add(canvas);
            Vector2 Position(AutomationPoint p)=>new Vector2((float)(p.line/Pattern.lineCount)*canvas.contentRect.width,(1-p.value)*canvas.contentRect.height);
            AutomationPoint Nearest(Vector2 mouse)=>lane.points.OrderBy(p=>(Position(p)-mouse).sqrMagnitude).FirstOrDefault(p=>(Position(p)-mouse).sqrMagnitude<=100);
            void SetPoint(AutomationPoint point,Vector2 mouse){double line=Math.Round(Mathf.Clamp01(mouse.x/Mathf.Max(1,canvas.contentRect.width))*Pattern.lineCount*256)/256;float value=Mathf.Clamp01(1-mouse.y/Mathf.Max(1,canvas.contentRect.height));if(lane.points.Any(p=>p!=point&&p.line==line))return;point.line=line;point.value=value;lane.points.Sort((a,b)=>a.line.CompareTo(b.line));automationPoint=lane.points.IndexOf(point);}
            canvas.generateVisualContent+=ctx=>{var painter=ctx.painter2D;painter.lineWidth=1;painter.strokeColor=new Color(.3f,.3f,.35f);for(int x=0;x<=4;x++){float xx=canvas.contentRect.width*x/4;painter.BeginPath();painter.MoveTo(new Vector2(xx,0));painter.LineTo(new Vector2(xx,canvas.contentRect.height));painter.Stroke();}painter.strokeColor=new Color(.4f,.8f,1);painter.lineWidth=2;painter.BeginPath();for(int i=0;i<lane.points.Count;i++){var pos=Position(lane.points[i]);if(i==0)painter.MoveTo(pos);else{if(lane.interpolation==AutomationInterpolation.Step)painter.LineTo(new Vector2(pos.x,Position(lane.points[i-1]).y));painter.LineTo(pos);}}painter.Stroke();foreach(var point in lane.points){var pos=Position(point);painter.fillColor=lane.points.IndexOf(point)==automationPoint?Color.yellow:Color.cyan;painter.BeginPath();painter.Arc(pos,4,0,360);painter.Fill();}};
            AutomationPoint dragging=null;
            canvas.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;var mouse=e.localPosition;dragging=Nearest(mouse);if(dragging==null&&e.clickCount==2){SongEdit("add automation point",()=>{var p=new AutomationPoint();SetPoint(p,mouse);if(!lane.points.Any(x=>x.line==p.line)){lane.points.Add(p);lane.points.Sort((a,b)=>a.line.CompareTo(b.line));automationPoint=lane.points.IndexOf(p);}},true);return;}if(dragging!=null){automationPoint=lane.points.IndexOf(dragging);canvas.CapturePointer(e.pointerId);canvas.MarkDirtyRepaint();}});
            canvas.RegisterCallback<PointerMoveEvent>(e=>{if(dragging==null||!canvas.HasPointerCapture(e.pointerId))return;SongEdit("move automation point",()=>SetPoint(dragging,e.localPosition));canvas.MarkDirtyRepaint();});
            canvas.RegisterCallback<PointerUpEvent>(e=>{if(canvas.HasPointerCapture(e.pointerId))canvas.ReleasePointer(e.pointerId);if(dragging!=null){dragging=null;BuildPane();}});
            scroll.Add(Flow(Button("Add point","Add a point at the first unoccupied line.",()=>SongEdit("add automation point",()=>{double line=0;while(lane.points.Any(p=>p.line==line)&&line<=Pattern.lineCount)line++;if(line>Pattern.lineCount)return;var point=new AutomationPoint{line=line,value=.5f};lane.points.Add(point);lane.points.Sort((a,b)=>a.line.CompareTo(b.line));automationPoint=lane.points.IndexOf(point);},true),"add-automation-point"),Button("Delete point","Delete the selected point.",()=>{if(lane.points.Count>0)SongEdit("delete automation point",()=>lane.points.RemoveAt(Mathf.Clamp(automationPoint,0,lane.points.Count-1)),true);},"delete-automation-point")));
            if(lane.points.Count>0){automationPoint=Mathf.Clamp(automationPoint,0,lane.points.Count-1);scroll.Add(Z.MiniRadio(automationPoint,lane.points.Select(p=>p.line.ToString("0.###")).ToArray(),"Select a point by line.",v=>{automationPoint=v;BuildPane();},wrap:true));var point=lane.points[automationPoint];scroll.Add(Flow(Named(Z.Float((float)point.line,"Exact fractional line; quantized to 1/256 line.",v=>SongEdit("point line",()=>{double line=Math.Round(Mathf.Clamp(v,0,Pattern.lineCount)*256)/256;if(lane.points.Any(p=>p!=point&&p.line==line))throw new InvalidOperationException("Another point already occupies that line");point.line=line;lane.points.Sort((a,b)=>a.line.CompareTo(b.line));automationPoint=lane.points.IndexOf(point);},true),90),"automation-point-line"),Named(DialSong("Value",point.value,0,1,"Normalized destination value.",v=>{point.value=v;canvas.MarkDirtyRepaint();},decimals:4),"automation-point-value")));}
        }
        string TargetName(ParameterTarget target)
        {
            if(target==null)return "Preserved target";
            var t=Data.tracks.Find(x=>x.id==target.trackId);if(target.kind==ParameterKind.InstrumentMacro){var i=Data.instruments.Find(i=>i!=null&&i.model?.id==target.instrumentId);return(i?.name??"Missing instrument")+" / Macro "+(target.index+1);}
            if(target.kind==ParameterKind.Device){var n=Records(t?.devices?.nodes).FirstOrDefault(n=>n.uid==target.deviceId);var d=n!=null?ZoundEffectDescriptors.Get(n.type):null;return(t?.name??"Missing track")+" / "+(d?.displayName??Records(t?.sourceDevices).FirstOrDefault(s=>s.id==target.deviceId)?.pluginId??"Missing device")+" / "+(d!=null&&target.index>=0&&target.index<d.parameters.Length?d.parameters[target.index].name:target.parameter);}
            return(t?.name??"Owning track")+" / "+target.parameter;
        }
        void BuildSources(VisualElement root,TrackData t)
        {
            root.Add(Button("Declare effect devices","Expose unlisted effect parameters to literal device commands and automation.",()=>SongEdit("declare effect devices",()=>{foreach(var node in Records(t.devices?.nodes))DeclareNativeSource(t,node);},true),"declare-effect-devices"));
            root.Add(Button("Add source device","Declare a stand-in plugin source for instrument automation.",()=>SongEdit("add source device",()=>{int ordinal=Enumerable.Range(1,34).FirstOrDefault(n=>!Records(t.sourceDevices).Any(s=>s.ordinal==n)&&!Records(t.externalSources).Any(s=>s.sourceOrdinal==n));if(ordinal==0)throw new InvalidOperationException("All command device ordinals are in use");if(t.sourceDevices==null)t.sourceDevices=new List<SourceDeviceData>();t.sourceDevices.Add(new SourceDeviceData{id=Id(),ordinal=ordinal,kind=SourceDeviceKind.InstrumentAutomation,pluginId="Source "+ordinal});},true),"add-source-device"));
            foreach(var source in Records(t.sourceDevices).ToArray())
            {
                var box=Z.BoxKeyed("Device "+source.ordinal,"Command-profile source declaration; its ordinal stays stable when effects are reordered.","tracker.source."+source.id);
                box.Add(Flow(Z.TextInput(source.pluginId,"Source device name; declared once and shown in destination pickers.",v=>SongEdit("source name",()=>{source.pluginId=v;SyncExternalDeclaration(t,source);}),170),DialSong("Ordinal",source.ordinal,1,34,"Literal command device ordinal. Duplicate values are refused.",v=>{int n=(int)v;if(Records(t.sourceDevices).Any(s=>s!=source&&s.ordinal==n)||Records(t.externalSources).Any(s=>s.id!=source.id&&s.sourceOrdinal==n))throw new InvalidOperationException("Device ordinal already declared");source.ordinal=n;SyncExternalDeclaration(t,source);},true),Z.Toggle("On","Enable this declared source device.",source.enabled,v=>SongEdit("source enabled",()=>source.enabled=v)),Named(Z.IconButton("trash","Remove declaration; affected lanes are flagged.",()=>SongEdit("remove source declaration",()=>{t.sourceDevices.Remove(source);t.externalSources?.RemoveAll(e=>e!=null&&e.id==source.id);foreach(var pattern in Data.patterns)foreach(var pt in pattern.tracks)foreach(var lane in Records(pt.automation))if(lane.target?.deviceId==source.id){lane.unsupported=true;lane.diagnostic="Source device removed";}},true)),"remove-source-"+source.id)));
                if(source.kind==SourceDeviceKind.Unsupported){box.Add(Z.Text("⚠ Preserved source",tooltip:source.rawSource+"\nThis source kind is unsupported and remains unchanged."));root.Add(box);continue;}
                if(source.parameters==null){box.Add(Z.Text("⚠ Parameter metadata",tooltip:source.rawSource+"\nThe imported parameter list is null and remains unchanged."));root.Add(box);continue;}
                if(source.kind==SourceDeviceKind.AudioChain)
                {
                    foreach(var sp in Records(source.parameters)){if(sp.target==null){box.Add(Z.Text("⚠ Parameter target",tooltip:"Source slot "+sp.ordinal+" has a null imported target and remains preserved."));continue;}var node=Records(t.devices?.nodes).FirstOrDefault(n=>n.uid==sp.target.deviceId);var d=node!=null?ZoundEffectDescriptors.Get(node.type):null;var pd=d!=null&&sp.target.index>=0&&sp.target.index<d.parameters.Length?d.parameters[sp.target.index]:new ParamDesc("Unavailable",sp.units,sp.min,sp.max,0);
                        var parameters=Flow(Z.Text(pd.name,tooltip:"Declared source slot "+sp.ordinal+"; "+sp.units+"; destination "+(d?.displayName??"missing")),Z.Toggle("Equivalent","Explicitly declare that the source and target parameter domains are equivalent.",sp.explicitEquivalence,v=>SongEdit("source equivalence",()=>sp.explicitEquivalence=v)),Z.MicroMinMax("Range",sp.min,sp.max,pd.min,pd.max,"Declared physical source parameter range in "+sp.units+".",(lo,hi)=>SongEdit("source range",()=>{sp.min=lo;sp.max=hi;}),170),DialSong("Default",sp.defaultValue,0,1,"Normalized source default within the declared range.",v=>sp.defaultValue=v,decimals:3),Z.Field("Quantum","Physical source quantization step; zero is continuous.",Z.Float(sp.quantum,"Physical step size in "+sp.units+".",v=>SongEdit("source quantum",()=>sp.quantum=Mathf.Max(0,v)),70)));box.Add(parameters);
                    }root.Add(box);continue;
                }
                box.Add(Z.Segmented(source.kind==SourceDeviceKind.InstrumentMacros?1:0,new[]{"External map","Macros"},"Route declared source parameters to the instrument's external map or eight macros.",v=>SongEdit("source kind",()=>{source.kind=v==1?SourceDeviceKind.InstrumentMacros:SourceDeviceKind.InstrumentAutomation;t.externalSources?.RemoveAll(e=>e!=null&&e.id==source.id);SyncExternalDeclaration(t,source);},true)));
                var instruments=Data.instruments.Where(i=>i!=null&&i.model!=null).ToList();if(instruments.Count>0){int ii=Math.Max(0,instruments.FindIndex(i=>i.model.id==source.instrumentId));box.Add(Z.MiniRadio(ii,instruments.Select(i=>i.name).ToArray(),"Instrument receiving this source's parameters.",v=>SongEdit("source instrument",()=>{source.instrumentId=instruments[v].model.id;SyncExternalDeclaration(t,source);},true),wrap:true));if(source.instrumentId=="")box.Add(Button("Link instrument","Link the selected instrument to this declaration.",()=>SongEdit("link source instrument",()=>{source.instrumentId=instruments[ii].model.id;SyncExternalDeclaration(t,source);},true),"link-source-instrument"));}
                var instrumentSource=instruments.Find(i=>i.model.id==source.instrumentId);var external=Records(instrumentSource?.model.externalParameters).Select(p=>p.externalId).ToList();
                box.Add(Button("Add parameter","Add a declared external parameter or macro slot.",()=>SongEdit("add source parameter",()=>{if(source.kind==SourceDeviceKind.InstrumentAutomation&&external.Count==0)throw new InvalidOperationException("Declare an external parameter on the instrument first");int ordinal=Records(source.parameters).Select(p=>p.ordinal).DefaultIfEmpty(0).Max()+1;if(source.kind==SourceDeviceKind.InstrumentMacros&&ordinal>8)throw new InvalidOperationException("An instrument has eight macros");source.parameters.Add(new SourceParameterData{ordinal=ordinal,externalId=external.FirstOrDefault()??"",parameter="Parameter "+ordinal});SyncExternalDeclaration(t,source);},true),"add-source-parameter"));
                foreach(var sp in Records(source.parameters).ToArray())
                {
                    var row=Flow(Z.Text(sp.ordinal.ToString(),tooltip:"One-based source parameter slot."),Z.TextInput(sp.parameter,"Parameter label declared once.",v=>SongEdit("source parameter name",()=>sp.parameter=v),110),DialSong("Default",sp.defaultValue,0,1,"Normalized authored source default.",v=>sp.defaultValue=v,decimals:3),Z.MicroMinMax("Range",sp.min,sp.max,0,1,"Source input range in normalized units.",(lo,hi)=>SongEdit("source range",()=>{sp.min=lo;sp.max=hi;}),140),Z.Field("Quantum","Normalized source quantization; zero is continuous.",Z.Float(sp.quantum,"Source quantization step.",v=>SongEdit("source quantum",()=>sp.quantum=Mathf.Max(0,v)),65)),Z.IconButton("trash","Remove this source slot; later slots keep their declared ordinals.",()=>SongEdit("remove source parameter",()=>{source.parameters.Remove(sp);SyncExternalDeclaration(t,source);},true)));box.Add(row);
                    if(source.kind==SourceDeviceKind.InstrumentAutomation&&external.Count>0)box.Add(Z.MiniRadio(Math.Max(0,external.IndexOf(sp.externalId)),external.ToArray(),"Pick the external parameter declared on the linked instrument.",v=>SongEdit("source external parameter",()=>{sp.externalId=external[v];SyncExternalDeclaration(t,source);},true),wrap:true));
                }
                root.Add(box);
            }
            foreach(var legacy in Records(t.externalSources).Where(e=>!Records(t.sourceDevices).Any(s=>s.id==e.id)).ToArray())root.Add(Flow(Z.Text(legacy.pluginId+" / "+legacy.sourceOrdinal,tooltip:"Preserved legacy source declaration."),Button("Edit declaration","Copy the legacy source into editable explicit metadata.",()=>SongEdit("edit legacy source",()=>{var source=new SourceDeviceData{id=legacy.id,ordinal=legacy.sourceOrdinal,instrumentId=legacy.instrumentId,pluginId=legacy.pluginId,kind=SourceDeviceKind.InstrumentAutomation};if(legacy.parameterNumbers==null)throw new InvalidOperationException("Preserved legacy parameter metadata is null");for(int p=0;p<legacy.parameterNumbers.Count;p++)source.parameters.Add(new SourceParameterData{ordinal=p+1,externalId=legacy.parameterNumbers[p],parameter="Parameter "+(p+1)});if(t.sourceDevices==null)t.sourceDevices=new List<SourceDeviceData>();t.sourceDevices.Add(source);},true))));
        }
        static void SyncExternalDeclaration(TrackData track,SourceDeviceData source)
        {
            if(source.kind!=SourceDeviceKind.InstrumentAutomation||source.parameters==null)return;var ext=Records(track.externalSources).FirstOrDefault(s=>s.id==source.id);if(ext==null){ext=new ExternalSourceDevice{id=source.id};if(track.externalSources==null)track.externalSources=new List<ExternalSourceDevice>();track.externalSources.Add(ext);}ext.instrumentId=source.instrumentId;ext.pluginId=source.pluginId;ext.sourceOrdinal=source.ordinal;if(ext.parameterNumbers==null)ext.parameterNumbers=new List<string>();ext.parameterNumbers.Clear();for(int p=1;p<=Records(source.parameters).Select(x=>x.ordinal).DefaultIfEmpty(0).Max();p++)ext.parameterNumbers.Add(Records(source.parameters).FirstOrDefault(x=>x.ordinal==p)?.externalId??"unassigned-"+p);
        }
    }
}
