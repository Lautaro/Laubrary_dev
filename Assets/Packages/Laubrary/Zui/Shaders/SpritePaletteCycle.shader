Shader "Laubrary/ZUI/SpritePaletteCycle"
{
    // A URP sprite shader that recolours the sprite through a 1-D palette LUT by the pixel's LUMA, offset by
    // _Phase (colour cycling) and optionally quantised to _Steps discrete bands. Feed _LUT from ZuiGradient.ToLut()
    // and animate _Phase with a ZuiPaletteCycle driver (per-renderer, via MaterialPropertyBlock). Sprite alpha is
    // preserved. Straight-alpha blend so it composites like a normal sprite.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _LUT ("Palette LUT", 2D) = "white" {}
        _Phase ("Phase", Float) = 0
        _Steps ("Quantise (0=off)", Float) = 0
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

            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_LUT);     SAMPLER(sampler_LUT);

            CBUFFER_START(UnityPerMaterial)
                float _Phase;
                float _Steps;
                float4 _Color;
            CBUFFER_END

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
                float luma = dot(c.rgb, half3(0.299, 0.587, 0.114));
                if (_Steps > 0.5) luma = floor(luma * _Steps) / max(1.0, _Steps - 1.0);
                half3 lut = SAMPLE_TEXTURE2D(_LUT, sampler_LUT, float2(frac(luma + _Phase), 0.5)).rgb;
                half a = c.a * IN.color.a * _Color.a;
                return half4(lut * _Color.rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback "Sprites/Default"
}
