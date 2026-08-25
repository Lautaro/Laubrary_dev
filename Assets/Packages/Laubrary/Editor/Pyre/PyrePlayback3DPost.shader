// Pyre/Playback3DPost — the post-processing chain the Playback3D preview needs in order to look like the
// VFX pack it is previewing, rather than like a raw un-graded particle render.
//
// WHY THIS EXISTS: VFX packs (Vefects Fire VFX, Hovl, etc.) author their fire/explosion materials with emissive
// intensities far above 1 (the Vefects fire materials use 33 and 123) and ship a demo scene whose Global Volume
// applies Bloom + ACES Tonemapping. Without that grade the same particles render as flat, clipped, over-saturated
// yellow with hard edges — which is exactly what the Playback3D preview used to show, and why it "looked nothing
// like the pack". A PreviewRenderUtility scene gets no URP Volume stack (and a global Volume placed in it would
// leak into every other camera in the editor), so the grade is reproduced here as an explicit blit chain instead.
//
// Values are matched to the pack's own PostProProfileNew: Bloom threshold 1, intensity 1, scatter 0.7, ACES tonemap.
Shader "Hidden/Laubrary/Pyre/Playback3DPost"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        sampler2D _BloomTex;      // the accumulated blur being combined in (upsample + composite passes)
        float4 _Filter;           // x = threshold, y = knee, z = 1/(4*knee), w = unused
        float4 _Params;           // x = bloom intensity, y = exposure, z = unused, w = unused
        float4 _Tint;             // final multiply — the layer's Tint dial, applied here instead of destroying
                                  // the prefab's authored per-particle start colours
        float _Sample;            // downsample/upsample spread in source texels

        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        v2f vert(appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            return o;
        }

        // A 4-tap bilinear box, offset by `spread` source texels — cheap and plenty for a preview-grade blur.
        float3 Box4(sampler2D tex, float2 uv, float2 texel, float spread)
        {
            float4 d = float4(-texel.x, -texel.y, texel.x, texel.y) * spread;
            float3 s = tex2D(tex, uv + d.xy).rgb;
            s += tex2D(tex, uv + d.zy).rgb;
            s += tex2D(tex, uv + d.xw).rgb;
            s += tex2D(tex, uv + d.zw).rgb;
            return s * 0.25;
        }

        // ACES, the FULL fit (Stephen Hill's ACEScg matrices + RRT/ODT curve) — deliberately NOT the cheaper
        // one-line Narkowicz approximation, which was tried first and is wrong for this job: it tonemaps each
        // channel independently, so a fire core measured here at RGB (24.9, 14.4, 0.0) stays a flat saturated
        // yellow. The matrix fit desaturates highlights toward white the way URP's own ACES Tonemapping does,
        // which is what gives the white-hot centre with an orange fringe the pack's demo scene shows.
        static const float3x3 ACESIn = float3x3(
            0.59719, 0.35458, 0.04823,
            0.07600, 0.90834, 0.01566,
            0.02840, 0.13383, 0.83777);
        static const float3x3 ACESOut = float3x3(
             1.60475, -0.53108, -0.07367,
            -0.10208,  1.10813, -0.00605,
            -0.00327, -0.07276,  1.07602);

        float3 RRTAndODTFit(float3 v)
        {
            float3 a = v * (v + 0.0245786) - 0.000090537;
            float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
            return a / b;
        }

        float3 ACES(float3 x)
        {
            x = mul(ACESIn, max(x, 0.0));
            x = RRTAndODTFit(x);
            return saturate(mul(ACESOut, x));
        }
        ENDCG

        // 0 — prefilter: soft-knee threshold, so only the emissive fire (not the black smoke) seeds the bloom.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                float br = max(c.r, max(c.g, c.b));
                float soft = clamp(br - _Filter.x + _Filter.y, 0.0, 2.0 * _Filter.y);
                soft = soft * soft * _Filter.z;
                float contrib = max(soft, br - _Filter.x) / max(br, 0.0001);
                return float4(c * contrib, 1);
            }
            ENDCG
        }

        // 1 — downsample (blur while shrinking).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                return float4(Box4(_MainTex, i.uv, _MainTex_TexelSize.xy, _Sample), 1);
            }
            ENDCG
        }

        // 2 — upsample and accumulate: blurred smaller mip + the level captured on the way down. This is what
        // gives the wide soft halo (scatter) instead of a single tight glow ring.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float3 up = Box4(_MainTex, i.uv, _MainTex_TexelSize.xy, _Sample);
                float3 keep = tex2D(_BloomTex, i.uv).rgb;
                return float4(up + keep, 1);
            }
            ENDCG
        }

        // 3 — composite: base + bloom, exposure, Tint, ACES, then back to sRGB-ish for display.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float4 base = tex2D(_MainTex, i.uv);
                float3 bloom = tex2D(_BloomTex, i.uv).rgb;
                float3 c = (base.rgb + bloom * _Params.x) * _Params.y;
                // Tint is applied AFTER the tonemap, not before. Pre-tonemap it would be swallowed on anything
                // bright — the fire core measures ~25 here, and ACES saturates it to white whatever hue you feed
                // in, so the dial would appear to do nothing on exactly the part of the image you aimed it at.
                c = ACES(c) * _Tint.rgb;
                // Alpha is carried through as the effect's own coverage (max of colour and the source alpha) so
                // the preview composites over the Pyre backdrop the same way every other shape does.
                float a = saturate(max(base.a, max(c.r, max(c.g, c.b))));
                return float4(c, a);
            }
            ENDCG
        }

        // 4 — pixel-grid quantise: optional posterisation of the already-downsampled grid. Without it a shrunken
        // render is a small photo, not pixel art: the smooth 8-bit gradient across the fire is what reads as
        // "downscaled screenshot". Snapping to a few levels per channel is what gives it authored-looking bands.
        // _Params.z = levels per channel; anything below 2 passes the colour through untouched.
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                float levels = _Params.z;
                if (levels >= 2.0) c.rgb = floor(c.rgb * levels + 0.5) / levels;
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
