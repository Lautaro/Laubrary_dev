using System;
using System.Collections.Generic;
using Laubrary.Audio;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Unity.Collections;
using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>Portable authored-chain compiler. Numeric schema matches the AudioCore kernels, without a Zounds asset dependency.</summary>
    internal static class TrackerFxCompiler
    {
        // Each parameter is default, minimum, maximum, ratio-spaced (0/1).
        static readonly float[][] Schema = {
            new float[]{1,0,4,0},
            new float[]{-1,-40,0,0,50,1,1000,1},
            new float[]{-10,-60,0,0,4,1,20,1,10,.1f,200,1,100,1,2000,1,0,-12,24,0},
            new float[]{250,1,2000,1,.4f,0,.98f,0,.3f,0,1,0,500,10,2000,1,0,0,1,0},
            new float[]{.5f,0,1,0,.5f,0,1,0,1,0,1,0,.3f,0,1,0},
            new float[]{20000,20,20000,1,.707f,.1f,10,1},
            new float[]{20,20,20000,1,.707f,.1f,10,1},
            new float[]{.5f,.01f,10,1,2,.1f,10,0,.5f,-.95f,.95f,0,.5f,0,1,0},
            new float[]{.8f,.01f,5,1,8,1,30,0,2,1,4,0,.5f,0,1,0},
            new float[]{.5f,.01f,10,1,.7f,0,1,0,4,2,12,0,.3f,-.9f,.9f,0,.5f,0,1,0},
            new float[]{8,1,16,0,1,1,64,1,1,0,1,0},
            new float[]{10,1,100,1,.5f,0,1,0,1,0,1,0},
            new float[]{0,-24,24,0,0,-24,24,0,0,-24,24,0,0,-24,24,0,0,-24,24,0,0,-24,24,0,0,-24,24,0,10,10,20000,1,22000,20,22000,1},
            new float[]{-.5f,-30,0,0},
            new float[]{0,0,10,0,0,0,10,0,0,0,1,0},
            new float[]{0,-24,24,0,0,-24,24,0,20,2,100,1,100,10,1000,1}
        };
        static readonly float[][] ModDefaults = {
            new float[]{0,0}, new float[]{1,1,0,1,0,.5f,0}, new float[]{-.25f,.25f,1}, new float[]{0,250,0,0,0,0}, new float[]{.5f}
        };
        static readonly float[][] ModMin = {new float[]{0,0},new float[]{-4,0,0,0,0,.01f,-1},new float[]{-1,-1,.1f},new float[]{0,1,0,0,0,0},new float[]{0}};
        static readonly float[][] ModMax = {new float[]{30,1},new float[]{4,50,3,1,1,10,1},new float[]{1,1,10},new float[]{1,10000,1,1,1,1},new float[]{1}};
        internal static string ParameterUnits(ZoundEffectType type,int parameter)
        {
            switch(type){
                case ZoundEffectType.Gain:return "linear";
                case ZoundEffectType.Delay:return parameter==0||parameter==3?"milliseconds":parameter==4?"boolean":"normalized";
                case ZoundEffectType.LowPass:case ZoundEffectType.HighPass:return parameter==0?"hertz":"Q";
                case ZoundEffectType.Limiter:return parameter==0?"decibels":"milliseconds";
                case ZoundEffectType.Compressor:return parameter==0||parameter==4?"decibels":parameter==1?"ratio":"milliseconds";
                case ZoundEffectType.Reverb:return "normalized";
                case ZoundEffectType.Flanger:return parameter==0?"hertz":parameter==1?"milliseconds":"normalized";
                case ZoundEffectType.Chorus:return parameter==0?"hertz":parameter==1?"milliseconds":parameter==2?"integer":"normalized";
                case ZoundEffectType.Phaser:return parameter==0?"hertz":parameter==2?"integer":"normalized";
                case ZoundEffectType.BitCrush:return parameter==0?"integer":parameter==1?"ratio":"normalized";
                case ZoundEffectType.Distortion:return parameter==0?"linear":"normalized";
                case ZoundEffectType.EQ:return parameter<7?"decibels":"hertz";
                case ZoundEffectType.Normalize:return "decibels";
                case ZoundEffectType.Fade:return parameter<2?"seconds":"normalized";
                case ZoundEffectType.TransientShaper:return parameter<2?"decibels":"milliseconds";
                default:return "unsupported";
            }
        }
        public static TrackerChain Compile(AudioEffectChainData chain, int start, int end, int rate)
        {
            if (chain == null) throw new ArgumentException("Missing effect chain");
            if (chain.nodes.Count > 64 || chain.modifiers.Count > 64 || chain.bindings.Count > 512) throw new ArgumentException("Effect chain exceeds engine limits");
            int n = end - start, m = chain.modifiers.Count;
            var t = new AudioChainTables { nodeCount=n, modCount=m,
                nodeType=new ZoundEffectType[n], enabled=new bool[n], stateOffset=new int[n], paramOffset=new int[n], paramCountOf=new int[n], derivedOffset=new int[n], derivedCountOf=new int[n],
                modType=new ZoundModifierType[m], modStateOffset=new int[m], modParamOffset=new int[m], modParamCountOf=new int[m], modCurveOffset=new int[m], modCurveCountOf=new int[m], modStepOffset=new int[m], modStepCountOf=new int[m], modExtraSeconds=new float[m],modCtlInit=new float[m],modCtlCoef=new float[m] };
            var pb = new List<float> { 1,1,1,1 }; var lo=new List<float>{.1f,0,.1f,0}; var hi=new List<float>{4,4,4,4}; var ratio=new List<bool>{true,false,true,false};
            var derived = new List<int>();
            for(int i=0;i<n;i++) {
                var node=chain.nodes[start+i]; int kind=(int)node.type;
                if(kind<0 || kind>=Schema.Length) throw new ArgumentException("Unknown AudioCore effect");
                float[] schema=Schema[kind], p=new float[schema.Length/4];
                if(node.p!=null && node.p.Length>p.Length) throw new ArgumentException("Extra effect parameters");
                t.nodeType[i]=node.type;t.enabled[i]=node.enabled;t.paramOffset[i]=pb.Count;t.paramCountOf[i]=p.Length;t.stateOffset[i]=t.stateFloats;
                for(int k=0;k<p.Length;k++) {
                    float v=node.p!=null && k<node.p.Length?node.p[k]:schema[k*4];
                    if(!math.isfinite(v)) throw new ArgumentException("Nonfinite effect parameter");
                    p[k]=math.clamp(v,schema[k*4+1],schema[k*4+2]);pb.Add(p[k]);lo.Add(schema[k*4+1]);hi.Add(schema[k*4+2]);ratio.Add(schema[k*4+3]!=0);
                }
                t.stateFloats=checked(t.stateFloats+AudioEffectSizing.EffectStateFloats(node.type,p,rate));
                t.derivedOffset[i]=derived.Count;
                if(node.type==ZoundEffectType.Reverb) {
                    for(int c=0;c<8;c++) for(int ch=0;ch<2;ch++) derived.Add(ReverbEffect.CombLen(c,ch,rate));
                    for(int a=0;a<4;a++) for(int ch=0;ch<2;ch++) derived.Add(ReverbEffect.AllpassLen(a,ch,rate));
                }
                else if(node.type==ZoundEffectType.Delay) derived.Add(AudioEffectSizing.DelayRingFrames(p,rate));
                else if(node.type==ZoundEffectType.Flanger) derived.Add(AudioEffectSizing.ModDelayFrames(12,rate));
                else if(node.type==ZoundEffectType.Chorus) derived.Add(AudioEffectSizing.ModDelayFrames(40,rate));
                t.derivedCountOf[i]=derived.Count-t.derivedOffset[i];
                if(node.type==ZoundEffectType.Delay)t.tailSeconds=math.min(30,t.tailSeconds+AudioEffectSizing.DelayTail(p));
                if(node.type==ZoundEffectType.Reverb)t.tailSeconds=math.min(30,t.tailSeconds+AudioEffectSizing.ReverbTail(p));
            }
            var mp=new List<float>();var curves=new List<EnvPoint>();var steps=new List<float>();
            for(int i=0;i<m;i++) {
                var mod=chain.modifiers[i];int kind=(int)mod.type;
                if(kind<0||kind>=ModDefaults.Length)throw new ArgumentException("Unknown chain modifier");
                var def=ModDefaults[kind];
                t.modType[i]=mod.type;t.modParamOffset[i]=mp.Count;t.modParamCountOf[i]=def.Length;
                if(mod.p!=null&&mod.p.Length>def.Length)throw new ArgumentException("Extra modifier parameters");
                for(int k=0;k<def.Length;k++){float v=mod.p!=null&&k<mod.p.Length?mod.p[k]:def[k];if(!math.isfinite(v))throw new ArgumentException("Nonfinite modifier");mp.Add(math.clamp(v,ModMin[kind][k],ModMax[kind][k]));}
                t.modStateOffset[i]=t.stateFloats;t.stateFloats=checked(t.stateFloats+AudioEffectSizing.ModifierStateFloats(mod.type));
                t.modCurveOffset[i]=curves.Count;
                if(mod.curve!=null&&mod.curve.m_points.Count>0) {if(mod.curve.m_points.Count>4096)throw new ArgumentException("Chain curve exceeds 4096 points");float time=float.NegativeInfinity;foreach(var p in mod.curve.m_points){if(!math.isfinite(p.time)||!math.isfinite(p.value)||!math.isfinite(p.exponent)||!math.isfinite(p.randomX)||!math.isfinite(p.randomY)||!math.isfinite(p.randomBias)||p.time<=time||p.randomX<0||p.randomY<0)throw new ArgumentException("Malformed chain curve");time=p.time;curves.Add(new EnvPoint(p.time,p.value,p.exponent,p.randomX,p.randomY,p.randomBias,mod.curve.m_yMin,mod.curve.m_yMax));}}
                else {curves.Add(new EnvPoint(0,1,1));curves.Add(new EnvPoint(1,1,1));}
                t.modCurveCountOf[i]=curves.Count-t.modCurveOffset[i];t.modStepOffset[i]=steps.Count;
                if(mod.steps!=null&&mod.steps.Length>0){if(mod.steps.Length>4096)throw new ArgumentException("Chain steps exceed 4096 values");foreach(float v in mod.steps){if(!math.isfinite(v))throw new ArgumentException("Nonfinite step");steps.Add(v);}}else steps.Add(1);
                t.modStepCountOf[i]=steps.Count-t.modStepOffset[i];t.modExtraSeconds[i]=mod.type==ZoundModifierType.Envelope?mp[t.modParamOffset[i]]:0;
                t.modCtlInit[i]=mod.type==ZoundModifierType.Code?math.saturate(mp[t.modParamOffset[i]]):(mod.zpocRest>=0?mod.zpocRest:1);
                t.modCtlCoef[i]=mod.zpocSmoothMs<=0?1:1-math.exp(-64f/(rate*mod.zpocSmoothMs*.001f));
            }
            var bm=new List<int>();var bt=new List<int>();var bo=new List<ModifierOp>();var bc=new List<ModulationCombine>();var bd=new List<float>();var ramp=new List<int>();
            foreach(var b in chain.bindings) {
                if(b.nodeIndex<start||b.nodeIndex>=end) {if(b.nodeIndex<0)throw new ArgumentException("Tracker bus has no source stage for source bindings");continue;}
                if(b.modifierIndex<0||b.modifierIndex>=m||b.paramIndex<0||b.paramIndex>=t.paramCountOf[b.nodeIndex-start])throw new ArgumentException("Invalid chain binding");
                if(!chain.modifiers[b.modifierIndex].enabled)continue;
                if(!Enum.IsDefined(typeof(ModulationCombine),b.combine)||!Enum.IsDefined(typeof(ModifierOp),b.op))throw new ArgumentException("Unknown binding operation");
                int f=t.paramOffset[b.nodeIndex-start]+b.paramIndex;var combine=b.combine;float depth=b.depth;
                if(b.schema==1){if(combine==ModulationCombine.Shift)combine=ModulationCombine.ShiftWholeRange;depth=math.clamp(depth,-1,1);}
                else if(b.schema<1){combine=b.op==ModifierOp.Replace?ModulationCombine.Set:ModulationCombine.ShiftWholeRange;float d=math.abs(depth);if(b.op==ModifierOp.Multiply)depth=math.saturate(d)*.5f;else if(b.op==ModifierOp.Replace)depth=1;else if(ratio[f]){float mid=math.sqrt(math.max(lo[f],1e-4f)*math.max(hi[f],1e-4f));depth=math.saturate(math.abs(ModulationMath.ToPosition(mid+d,lo[f],hi[f],true)-ModulationMath.ToPosition(mid,lo[f],hi[f],true)));}else depth=math.saturate(d/(hi[f]-lo[f]));}
                else depth=math.clamp(depth,-1,1);
                if(combine==ModulationCombine.Set&&(chain.modifiers[b.modifierIndex].type==ZoundModifierType.Envelope||chain.modifiers[b.modifierIndex].type==ZoundModifierType.Code))combine=ModulationCombine.SetFromZero;
                if(combine==ModulationCombine.Shift&&chain.modifiers[b.modifierIndex].type==ZoundModifierType.Code)combine=ModulationCombine.ShiftFromCentre;
                bm.Add(b.modifierIndex);bt.Add(f);bo.Add(b.op);bc.Add(combine);bd.Add(depth);if(!ramp.Contains(f))ramp.Add(f);
            }
            if(t.stateFloats>4194304)throw new ArgumentException("FX state exceeds 16 MiB per chain");
            t.paramCount=pb.Count;t.pBase=pb.ToArray();t.pMin=lo.ToArray();t.pMax=hi.ToArray();t.pRatio=ratio.ToArray();t.derivedFlat=derived.ToArray();
            t.modParamFlat=mp.ToArray();t.modCurveFlat=curves.ToArray();t.modStepFlat=steps.ToArray();
            t.bindCount=bm.Count;t.bindModifier=bm.ToArray();t.bindTarget=bt.ToArray();t.bindOp=bo.ToArray();t.bindCombine=bc.ToArray();t.bindDepth=bd.ToArray();t.rampedCount=ramp.Count;t.ramped=ramp.ToArray();
            var layout=SapChainLayout.Create(t,Allocator.Persistent);
            try {
                var processor=AudioChainProcessor.Create(in layout,rate,Allocator.Persistent);
                // Random values are per chain instance, seeded before publication; they must never remain an uninitialized zero.
                TrackerChainSeed.Apply(ref processor,in layout);
                return new TrackerChain{layout=layout,processor=processor,position=end};
            }
            catch {layout.Dispose();throw;}
        }
    }
}
