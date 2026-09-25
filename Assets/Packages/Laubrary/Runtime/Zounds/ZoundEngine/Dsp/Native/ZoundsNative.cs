using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Laubrary.Zounds.Dsp.Native {

    /// <summary>
    /// The boundary to the native DSP engine (<c>audiopluginZoundsNative</c>).
    ///
    /// Two kinds of traffic cross it, and the split is deliberate. Structural calls
    /// — upload a clip, upload a chain layout, start or stop a node, drain events —
    /// go through P/Invoke below. Per-frame state does not: the plugin allocates one
    /// control block at init and hands over its address, and C# reads and writes the
    /// per-node fields there directly through pointers. That is the same
    /// publish-by-release-store handshake the managed voices used, moved into native
    /// memory so the thread touching it is not a GC mutator — which is the entire
    /// point of the port.
    ///
    /// Everything here is main-thread only. Nothing in this file may be called from
    /// an audio callback, because there is no longer a managed audio callback to call
    /// it from: the old <c>ZoundBusFilter</c> and its <c>OnAudioFilterRead</c> are gone
    /// on purpose, and must stay gone. One managed audio callback anywhere in the
    /// process permanently attaches Unity's mixer thread to the garbage collector,
    /// including for audio that renders natively.
    /// </summary>
    public static class ZoundsNative {

        public const string DLL = "audiopluginZoundsNative";

        /// <summary>Bumped in ZoundsCore.h whenever the shared structs change. A mismatch refuses to run.</summary>
        public const int EXPECTED_ABI = 4;

        // ─────────────────────────── structs ───────────────────────────
        // Field for field with ZoundsCore.h. Offsets and sizes are asserted against
        // the plugin's own sizeof at init rather than trusted, because a silent
        // mismatch here is memory corruption on the audio thread.

        [StructLayout(LayoutKind.Sequential)]
        public struct NodeControl {
            // written by the audio thread, read here
            public int state;
            public int flags;
            public float lastPeak;
            public int repeatsDone;
            public int repeatsTotal;
            public int onsetTotal;
            public long trainEndSample;
            public long elapsedSamples;
            // written here, read by the audio thread
            public int killRequest;
            public int releaseRequest;
            public int pauseRequest;
            public float basePitchTarget;
            public float outGainTarget;
            // published with the node, read-only afterwards
            public long tokenId;
            public int busIndex;
            public int groupIndex;
            public int depth;
            public int layoutId;
            public int liveChildren;
            public int slotsStolen;
            public long _pad1a;
            public long _pad1b;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BusStatus {
            public long callbackCount;
            public long maxCallbackTicks;
            public long lastCallbackTicks;
            public long maxGapTicks;
            public long lateCallbacks;
            public long stallCount;
            public float lastPeak;
            public int lastFrames;
            public int lastChannels;
            public int lastNodes;
            public int bound;
            public int _pad;
        }

        /// <summary>The fixed header of the control block; the bus and node arrays follow it at offsets the plugin reports.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ControlHeader {
            public int abiVersion;
            public int sampleRate;
            public int maxBlock;
            public int nodeCount;
            public long dspSampleClock;
            public long playsDropped;
            public long voicesStolen;
            public long groupsDropped;
            public long renderFaults;
            public long qpcFrequency;
            public long eventsDropped;
            public int testTone;
            public float testToneFrequency;
            public float testToneAmplitude;
            public int running;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PrepareArgs {
            public long tokenId;
            public int busIndex;
            public int groupIndex;
            public int depth;
            public int pcmId;
            public int layoutId;
            public int isGroup;
            public double startFrame;
            public double endFrame;
            public float basePitch;
            public float outGain;
            public float sourceDuration;
            public int loop;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NativeRepeatPlan {
            public int enabled;
            public int count;
            public long intervalSamples;
            public int spaceFromEnd;
            public int retrigger;
            public float pitchMulMin, pitchMulMax, gainMulMin, gainMulMax;
            public long durationLimitSamples;
            public long nominalLengthSamples;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NativeEvent {
            public int type;
            public int nodeId;
            public long tokenId;
            public long dspSample;
        }

        public const int EV_AUDIO_END = 0;

        // ─────────────────────────── imports ───────────────────────────

        [DllImport(DLL)] public static extern int Zounds_GetAbiVersion();
        [DllImport(DLL)] public static extern int Zounds_GetControlBlockSize();
        [DllImport(DLL)] public static extern int Zounds_GetNodeControlSize();
        [DllImport(DLL)] public static extern int Zounds_GetBusStatusSize();
        [DllImport(DLL)] public static extern int Zounds_GetNodesOffset();
        [DllImport(DLL)] public static extern int Zounds_GetBusesOffset();

        [DllImport(DLL)] public static extern int Zounds_Init(int sampleRate, int maxBlock);
        [DllImport(DLL)] public static extern void Zounds_Shutdown();
        [DllImport(DLL)] public static extern IntPtr Zounds_GetControlBlock();

        [DllImport(DLL)] public static extern int Zounds_UploadPcm(int id, float[] samples, int frames, int channels, int frequency, float peak);
        [DllImport(DLL)] public static extern void Zounds_ReleasePcm(int id);
        [DllImport(DLL)] public static extern int Zounds_UploadLayout(int id, byte[] blob, int size);
        [DllImport(DLL)] public static extern void Zounds_ReleaseLayout(int id);

        [DllImport(DLL)] public static extern int Zounds_PrepareNode(int nodeId, ref PrepareArgs args);
        [DllImport(DLL)] public static extern void Zounds_SetRepeat(int nodeId, ref NativeRepeatPlan plan);
        [DllImport(DLL)] public static extern void Zounds_SeedModifier(int nodeId, int modifier, int slot, float value);
        [DllImport(DLL)] public static extern void Zounds_PublishNode(int nodeId);
        [DllImport(DLL)] public static extern void Zounds_PushLiveParam(int nodeId, int flatIndex, float value);
        [DllImport(DLL)] public static extern void Zounds_ForceFree(int nodeId);
        [DllImport(DLL)] public static extern void Zounds_SetSourceDuration(int nodeId, float seconds);
        [DllImport(DLL)] public static extern float Zounds_ReadNodeState(int nodeId, int offset);
        [DllImport(DLL)] public static extern int Zounds_PopEvent(ref NativeEvent e);
        [DllImport(DLL)] public static extern void Zounds_Collect();
        [DllImport(DLL)] public static extern int Zounds_EffectImplemented(int type);
        [DllImport(DLL)] public static extern void Zounds_RenderOffline(int busIndex, float[] buffer, int frames, int channels);

        // ─────────────────────────── the control block ───────────────────────────

        private static IntPtr block = IntPtr.Zero;
        private static int nodesOffset;
        private static int busesOffset;
        private static int nodeStride;
        private static int busStride;

        /// <summary>Null until <see cref="Initialise"/> has succeeded; every accessor below is unusable before that.</summary>
        public static bool Available => block != IntPtr.Zero;

        /// <summary>Set when the plugin could not be loaded or refused to match; reported once, then the engine stays silent.</summary>
        public static string LoadError { get; private set; }

        /// <summary>
        /// Brings the native engine up and binds the shared control block. Safe to call
        /// repeatedly; a sample-rate change re-publishes the rate without disturbing the pools.
        /// </summary>
        public static bool Initialise(int sampleRate, int maxBlock) {
            if (LoadError != null) return false;
            try {
                int abi = Zounds_GetAbiVersion();
                if (abi != EXPECTED_ABI) {
                    LoadError = "native plugin ABI " + abi + " does not match the " + EXPECTED_ABI + " this build expects; rebuild NativeAudio/ZoundsNative.";
                    Debug.LogError("[Zounds] " + LoadError);
                    return false;
                }
                if (Zounds_Init(sampleRate, maxBlock) == 0) {
                    LoadError = "native engine refused to initialise.";
                    Debug.LogError("[Zounds] " + LoadError);
                    return false;
                }
                nodeStride = Zounds_GetNodeControlSize();
                busStride = Zounds_GetBusStatusSize();
                nodesOffset = Zounds_GetNodesOffset();
                busesOffset = Zounds_GetBusesOffset();
                int managedNode = Marshal.SizeOf(typeof(NodeControl));
                int managedBus = Marshal.SizeOf(typeof(BusStatus));
                if (managedNode != nodeStride || managedBus != busStride) {
                    LoadError = "control-block layout mismatch: NodeControl " + managedNode + " vs " + nodeStride
                              + ", BusStatus " + managedBus + " vs " + busStride + ". The C# mirrors in ZoundsNative.cs have drifted from ZoundsCore.h.";
                    Debug.LogError("[Zounds] " + LoadError);
                    return false;
                }
                block = Zounds_GetControlBlock();
                if (block == IntPtr.Zero) {
                    LoadError = "native engine published no control block.";
                    Debug.LogError("[Zounds] " + LoadError);
                    return false;
                }
                return true;
            }
            catch (DllNotFoundException) {
                LoadError = "audiopluginZoundsNative was not found. Build NativeAudio/ZoundsNative and copy the DLL to Assets/Zounds/Plugins/x86_64/, then restart the editor.";
                Debug.LogError("[Zounds] " + LoadError);
                return false;
            }
            catch (EntryPointNotFoundException e) {
                LoadError = "the loaded audiopluginZoundsNative is older than this C# build (" + e.Message + "). Rebuild it and restart the editor — Unity holds the DLL open, so an overwrite alone does not take.";
                Debug.LogError("[Zounds] " + LoadError);
                return false;
            }
        }

        public static void Shutdown() {
            if (block == IntPtr.Zero) return;
            Zounds_Shutdown();
            block = IntPtr.Zero;
        }

        public static unsafe NodeControl* Node(int nodeId) {
            return (NodeControl*)((byte*)block.ToPointer() + nodesOffset + (long)nodeId * nodeStride);
        }

        public static unsafe BusStatus* Bus(int busIndex) {
            return (BusStatus*)((byte*)block.ToPointer() + busesOffset + (long)busIndex * busStride);
        }

        public static unsafe ControlHeader* Header() {
            return (ControlHeader*)block.ToPointer();
        }

        /// <summary>Flat node ids: voices first, then groups. Mirrors the native pool exactly.</summary>
        public static int VoiceNodeId(int voiceIndex) => voiceIndex;
        public static int GroupNodeId(int groupIndex) => ZoundDspConstants.MAX_VOICES + groupIndex;
        public static bool IsGroupNode(int nodeId) => nodeId >= ZoundDspConstants.MAX_VOICES;
    }
}
