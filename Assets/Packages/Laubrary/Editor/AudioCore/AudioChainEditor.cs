using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Audio.Editor
{
    /// <summary>Descriptor-driven authoring shared by Zounds and portable AudioCore clients. The edit callback records Undo before invoking the supplied action.</summary>
    public sealed class AudioChainEditor : VisualElement
    {
        readonly Func<AudioEffectChainData> get;
        readonly Action<string,Action,bool> edit;
        readonly Action<string> removed;
        readonly bool directAdd;
        int addType, addModifier, bindingNode, bindingParam;
        public AudioChainEditor(Func<AudioEffectChainData> getter, Action<string,Action,bool> editCallback, Action<string> nodeRemoved=null,bool directAdd=false)
        {
            get=getter;edit=editCallback;removed=nodeRemoved;this.directAdd=directAdd;name="audio-chain-editor";style.minWidth=0;style.flexShrink=0;Build();
        }
        public static VisualElement Parameter(ParamDesc pd,float value,Action<float> changed,float width=140)
        {
            string tip=(pd.desc??pd.name)+(string.IsNullOrEmpty(pd.unit)?"":" ("+pd.unit+")");
            if(pd.IsChoice)return Z.Field(pd.name,tip,Z.MiniRadio(Mathf.Clamp(Mathf.RoundToInt(value),0,pd.options.Length-1),pd.options,tip,v=>changed(v),wrap:true));
            if(pd.curve==ParamCurve.Toggle)return Z.Toggle(pd.name,tip,value>=.5f,v=>changed(v?1:0));
            return Scalar(pd,value,changed,(display,min,max,defaultValue,onChanged)=>Z.MicroSlider(pd.name,display,min,max,tip,onChanged,Math.Max(width,pd.name.Length*6.5f+50),defaultValue:defaultValue,decimals:pd.curve==ParamCurve.Integer?0:3));
        }
        /// <summary>Shared descriptor-to-control authoring, including log scaling, integer snapping and reset defaults. Clients choose their existing visual skin.</summary>
        public static T Scalar<T>(ParamDesc pd,float value,Action<float> changed,Func<float,float,float,float,Action<float>,T> factory) where T:VisualElement
        {
            bool log=pd.curve==ParamCurve.Logarithmic;float min=log?Mathf.Log(Mathf.Max(pd.min,1e-4f)):pd.min,max=log?Mathf.Log(Mathf.Max(pd.max,1e-4f)):pd.max;
            float Display(float v)=>log?Mathf.InverseLerp(min,max,Mathf.Log(Mathf.Max(v,1e-4f))):v;
            return factory(Display(value),log?0:pd.min,log?1:pd.max,Display(pd.def),v=>changed(log?Mathf.Exp(Mathf.Lerp(min,max,v)):pd.curve==ParamCurve.Integer?Mathf.Round(v):v));
        }
        public static float DisplayValue(ParamDesc pd,float value)=>pd.curve==ParamCurve.Logarithmic?Mathf.InverseLerp(Mathf.Log(Mathf.Max(pd.min,1e-4f)),Mathf.Log(Mathf.Max(pd.max,1e-4f)),Mathf.Log(Mathf.Max(value,1e-4f))):value;
        public sealed class NodeParts {public VisualElement enabled;public Label title;public Button remove;}
        public static NodeParts NodeAuthoring(EffectDesc desc,bool enabled,Action<bool> setEnabled,Action remove,Func<bool,Action<bool>,VisualElement> enableFactory=null,Func<string,string,Label> titleFactory=null,Func<Action,Button> removeFactory=null)
            => new NodeParts{enabled=enableFactory!=null?enableFactory(enabled,setEnabled):Z.Toggle("On",desc.summary,enabled,setEnabled),title=titleFactory!=null?titleFactory(desc.displayName,desc.summary):Z.Text(desc.displayName,tooltip:desc.summary),remove=removeFactory!=null?removeFactory(remove):Z.IconButton("trash","Remove this effect.",remove)};
        public static VisualElement NodeHeader(EffectDesc desc,bool enabled,Action<bool> setEnabled,Action remove)
        {var parts=NodeAuthoring(desc,enabled,setEnabled,remove);return Row(parts.enabled,parts.title,parts.remove);}
        static VisualElement Row(params VisualElement[] parts){var r=Z.Row();r.style.flexWrap=Wrap.Wrap;r.style.height=StyleKeyword.Auto;r.style.minHeight=StyleKeyword.Auto;r.style.flexShrink=0;r.style.alignItems=Align.FlexStart;foreach(var p in parts)r.Add(p);return r;}
        static float[] Defaults(ParamDesc[] p)=>p.Select(x=>x.def).ToArray();
        static float Read(float[] p,int i,ParamDesc d)=>p!=null&&i<p.Length?p[i]:d.def;
        static void Write(ref float[] p,int at,float value,ParamDesc[] ds){if(p==null||p.Length<ds.Length){var n=Defaults(ds);if(p!=null)Array.Copy(p,n,Math.Min(p.Length,n.Length));p=n;}p[at]=value;}
        void Change(string label,Action action,bool structural){edit(label,action,structural);if(structural)Build();}
        void Build()
        {
            Clear();var chain=get();if(chain==null)return;
            var types=Enumerable.Range(0,ZoundEffectDescriptors.EffectTypeCount).Select(i=>ZoundEffectDescriptors.Get((ZoundEffectType)i).displayName).ToArray();
            void AddEffect(int type)=>Change("add effect",()=>chain.nodes.Add(new AudioEffectNodeData{type=(ZoundEffectType)type,uid=Guid.NewGuid().ToString("N"),p=Defaults(ZoundEffectDescriptors.Get((ZoundEffectType)type).parameters)}),true);
            if(directAdd){var choices=Row();for(int type=0;type<types.Length;type++){int at=type;choices.Add(Named(Z.Button(types[type],"Add "+types[type]+" to this chain.",()=>AddEffect(at)),type==0?"chain-add-effect":"chain-add-effect-"+type));}Add(choices);}
            else Add(Row(Z.MiniRadio(addType,types,"Choose an effect to add.",v=>addType=v,wrap:true),Named(Z.Button("Add effect","Append the chosen effect.",()=>AddEffect(addType)),"chain-add-effect")));
            for(int i=0;i<chain.nodes.Count;i++)
            {
                int at=i;var node=chain.nodes[i];var desc=ZoundEffectDescriptors.Get(node.type);if(desc==null)continue;
                var box=Z.BoxKeyed("",desc.summary,"audio.chain.node."+node.uid);var header=NodeHeader(desc,node.enabled,v=>Change("enable effect",()=>node.enabled=v,false),()=>Change("remove effect",()=>{string uid=node.uid;chain.nodes.RemoveAt(at);chain.bindings.RemoveAll(b=>b.nodeIndex==at);foreach(var b in chain.bindings)if(b.nodeIndex>at)b.nodeIndex--;removed?.Invoke(uid);},true));
                header.name="chain-node-"+at;Drag(header,"node",at,(from,to)=>Change("reorder effects",()=>{var old=chain.nodes.ToArray();Move(chain.nodes,from,to);foreach(var b in chain.bindings)if(b.nodeIndex>=0&&b.nodeIndex<old.Length)b.nodeIndex=chain.nodes.IndexOf(old[b.nodeIndex]);},true));box.Add(header);
                var parameters=Row();for(int p=0;p<desc.parameters.Length;p++){int pi=p;parameters.Add(Named(Parameter(desc.parameters[p],Read(node.p,p,desc.parameters[p]),v=>Change("effect parameter",()=>Write(ref node.p,pi,v,desc.parameters),false)),"chain-param-"+at+"-"+p));}box.Add(parameters);Add(box);
            }
            var mods=Z.BoxKeyed("Modifiers","Modulate effect parameters with the shared audio engine.","audio.chain.modifiers");
            var mt=Enumerable.Range(0,ZoundEffectDescriptors.ModifierTypeCount).Select(i=>ZoundEffectDescriptors.GetModifier((ZoundModifierType)i).displayName).ToArray();
            mods.Add(Z.MiniRadio(addModifier,mt,"Choose a modifier to add.",v=>addModifier=v,wrap:true));
            mods.Add(Row(Named(Z.Button("Add modifier","Append the chosen modifier.",()=>Change("add modifier",()=>{var m=new AudioModifierData{type=(ZoundModifierType)addModifier,uid=Guid.NewGuid().ToString("N"),p=Defaults(ZoundEffectDescriptors.GetModifier((ZoundModifierType)addModifier).parameters),steps=new[]{1f}};m.curve.m_enabled=true;m.curve.m_points.Add(new AudioEnvelopePointData{time=0,value=1,exponent=1});m.curve.m_points.Add(new AudioEnvelopePointData{time=1,value=1,exponent=1});chain.modifiers.Add(m);},true)),"chain-add-modifier")));
            for(int i=0;i<chain.modifiers.Count;i++)
            {
                int at=i;var m=chain.modifiers[i];var d=ZoundEffectDescriptors.GetModifier(m.type);var box=Z.BoxKeyed(d.displayName,d.summary,"audio.chain.mod."+m.uid);var head=Row(Z.Toggle("On",d.summary,m.enabled,v=>Change("enable modifier",()=>m.enabled=v,false)),Z.Field("Name","Optional modifier name.",Z.TextInput(m.name,"Optional modifier name.",v=>Change("modifier name",()=>m.name=v,false),100)),Z.IconButton("trash","Remove this modifier and its bindings.",()=>Change("remove modifier",()=>{chain.modifiers.RemoveAt(at);chain.bindings.RemoveAll(b=>b.modifierIndex==at);foreach(var b in chain.bindings)if(b.modifierIndex>at)b.modifierIndex--;},true)));
                Drag(head,"modifier",at,(a,b)=>Change("reorder modifiers",()=>{var old=chain.modifiers.ToArray();Move(chain.modifiers,a,b);foreach(var binding in chain.bindings)binding.modifierIndex=chain.modifiers.IndexOf(old[binding.modifierIndex]);},true));box.Add(head);
                var pr=Row();for(int p=0;p<d.parameters.Length;p++){int pi=p;pr.Add(Parameter(d.parameters[p],Read(m.p,p,d.parameters[p]),v=>Change("modifier parameter",()=>Write(ref m.p,pi,v,d.parameters),false)));}box.Add(pr);
                if(m.type==ZoundModifierType.Step){var steps=Row();for(int s=0;s<m.steps.Length;s++){int si=s;steps.Add(Z.MicroSlider("Step "+(s+1),m.steps[s],-1,2,"Modifier step output.",v=>Change("step value",()=>m.steps[si]=v,false),100));}steps.Add(Z.Button("+","Add a step.",()=>Change("add step",()=>m.steps=m.steps.Concat(new[]{1f}).ToArray(),true)));if(m.steps.Length>1)steps.Add(Z.Button("−","Remove last step.",()=>Change("remove step",()=>m.steps=m.steps.Take(m.steps.Length-1).ToArray(),true)));box.Add(steps);}
                if(m.type==ZoundModifierType.Envelope||m.type==ZoundModifierType.Lfo)
                {
                    box.Add(Row(Z.Button("Add point","Add a midpoint to the curve.",()=>Change("curve point",()=>{float time=.5f;while(m.curve.m_points.Exists(x=>Mathf.Abs(x.time-time)<.0001f))time+=.05f;m.curve.m_points.Add(new AudioEnvelopePointData{time=time,value=1,exponent=1});m.curve.m_points.Sort((a,b)=>a.time.CompareTo(b.time));},true))));
                    for(int p=0;p<m.curve.m_points.Count;p++){int pi=p;var point=m.curve.m_points[p];box.Add(Row(Z.Field("Time","Curve point time.",Z.Float(point.time,"Curve point time.",v=>Change("curve time",()=>{var q=m.curve.m_points[pi];q.time=v;m.curve.m_points[pi]=q;m.curve.m_points.Sort((a,b)=>a.time.CompareTo(b.time));},true),70)),Z.Field("Value","Curve point value.",Z.Float(point.value,"Curve point value.",v=>Change("curve value",()=>{var q=m.curve.m_points[pi];q.value=v;m.curve.m_points[pi]=q;},false),70)),Z.Field("Exponent","Curve segment exponent.",Z.Float(point.exponent,"Curve segment exponent.",v=>Change("curve exponent",()=>{var q=m.curve.m_points[pi];q.exponent=Mathf.Max(.001f,v);m.curve.m_points[pi]=q;},false),70)),Z.IconButton("trash","Remove curve point.",()=>Change("remove curve point",()=>m.curve.m_points.RemoveAt(pi),true))));}
                }
                for(int b=0;b<chain.bindings.Count;b++)
                {
                    var binding=chain.bindings[b];if(binding.modifierIndex!=at)continue;var bd=binding.nodeIndex>=0&&binding.nodeIndex<chain.nodes.Count?ZoundEffectDescriptors.Get(chain.nodes[binding.nodeIndex].type):null;
                    var operations=new[]{ModulationCombine.Shift,ModulationCombine.Set,ModulationCombine.Scale,ModulationCombine.Ratio};int operation=Array.IndexOf(operations,binding.combine);if(operation<0)operation=0;
                    box.Add(Row(Z.Text(bd!=null&&binding.paramIndex<bd.parameters.Length?bd.displayName+" / "+bd.parameters[binding.paramIndex].name:"Source / "+binding.paramIndex,tooltip:"The parameter this modifier drives."),Z.MiniRadio(operation,new[]{"Shift","Set","Scale","Ratio"},"How the modifier combines with the parameter.",v=>Change("binding operation",()=>{binding.schema=2;binding.combine=operations[v];},false),wrap:true),Z.MicroSlider("Depth",binding.depth,0,1,"Binding strength.",v=>Change("binding depth",()=>{binding.schema=2;binding.depth=v;},false),110),Z.IconButton("trash","Remove this binding.",()=>Change("remove binding",()=>chain.bindings.Remove(binding),true))));
                }
                if(chain.nodes.Count>0){bindingNode=Mathf.Clamp(bindingNode,0,chain.nodes.Count-1);var target=ZoundEffectDescriptors.Get(chain.nodes[bindingNode].type);bindingParam=Mathf.Clamp(bindingParam,0,target.parameters.Length-1);box.Add(Row(Z.MiniRadio(bindingNode,chain.nodes.Select((n,nx)=>nx+" "+ZoundEffectDescriptors.Get(n.type).displayName).ToArray(),"Pick the effect to modulate.",v=>{bindingNode=v;bindingParam=0;Build();},wrap:true),Z.MiniRadio(bindingParam,target.parameters.Select(p=>p.name).ToArray(),"Pick its parameter.",v=>bindingParam=v,wrap:true),Named(Z.Button("Bind","Bind this modifier to the selected parameter.",()=>Change("add binding",()=>chain.bindings.Add(new AudioModifierBindingData{schema=2,modifierIndex=at,nodeIndex=bindingNode,paramIndex=bindingParam,combine=ModulationCombine.Set,depth=1}),true)),"chain-bind-"+at)));}
                mods.Add(box);
            }
            Add(mods);
        }
        static T Named<T>(T e,string name) where T:VisualElement{e.name=name;return e;}
        static void Move<T>(IList<T> xs,int a,int b){var item=xs[a];xs.RemoveAt(a);xs.Insert(b,item);}
        public static void Drag(VisualElement e,string key,int index,Action<int,int> move)
        {
            e.RegisterCallback<PointerDownEvent>(v=>{if(v.button==0){DragAndDrop.PrepareStartDrag();DragAndDrop.SetGenericData("audio.chain."+key,index);}});
            e.RegisterCallback<PointerMoveEvent>(v=>{if((v.pressedButtons&1)!=0&&DragAndDrop.GetGenericData("audio.chain."+key)is int)DragAndDrop.StartDrag("Reorder "+key);});
            e.RegisterCallback<DragUpdatedEvent>(v=>{if(DragAndDrop.GetGenericData("audio.chain."+key)is int)DragAndDrop.visualMode=DragAndDropVisualMode.Move;});
            e.RegisterCallback<DragPerformEvent>(v=>{if(DragAndDrop.GetGenericData("audio.chain."+key)is int from){DragAndDrop.AcceptDrag();DragAndDrop.SetGenericData("audio.chain."+key,null);if(from!=index)move(from,index);v.StopPropagation();}});
        }
    }
}
