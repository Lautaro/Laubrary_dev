// A sprite clipped by vision, PIXEL BY PIXEL: each fragment tests its own world position against the
// published vision cones (VisionMask.hlsl). Fragments inside a cone draw exactly like URP's
// Sprite-Unlit-Default; fragments outside draw at _HiddenAlpha (0 = invisible, a small value = a ghost).
//
// Built on URP's own 2D sprite path (Core2D.hlsl) rather than a bare vertex shader, so everything a
// SpriteRenderer does on the GPU still happens: flipX/flipY (UnityFlipSprite + unity_SpriteProps — a
// bare shader silently draws flipped sprites unflipped), renderer colour (unity_SpriteColor), GPU
// instancing and skinned sprites. Unlit: it matches Sprite-Lit-Default only where no Light2D lights the
// sprite (the 2D renderer's default global light).
Shader "Laubrary/VisionMaskSprite"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _HiddenAlpha ("Hidden Alpha", Range(0, 1)) = 0
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Name "VisionMaskSprite"
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "VisionMask.hlsl"

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _HiddenAlpha;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4  color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = input.uv;
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                // The whole mechanism: THIS fragment's own world position decides THIS fragment's alpha.
                c.a *= VisionMaskVisibility(i.positionWS) > 0.5 ? 1.0 : _HiddenAlpha;
                return c;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
