// Colours each road texel of a LatticePixelView from its reveal mask and the
// shared per-pixel vision test (LatticeVision.hlsl). Non-road texels are
// discarded.
Shader "Laubrary/LatticePixels"
{
    Properties
    {
        _RoadMask ("Road Mask", 2D) = "black" {}
        _RevealMask ("Reveal Mask", 2D) = "black" {}
        _ShadowColor ("Shadow", Color) = (0.08, 0.08, 0.10, 1)
        _RevealedColor ("Revealed", Color) = (0.85, 0.85, 0.85, 1)
        _VisibleColor ("Visible", Color) = (0.30, 0.55, 0.85, 1)
        _BothColor ("Revealed And Visible", Color) = (1.00, 0.95, 0.40, 1)
        _Origin ("Origin (plane u, v)", Vector) = (0, 0, 0, 0)
        _Size ("Size (plane u, v)", Vector) = (1, 1, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "LatticePixels"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "LatticeVision.hlsl"

            TEXTURE2D(_RoadMask);   SAMPLER(sampler_RoadMask);
            TEXTURE2D(_RevealMask); SAMPLER(sampler_RevealMask);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half4 _RevealedColor;
                half4 _VisibleColor;
                half4 _BothColor;
                float4 _Origin;
                float4 _Size;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Masks hold 0 or 1 in an 8-bit channel: any non-zero value is set.
                half road = SAMPLE_TEXTURE2D(_RoadMask, sampler_RoadMask, i.uv).r;
                if (road < 0.002) discard;
                half revealed = SAMPLE_TEXTURE2D(_RevealMask, sampler_RevealMask, i.uv).r;

                bool visible = LatticeVisible(_Origin.xy + i.uv * _Size.xy);
                bool rev = revealed > 0.002;
                if (rev) return visible ? _BothColor : _RevealedColor;
                return visible ? _VisibleColor : _ShadowColor;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
