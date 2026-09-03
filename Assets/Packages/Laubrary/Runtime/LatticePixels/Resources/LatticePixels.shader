// Colours each road texel of a LatticePixelView from its reveal mask and a
// per-pixel vision cone. Non-road texels are discarded.
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
        _VisionPos ("Vision Position", Vector) = (0, 0, 0, 0)
        _VisionDir ("Vision Direction", Vector) = (0, 1, 0, 0)
        _VisionCos ("Vision Cone Cosine", Float) = 0
        _VisionRange ("Vision Range", Float) = 0
        _VisionOmni ("Vision Omni Radius", Float) = 0
        _VisionOn ("Vision Enabled", Float) = 0
        _VisionMode ("Vision Mode (0 cone, 1 occluded)", Float) = 0
        _VisionHalfAngle ("Vision Half Angle (radians)", Float) = 1.4
        _ShadowMap ("Shadow Map", 2D) = "white" {}
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

            TEXTURE2D(_RoadMask);   SAMPLER(sampler_RoadMask);
            TEXTURE2D(_RevealMask); SAMPLER(sampler_RevealMask);
            TEXTURE2D(_ShadowMap);  SAMPLER(sampler_ShadowMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half4 _RevealedColor;
                half4 _VisibleColor;
                half4 _BothColor;
                float4 _Origin;
                float4 _Size;
                float4 _VisionPos;
                float4 _VisionDir;
                float _VisionCos;
                float _VisionRange;
                float _VisionOmni;
                float _VisionOn;
                float _VisionMode;
                float _VisionHalfAngle;
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

                float2 p = _Origin.xy + i.uv * _Size.xy;
                float2 d = p - _VisionPos.xy;
                float r2 = dot(d, d);
                bool visible = false;
                if (_VisionOn > 0.5)
                {
                    if (r2 <= _VisionOmni * _VisionOmni) visible = true;
                    else if (r2 <= _VisionRange * _VisionRange && r2 > 1e-8)
                    {
                        float2 dir = d * rsqrt(r2);
                        float2 f = normalize(_VisionDir.xy);
                        if (_VisionMode < 0.5)
                        {
                            if (dot(dir, f) >= _VisionCos) visible = true;
                        }
                        else
                        {
                            // Occluded: look the pixel's angle up in the shadow map.
                            float ang = atan2(f.x * dir.y - f.y * dir.x, dot(f, dir));
                            float u = (ang / _VisionHalfAngle + 1.0) * 0.5;
                            if (u >= 0.0 && u <= 1.0)
                            {
                                float reach = SAMPLE_TEXTURE2D(_ShadowMap, sampler_ShadowMap, float2(u, 0.5)).r;
                                if (sqrt(r2) <= reach) visible = true;
                            }
                        }
                    }
                }

                bool rev = revealed > 0.002;
                if (rev) return visible ? _BothColor : _RevealedColor;
                return visible ? _VisibleColor : _ShadowColor;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
