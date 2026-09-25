using System.Collections.Generic;
using System.IO;

namespace Laubrary.Zounds.Dsp.Native {

    /// <summary>
    /// Serializes a built <see cref="ChainLayout"/> into the flat blob the native engine
    /// parses (see parseLayoutBlob in ZoundsEngine.cpp). Little-endian, tightly packed,
    /// no alignment assumptions — the reader walks it through a bounds-checked cursor,
    /// so a malformed blob is refused rather than becoming an out-of-bounds read on the
    /// audio thread.
    ///
    /// This is the only thing about a chain that crosses the boundary. The authored
    /// model (nodes, modifiers, bindings, presets, the editor UI) does not change and
    /// does not travel: it is flattened into a ChainLayout on the main thread exactly as
    /// before, and only that flattening is handed over.
    ///
    /// The format is versioned. Changing it means changing BLOB_VERSION here, in
    /// ZoundsEngine.cpp and in the harness's own writer — the harness deliberately keeps
    /// a separate writer so a drift shows up as a failing check rather than two halves
    /// agreeing on something wrong.
    /// </summary>
    public static class NativeChainBlob {

        private const int MAGIC = 0x54594C5A;   // 'ZLYT'
        private const int VERSION = 1;

        [System.ThreadStatic] private static MemoryStream stream;
        [System.ThreadStatic] private static BinaryWriter writer;

        /// <summary>
        /// Writes <paramref name="layout"/> into a byte array. Allocates — main thread only,
        /// and only when a layout is first uploaded or its chain has been edited.
        /// </summary>
        public static byte[] Write(ChainLayout layout) {
            if (stream == null) { stream = new MemoryStream(4096); writer = new BinaryWriter(stream); }
            stream.SetLength(0);
            stream.Position = 0;
            var w = writer;

            w.Write(MAGIC);
            w.Write(VERSION);
            w.Write(layout.sourceVersion);
            w.Write(layout.nodeCount);
            w.Write(layout.paramCount);
            w.Write(layout.rampedCount);
            w.Write(layout.modCount);
            w.Write(layout.bindCount);
            w.Write(layout.stateFloats);
            w.Write(layout.heavy ? 1 : 0);
            w.Write(layout.tailSeconds);
            w.Write(layout.pitchModulated ? 1 : 0);

            for (int i = 0; i < layout.nodeCount; i++) {
                w.Write((int)layout.nodeType[i]);
                w.Write(layout.enabled[i] ? 1 : 0);
                w.Write(layout.stateOffset[i]);
                w.Write(layout.paramOffset[i]);
                w.Write(layout.paramCountOf[i]);
            }
            for (int i = 0; i < layout.paramCount; i++) w.Write(layout.pBase[i]);
            for (int i = 0; i < layout.paramCount; i++) w.Write(layout.pMin[i]);
            for (int i = 0; i < layout.paramCount; i++) w.Write(layout.pMax[i]);
            for (int i = 0; i < layout.rampedCount; i++) w.Write(layout.ramped[i]);

            for (int m = 0; m < layout.modCount; m++) {
                w.Write((int)layout.modType[m]);
                w.Write(layout.modStateOffset[m]);
                w.Write(layout.modExtraSeconds[m]);
                var mp = layout.modParams[m];
                int pc = mp != null ? mp.Length : 0;
                if (pc > 16) pc = 16;                       // the native side caps a modifier at 16 parameters
                w.Write(pc);
                for (int k = 0; k < pc; k++) w.Write(mp[k]);
                var curve = layout.modCurve[m];
                int cc = curve != null ? curve.Length : 0;
                w.Write(cc);
                for (int k = 0; k < cc; k++) { w.Write(curve[k].time); w.Write(curve[k].value); w.Write(curve[k].exponent); }
                var steps = layout.modSteps[m];
                int sc = steps != null ? steps.Length : 0;
                w.Write(sc);
                for (int k = 0; k < sc; k++) w.Write(steps[k]);
            }

            for (int b = 0; b < layout.bindCount; b++) {
                w.Write(layout.bindModifier[b]);
                w.Write(layout.bindTarget[b]);
                w.Write((int)layout.bindOp[b]);
                w.Write(layout.bindDepth[b]);
            }

            w.Flush();
            return stream.ToArray();
        }

        // ── layout ids ──
        //
        // A ChainLayout is immutable once built, and a chain edit produces a new one, so
        // an id is handed out per layout instance and kept with it. Ids are not recycled
        // within a session: the native side keeps a retired layout alive until nothing
        // plays it, and reusing an id would make "is this the layout I uploaded" ambiguous.

        private static readonly Dictionary<ChainLayout, int> ids = new Dictionary<ChainLayout, int>();
        private static int nextId = 1;

        /// <summary>The native id for a layout, uploading it on first use. 0 means the upload failed.</summary>
        public static int IdFor(ChainLayout layout) {
            if (layout == null || !ZoundsNative.Available) return 0;
            if (ids.TryGetValue(layout, out int id)) return id;
            id = nextId++;
            var blob = Write(layout);
            if (ZoundsNative.Zounds_UploadLayout(id, blob, blob.Length) == 0) {
                UnityEngine.Debug.LogError("[Zounds] the native engine refused a chain layout (" + layout.nodeCount + " nodes, "
                    + layout.modCount + " modifiers, " + blob.Length + " bytes); the zound will play without its chain.");
                return 0;
            }
            ids[layout] = id;
            return id;
        }

        /// <summary>Drops every id mapping (a play-mode change, an engine teardown, a project reload).</summary>
        public static void Clear() {
            ids.Clear();
        }
    }
}
