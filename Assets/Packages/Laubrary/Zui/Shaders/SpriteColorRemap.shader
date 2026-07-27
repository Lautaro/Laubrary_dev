Shader "Laubrary/ZUI/SpriteColorRemap"
{
    // A URP sprite shader that recolours a sprite by REMAPPING its source colours (the GPU/live twin of the managed
    // ColorRemapModifier). Up to MAXREGIONS regions are pushed as uniforms; each grabs a hue cluster (chromatic:
    // by HUE window) or a grey range (by VALUE window) and maps it to a flat swatch OR a luma-indexed gradient. The
    // gradient rows live in a LUT ATLAS (_GradLUT, one row per region, baked from ZuiGradient.ToLut). A cycling
    // region scrolls its row by _Phase * cycleSpeed. Colours matching no region pass through (or drop). The match
    // math mirrors ColorRemapRegion.TryScore so the live result reads the same as the CPU bake.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _GradLUT ("Gradient LUT atlas", 2D) = "white" {}
        _Phase ("Phase", Float) = 0
        _RegionCount ("Region count", Float) = 0
        _DropUnmatched ("Drop unmatched", Float) = 0
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
        }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAXREGIONS 8

            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_GradLUT); SAMPLER(sampler_GradLUT);

            CBUFFER_START(UnityPerMaterial)
                // x = hueCenter (chromatic) or valueCenter (grey); y = match window; z = minSat;
                // w = code: 0 = chromatic+flat, 1 = chromatic+gradient, 2 = grey+flat, 3 = grey+gradient
                float4 _RegionA[MAXREGIONS];
                // x = lumaLow, y = lumaHigh, z = cycleSpeed (0 = static), w = atlas row index
                float4 _RegionB[MAXREGIONS];
                float4 _RegionColor[MAXREGIONS];   // flat-swatch RGBA (used when the region is flat)
                float _Phase;
                float _RegionCount;
                float _DropUnmatched;
                float4 _Color;
            CBUFFER_END

            // Standard RGB→HSV (Sam Hocevar's branchless form). Visually matches Unity's Color.RGBToHSV.
            float3 Rgb2Hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float3 hsv = Rgb2Hsv(c.rgb);
                float hue = hsv.x, sat = hsv.y;
                float luma = dot(c.rgb, float3(0.299, 0.587, 0.114));
                half aOut = c.a * IN.color.a * _Color.a;

                int count = (int)(_RegionCount + 0.5);
                int best = -1;
                float bestScore = 1e9;

                [loop] for (int i = 0; i < MAXREGIONS; i++)
                {
                    if (i >= count) break;
                    float4 A = _RegionA[i];
                    bool isGrey = A.w >= 2.0;
                    float score = 1e9;
                    bool match = false;
                    if (isGrey)
                    {
                        if (sat <= A.z) { float dv = abs(luma - A.x); if (dv <= A.y) { match = true; score = dv; } }
                    }
                    else
                    {
                        if (sat >= A.z) { float hd = abs(frac(hue - A.x + 0.5) - 0.5); if (hd <= A.y) { match = true; score = hd; } }
                    }
                    if (match && score < bestScore) { bestScore = score; best = i; }
                }

                if (best < 0)
                {
                    if (_DropUnmatched > 0.5) return half4(0, 0, 0, 0);
                    return half4(c.rgb * _Color.rgb, aOut);
                }

                float4 A = _RegionA[best];
                float4 B = _RegionB[best];
                bool isGrad = (A.w == 1.0 || A.w == 3.0);
                float3 outc;
                if (isGrad)
                {
                    float t = saturate((luma - B.x) / max(1e-4, B.y - B.x));
                    float u = (B.z > 0.0) ? frac(t + _Phase * B.z) : t;   // cycle only when cycleSpeed > 0
                    float v = (B.w + 0.5) / max(1.0, _RegionCount);       // exact row centre → no atlas row bleed
                    outc = SAMPLE_TEXTURE2D(_GradLUT, sampler_GradLUT, float2(u, v)).rgb;
                }
                else
                {
                    outc = _RegionColor[best].rgb;
                }
                return half4(outc * _Color.rgb, aOut);
            }
            ENDHLSL
        }
    }
    Fallback "Sprites/Default"
}
