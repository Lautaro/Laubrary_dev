using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0115 -- a content-addressed identity for one node's own output (never a memory address, never a
    /// Unity InstanceID, never a serialized-asset GUID): two independent 64-bit FNV-1a lanes mixed with
    /// different multipliers, so a collision needs both lanes to agree by chance. Good enough for an
    /// in-process cache where a false HIT (two different inputs landing on the same key) is the only failure
    /// mode that matters for correctness, and a false MISS (an extra recompute) only costs performance.
    ///
    /// Deliberately NOT <c>System.HashCode</c> / <c>string.GetHashCode</c>: both are randomised per process in
    /// modern .NET (hash-flooding mitigation), which is fine for a same-session cache but makes "same input ->
    /// same key" unrepeatable across test runs and across the editor being restarted. FNV-1a here is seeded by
    /// fixed offset-basis constants, so the SAME inputs produce the SAME key in every process, on every
    /// machine -- which is what a unit test asserting an exact key value needs.
    /// </summary>
    public readonly struct ShaperCacheKey : IEquatable<ShaperCacheKey>
    {
        public readonly ulong lane0;
        public readonly ulong lane1;

        public ShaperCacheKey(ulong lane0, ulong lane1) { this.lane0 = lane0; this.lane1 = lane1; }

        /// <summary>
        /// The canonical key for "nothing": a disabled node, an empty bag, a null child slot. Every disabled
        /// node collapses to exactly this ONE key regardless of its other authored fields -- correct, because
        /// <see cref="ShaperCompiler"/> already treats a disabled node as contributing nothing (EmitNode's
        /// caller skips it entirely), so two disabled nodes with different dial values are the SAME case for
        /// caching purposes and must not be told apart by a cache that doesn't know what "disabled" means.
        /// </summary>
        public static readonly ShaperCacheKey Empty = new ShaperCacheKey(0xcbf29ce484222325UL, 0x100000001b3UL);

        public bool Equals(ShaperCacheKey other) => lane0 == other.lane0 && lane1 == other.lane1;
        public override bool Equals(object obj) => obj is ShaperCacheKey k && Equals(k);
        public override int GetHashCode() => unchecked((int)(lane0 ^ (lane1 * 0x9E3779B97F4A7C15UL)));
        public override string ToString() => lane0.ToString("x16") + lane1.ToString("x16");
        public static bool operator ==(ShaperCacheKey a, ShaperCacheKey b) => a.Equals(b);
        public static bool operator !=(ShaperCacheKey a, ShaperCacheKey b) => !a.Equals(b);
    }

    /// <summary>
    /// T-0115 -- a running mix. <c>Begin(salt)</c> starts a fresh state tagged by a short literal (so a Bag's
    /// own hash space can never collide with a Primitive's own hash space even if every OTHER field happened
    /// to match); fold in every field that is part of an identity with the <c>Mix*</c> calls, in a FIXED
    /// order (order matters -- two different field orders are two different hashes, which is fine as long as
    /// every call site is internally consistent, which is why every node-hash function lives in ONE place,
    /// <see cref="ShaperLayerKey"/>, rather than being reimplemented per call site); read the result with
    /// <see cref="Key"/>.
    ///
    /// A mutable struct on purpose -- each <c>Mix*</c> call mutates the two ulong lanes in place and is meant
    /// to be called as a sequence of statements on one local variable, not chained through the return value
    /// (the fluent return exists for convenience only; chaining it is harmless but copies the struct on every
    /// call, so plain sequential statements are the cheaper and clearer style used throughout this codebase).
    /// </summary>
    public struct ShaperCacheMixer
    {
        ulong h0, h1;
        const ulong Prime0 = 1099511628211UL;      // standard FNV-1a 64-bit prime
        const ulong Prime1 = 1099511628213UL;      // a different odd multiplier so lane 1 decorrelates from lane 0

        public static ShaperCacheMixer Begin(string salt)
        {
            var m = new ShaperCacheMixer { h0 = 0xcbf29ce484222325UL, h1 = 0x100000001b3UL };
            m.MixString(salt);
            return m;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void MixByte(byte b)
        {
            h0 = unchecked((h0 ^ b) * Prime0);
            h1 = unchecked((h1 ^ b) * Prime1);
        }

        public ShaperCacheMixer MixString(string s)
        {
            if (string.IsNullOrEmpty(s)) { MixByte(0); return this; }
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                MixByte((byte)(c & 0xFF));
                MixByte((byte)(c >> 8));
            }
            MixByte(0xFF);
            return this;
        }

        public ShaperCacheMixer MixFloat(float f)
        {
            // Canonicalise -0 to +0 so two numerically-equal-but-differently-signed zeros never diverge.
            if (f == 0f) f = 0f;
            uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(f));
            MixByte((byte)bits); MixByte((byte)(bits >> 8)); MixByte((byte)(bits >> 16)); MixByte((byte)(bits >> 24));
            return this;
        }

        public ShaperCacheMixer MixInt(int i) => MixUInt(unchecked((uint)i));

        public ShaperCacheMixer MixUInt(uint u)
        {
            MixByte((byte)u); MixByte((byte)(u >> 8)); MixByte((byte)(u >> 16)); MixByte((byte)(u >> 24));
            return this;
        }

        public ShaperCacheMixer MixBool(bool b) { MixByte(b ? (byte)1 : (byte)0); return this; }
        public ShaperCacheMixer MixVector2(Vector2 v) { MixFloat(v.x); MixFloat(v.y); return this; }

        public ShaperCacheMixer MixKey(ShaperCacheKey k)
        {
            MixUInt((uint)k.lane0); MixUInt((uint)(k.lane0 >> 32));
            MixUInt((uint)k.lane1); MixUInt((uint)(k.lane1 >> 32));
            return this;
        }

        public ShaperCacheKey Key => new ShaperCacheKey(h0, h1);
    }
}
