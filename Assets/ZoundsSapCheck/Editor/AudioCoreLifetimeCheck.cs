using System;
using Unity.Collections;
using UnityEngine;
using Laubrary.Audio;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

/// <summary>Kept lifecycle and serialization checks, without a menu or live graph/quit operations.</summary>
public static class AudioCoreLifetimeCheck {
    public static string Execute() {
        var empty = SapChainLayout.Create(new AudioChainTables(), Allocator.TempJob);
        var emptyProcessor = AudioChainProcessor.Create(in empty, 48000, Allocator.TempJob);
        var samples = new NativeArray<float>(new[] { 0.1f, 0.2f, 0.3f }, Allocator.TempJob);
        var other = new NativeArray<float>(new[] { -0.1f, -0.2f, -0.3f }, Allocator.TempJob);
        try {
            Require(empty.nodeCount == 0 && empty.paramCount == 0 && empty.nodeType.Length == 1 && emptyProcessor.arena.Length == 1, "empty logical counts and backing allocations");
            var context = new ChainProcessContext { effects = new VoiceContext { sampleRate = 48000, sourcePeak = 1f } };
            emptyProcessor.Process(in empty, samples, other, 1, 1, in context);
            Require(samples[1] == 0.2f && other[1] == -0.2f && emptyProcessor.elapsedSamples == 1, "generic empty chain in place");
        }
        finally { emptyProcessor.Dispose(); empty.Dispose(); samples.Dispose(); other.Dispose(); }
        Require(SapLifetime.DefaultSettleSeconds(1, 48000) == 0.005d && SapLifetime.DefaultSettleSeconds(8192, 48000) == 0.060d, "settle clamps");
        Require(Math.Abs(SapLifetime.DefaultSettleSeconds(0, 0) - 0.0462d) < 1e-12, "settle fallback");
        var observation = new SapQuietWindow();
        Require(!observation.Observe(2, 0, 0.01) && !observation.Observe(2, 0.009, 0.01) && observation.Observe(2, 0.011, 0.01), "stable even window");
        Require(!observation.Observe(3, 0.012, 0.01) && !observation.Observe(4, 0.02, 0.01) && !observation.Observe(6, 0.025, 0.01) && !observation.Observe(6, 0.03, 0.01) && observation.Observe(6, 0.036, 0.01), "odd/moving restarts window");
        Require(SapLifetime.WaitUntilQuiet(2, i => 2, 0.002, 0.05), "quiet wait success");
        Require(!SapLifetime.WaitUntilQuiet(1, i => 3, 0.002, 0.01), "odd timeout refusal");
        long moving = 0;
        Require(!SapLifetime.WaitUntilQuiet(1, i => moving += 2, 0.002, 0.01), "moving timeout refusal");
        var ticket = SapRenderTicket.Create(Allocator.TempJob);
        try {
            SapRenderTicket.Enter(ticket); Require(ticket[0] == 1, "ticket enters odd");
            SapRenderTicket.Exit(ticket, 64, false); Require(ticket[0] == 2 && ticket[1] == 64 && ticket[2] == 0, "ticket exits even with frames");
            SapRenderTicket.Enter(ticket); SapRenderTicket.Exit(ticket, 32, true);
            SapRenderTicket.Enter(ticket); SapRenderTicket.Exit(ticket, 64, true);
            Require(ticket[0] == 6 && ticket[1] == 160 && ticket[2] == 96, "terminal marker stable");
            var pcm = new PcmClip { channels = 2, frequency = 48000, frames = 8, samples = new float[16], peak = 1f, valid = true };
            var voice = SapRealtimeVoice.Create(pcm, ChainLayout.Empty, 48000, 0, 8, 1f, 1f, 8f / 48000, false, 1, false, Allocator.TempJob, renderTicket: ticket);
            voice.Dispose(); Require(ticket.IsCreated && SapRenderTicket.Read(ticket) == 6, "ticket survives voice disposal");
        }
        finally { ticket.Dispose(); }
        var drain = new SapQuitDrain(); drain.Begin(10);
        Require(drain.RefuseNewRendering && !drain.Tick(10.31) && drain.Tick(10.32) && drain.complete && !drain.Tick(11), "two frames before quit");
        drain = default; drain.Begin(10);
        Require(!drain.Tick(10.01) && !drain.Tick(10.1) && !drain.Tick(10.299) && drain.Tick(10.301), "elapsed floor before quit");
        EnumValues<ZoundEffectType>(16); EnumValues<ZoundModifierType>(5); EnumValues<ModifierOp>(3);
        EnumValues<LfoShape>(4); EnumValues<LfoMode>(2); EnumValues<StepTiming>(2); EnumValues<StepOrder>(2); EnumValues<ModulationCombine>(7); EnumValues<ParamCurve>(5);
        var chain = new ZoundEffectChain();
        chain.nodes.Add(new ZoundEffectNode(ZoundEffectType.TransientShaper) { uid = "node" });
        chain.modifiers.Add(new ZoundModifier(ZoundModifierType.Code) { zpocId = "macro", zpocMode = ZpocMode.Set, zpocRest = 0.2f, uid = "modifier" });
        chain.modifiers[0].curve.GetPoint(0).randomX = 0.17f; chain.modifiers[0].curve.GetPoint(0).editState = ZUIEnvelopeEditState.YEditable;
        chain.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = 0, paramIndex = 0, combine = ModulationCombine.Ratio, op = ModifierOp.Replace, schema = 2 });
        string json = JsonUtility.ToJson(chain);
        var portable = ZoundsAudioCoreData.ToAudioData(chain); var roundtrip = ZoundsAudioCoreData.ToZoundsData(portable);
        Require(JsonUtility.ToJson(roundtrip) == json, "portable authoring data roundtrip must preserve every field");
        Require(json.Contains("\"type\":15") && json.Contains("\"type\":4") && json.Contains("\"combine\":5") && json.Contains("\"op\":2"), "enum int serialization");
        Require(typeof(ZoundEffectType).Assembly == typeof(AudioChainProcessor).Assembly, "enums live in AudioCore");
        return "PASS generic empty array layout/in-place processing; settle clamp/fallback; stable-even and moving/odd timeout refusal; ticket frame/terminal/retirement ownership; two-frame and 0.3s quit gate; nine enum ranges; exact portable chain JSON roundtrip.";
    }
    static void EnumValues<T>(int count) where T : Enum {
        var values = Enum.GetValues(typeof(T)); Require(values.Length == count, "enum count " + typeof(T).Name);
        for (int i = 0; i < count; i++) Require(Convert.ToInt32(values.GetValue(i)) == i, "enum value " + typeof(T).Name);
    }
    static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
