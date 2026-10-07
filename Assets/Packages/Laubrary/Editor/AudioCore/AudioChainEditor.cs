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
    /// <summary>Descriptor-to-control helpers shared by the chain editor and checks.</summary>
    public static class AudioChainEditor
    {
        public static VisualElement Parameter(ParamDesc pd,float value,Action<float> changed,float width=140)
        {
            string tip=(pd.desc??pd.name)+(string.IsNullOrEmpty(pd.unit)?"":" ("+pd.unit+")");
            if(pd.IsChoice)return Z.Field(pd.name,tip,pd.options.Length<=3?Z.Segmented(Mathf.Clamp(Mathf.RoundToInt(value),0,pd.options.Length-1),pd.options,tip,v=>changed(v)):Z.MiniRadio(Mathf.Clamp(Mathf.RoundToInt(value),0,pd.options.Length-1),pd.options,tip,v=>changed(v),wrap:true));
            if(pd.curve==ParamCurve.Toggle)return Z.Toggle(pd.name,tip,value>=.5f,v=>changed(v?1:0));
            // Display physical units while the slider maps the track logarithmically.
            // Opening the control never rewrites a legacy value outside its editing range.
            bool percent=string.IsNullOrEmpty(pd.unit)&&pd.min>=-1&&pd.max<=1;
            float scale=pd.unit=="s"?1000:percent?100:1;
            string unit=pd.unit=="s"?"ms":percent?"%":pd.unit;
            string label=pd.name+(string.IsNullOrEmpty(unit)?"":" "+unit);
            int decimals=pd.curve==ParamCurve.Integer||percent||pd.unit=="s"?0:pd.unit=="dB"?1:pd.unit=="Hz"?(pd.max>=1000?0:2):pd.unit=="ms"?(pd.min<1?2:pd.max<=100?1:0):2;
            return Z.MicroSlider(label,value*scale,pd.min*scale,pd.max*scale,tip,v=>changed(pd.curve==ParamCurve.Integer?Mathf.Round(v/scale):v/scale),Math.Max(width,label.Length*6.5f+50),defaultValue:pd.def*scale,decimals:decimals,logarithmic:pd.curve==ParamCurve.Logarithmic&&pd.min>0);
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

    }
}
