using System;
using Unity.Mathematics;
using Laubrary.ZTracker.Model;

namespace Laubrary.ZTracker.Engine
{
    // Values are physical, not normalized. Macro inputs alone are normalized.
    public enum TrackerParameter
    {
        Volume, Pan, FineTune, Blend, PulseWidth, WaveBRatio, PMDepth, UnisonDetune, UnisonSpread,
        Attack, Decay, Sustain, Release, VibratoDepth, VibratoRate, VibratoFadeIn, FilterCutoff, FilterResonance,
        GlideSeconds, FMFeedback,
        Op0Ratio, Op0FixedHz, Op0Level, Op0Attack, Op0Decay, Op0Sustain, Op0Release,
        Op1Ratio, Op1FixedHz, Op1Level, Op1Attack, Op1Decay, Op1Sustain, Op1Release,
        Op2Ratio, Op2FixedHz, Op2Level, Op2Attack, Op2Decay, Op2Sustain, Op2Release,
        Op3Ratio, Op3FixedHz, Op3Level, Op3Attack, Op3Decay, Op3Sustain, Op3Release,
        // Self-feedback of the modulators A, B and D (operator slots 0..2), 0..1.
        // Kept after the seven-field operator blocks so their indices do not move.
        Op0Feedback, Op1Feedback, Op2Feedback,
        Count
    }
    public struct TrackerMacroValue { public float value, target, step, authored; public bool sliding; }
    public struct TrackerParameterRoute { public int instrument, macro, parameter, points, pointCount; public float min,max,quantum,lower; }
    public unsafe struct TrackerTone
    {
        // Zero = static; otherwise one plus the relative modulation slot. This
        // keeps parameter reads constant-time even with many FM envelopes.
        public fixed byte parameterEnvelopeSlots[TrackerParameters.Stride];
        public int kind, waveA, waveB, blendMode, members, pcmB, baseNoteB, envelopes, arpPoints, arpPointCount, arpNotes, arpNoteCount, algorithm;
        public int parameterSet;
        public SampleLoop loopB;
        public int loopStartB, loopEndB;
        public bool releaseExitsLoopB, blendEnvelope, glide, legato, legatoEnvelopes, arpPerNote;
        public float transposeCents, fineTuneB, blendAttack, blendDecay, blendSustain, blendRelease, baseGlobalVolume, baseGlobalPan, baseGlobalTune, baseBlend, basePM, localVolume, localPan, vibratoRandomness;
        // Legato (T-0627/T-0628): whether an overlapping note slides its pitch over the glide time, and what happens to
        // the held note's level: 0 follows the new note's velocity, 1 ignores it, 2 slides to it over legatoVelocitySeconds;
        // legatoVelocityScale is the share of the velocity change that applies (follow and slide).
        public bool legatoSlide;
        public int legatoVelocity;
        public float legatoVelocitySeconds, legatoVelocityScale;
        // Polyphonic legato (T-0633): a new note while another is held joins its running envelopes (pitch stays its own).
        public bool legatoPoly;
    }
    public static class TrackerParameters
    {
        public const int Stride=(int)TrackerParameter.Count;
        public static string Units(TrackerParameter p)
        {
            if(p==TrackerParameter.Volume)return "linear gain";
            if(p==TrackerParameter.FMFeedback)return "phase feedback";
            if(p==TrackerParameter.FineTune||p==TrackerParameter.UnisonDetune||p==TrackerParameter.VibratoDepth)return "cents";
            if(p==TrackerParameter.VibratoRate||p==TrackerParameter.FilterCutoff)return "Hz";
            if(p==TrackerParameter.Attack||p==TrackerParameter.Decay||p==TrackerParameter.Release||p==TrackerParameter.GlideSeconds||p==TrackerParameter.VibratoFadeIn)return "seconds";
            if(IsOperatorFeedback(p))return "normalized";
            if(p>=TrackerParameter.Op0Ratio){int n=((int)p-(int)TrackerParameter.Op0Ratio)%7;return n==1?"Hz":n==3||n==4||n==6?"seconds":n==0?"ratio":n==2?"linear gain":"normalized";}
            return p==TrackerParameter.WaveBRatio?"ratio":p==TrackerParameter.PMDepth?"phase cycles or sample frames":"normalized";
        }
        public static bool Resolve(string name,out TrackerParameter parameter)
        {
            switch(name){
                case "volume":parameter=TrackerParameter.Volume;return true;case "pan":parameter=TrackerParameter.Pan;return true;
                case "fineTune":case "fineTuneCents":parameter=TrackerParameter.FineTune;return true;
                case "blend":parameter=TrackerParameter.Blend;return true;case "pulseWidth":parameter=TrackerParameter.PulseWidth;return true;
                case "waveBRatio":parameter=TrackerParameter.WaveBRatio;return true;case "pmDepth":parameter=TrackerParameter.PMDepth;return true;
                case "unisonDetune":parameter=TrackerParameter.UnisonDetune;return true;case "unisonSpread":parameter=TrackerParameter.UnisonSpread;return true;
                case "attack":parameter=TrackerParameter.Attack;return true;case "decay":parameter=TrackerParameter.Decay;return true;case "sustain":parameter=TrackerParameter.Sustain;return true;case "release":parameter=TrackerParameter.Release;return true;
                case "vibratoDepth":parameter=TrackerParameter.VibratoDepth;return true;case "vibratoRate":parameter=TrackerParameter.VibratoRate;return true;case "vibratoFadeIn":parameter=TrackerParameter.VibratoFadeIn;return true;
                case "instFilterCutoff":case "cutoff":parameter=TrackerParameter.FilterCutoff;return true;case "instFilterResonance":case "resonance":parameter=TrackerParameter.FilterResonance;return true;
                case "glideSeconds":parameter=TrackerParameter.GlideSeconds;return true;case "fmFeedback":parameter=TrackerParameter.FMFeedback;return true;
            }
            if(name!=null&&name.StartsWith("fmOperators.",StringComparison.Ordinal)){
                var bits=name.Split('.');
                if(bits.Length==3&&bits[2]=="feedback"&&int.TryParse(bits[1],out int fb)&&fb>=0&&fb<TrackerFmSetups.Modulators){parameter=TrackerParameter.Op0Feedback+fb;return true;}
                if(bits.Length==3&&int.TryParse(bits[1],out int op)&&op>=0&&op<4){int field=Array.IndexOf(new[]{"freqRatio","freqFixed","level","attack","decay","sustain","release"},bits[2]);if(field>=0){parameter=(TrackerParameter)((int)TrackerParameter.Op0Ratio+op*7+field);return true;}}
            }
            parameter=default;return false;
        }
        public static bool IsOperatorFeedback(TrackerParameter p)=>p>=TrackerParameter.Op0Feedback&&p<=TrackerParameter.Op2Feedback;
        // Operator slot (0..3) of an operator parameter, or -1.
        public static int OperatorOf(TrackerParameter p)=>IsOperatorFeedback(p)?p-TrackerParameter.Op0Feedback:p>=TrackerParameter.Op0Ratio&&p<TrackerParameter.Op0Feedback?((int)p-(int)TrackerParameter.Op0Ratio)/7:-1;
        public static float Clamp(TrackerParameter p,float v)
        {
            switch(p){case TrackerParameter.Volume:return math.clamp(v,0,16);case TrackerParameter.Pan:return math.clamp(v,-1,1);case TrackerParameter.FineTune:return math.clamp(v,-12000,12000);
                case TrackerParameter.PulseWidth:return math.clamp(v,.01f,.99f);case TrackerParameter.WaveBRatio:return math.clamp(v,0,64);case TrackerParameter.PMDepth:return math.clamp(v,0,65536);
                case TrackerParameter.UnisonDetune:return math.clamp(v,0,12000);case TrackerParameter.VibratoDepth:return math.clamp(v,0,1200);case TrackerParameter.VibratoRate:return math.clamp(v,0,1000);
                case TrackerParameter.FilterCutoff:return math.clamp(v,20,96000);case TrackerParameter.FilterResonance:return math.clamp(v,.1f,10);
                case TrackerParameter.Attack:case TrackerParameter.Decay:case TrackerParameter.Release:case TrackerParameter.GlideSeconds:case TrackerParameter.VibratoFadeIn:return math.clamp(v,0,3600);
                case TrackerParameter.FMFeedback:return math.clamp(v,0,16);
            }
            if(IsOperatorFeedback(p))return math.saturate(v);
            if(p>=TrackerParameter.Op0Ratio){int n=((int)p-(int)TrackerParameter.Op0Ratio)%7;return math.clamp(v,0,n==1?96000:n==0?64:n==3||n==4||n==6?3600:n==2?16:1);}
            return math.saturate(v);
        }
    }
}
