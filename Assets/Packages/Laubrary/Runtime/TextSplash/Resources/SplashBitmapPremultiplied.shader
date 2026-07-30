Shader "Hidden/Laubrary/TextSplash/BitmapPremultiplied"
{
    // Draws a BITMAP (raster) TMP font asset into the pixel rig's buffer under the same premultiplied contract every
    // other pass in this tool already obeys.
    //
    // It exists because TMP's own `TextMeshPro/Mobile/Bitmap` breaks that contract twice over, and one of the two is
    // silent:
    //
    //   ALPHA SQUARED. That shader outputs STRAIGHT colour and blends `SrcAlpha OneMinusSrcAlpha`. Into a
    //     transparent-black-cleared buffer the destination contributes nothing, so what lands is `C·a` in RGB and
    //     `a·a` in alpha — the alpha channel ends up on a different scale from the colour it belongs to. Every SDF
    //     shader in TMP blends `One OneMinusSrcAlpha` instead, and the whole rig is built on that: SplashPixelRig's
    //     `Crisp` divides RGB by the stored alpha to recover a glyph's true colour, the palette lock matches those
    //     recovered colours against the author's fills, and `fixEdgeAlpha` presents through a premultiplied blend.
    //     Hand all three an `a²` and every partly covered cell is wrong — and a perfectly binary mask hides it,
    //     because 0 and 1 are the two fixed points of squaring. So it looks right exactly until it isn't.
    //
    //   STRIPPING. `TextMeshPro/Mobile/Bitmap` sits in no Resources folder and, in a project that authors no bitmap
    //     text by hand, is referenced by no shipped material — so it is stripped from a player build and
    //     `Shader.Find` returns null there while working perfectly in the editor. Living in the package's OWN
    //     Resources folder is what keeps this one alive, the same keep-alive SplashPremultipliedUI and the bevel
    //     preset material already rely on.
    //
    // `UnityPixelSnap` is kept from TMP's original on purpose: it snaps each vertex to the pixel grid, which is free
    // grid alignment for text at rest and is exactly what the Pixel-font mode wants. It is per-VERTEX, so it cannot
    // rescue a rotated quad — that limitation belongs to the mode, not to this shader.
    Properties
    {
        _MainTex          ("Font Atlas", 2D) = "white" {}
        _Color            ("Text Color", Color) = (1,1,1,1)
        _DiffusePower     ("Diffuse Power", Range(1.0,4.0)) = 1.0

        _VertexOffsetX    ("Vertex OffsetX", float) = 0
        _VertexOffsetY    ("Vertex OffsetY", float) = 0

        _ClipRect         ("Clip Rect", vector) = (-32767, -32767, 32767, 32767)

        _StencilComp      ("Stencil Comparison", Float) = 8
        _Stencil          ("Stencil ID", Float) = 0
        _StencilOp        ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask", Float) = 255

        _CullMode         ("Cull Mode", Float) = 0
        _ColorMask        ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }

        Stencil
        {
            Ref[_Stencil]
            Comp[_StencilComp]
            Pass[_StencilOp]
            ReadMask[_StencilReadMask]
            WriteMask[_StencilWriteMask]
        }

        Lighting Off
        Cull [_CullMode]
        ZTest [unity_GUIZTestMode]
        ZWrite Off
        Fog { Mode Off }
        // The fix. With the fragment emitting premultiplied colour, this stores C·a and a — the same pair TMP's SDF
        // shaders store, so the buffer means one thing no matter which mode drew into it.
        Blend One OneMinusSrcAlpha
        ColorMask[_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma fragmentoption ARB_precision_hint_fastest

            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex    : POSITION;
                fixed4 color     : COLOR;
                float2 texcoord0 : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1;
            };

            struct v2f
            {
                float4 vertex    : POSITION;
                fixed4 color     : COLOR;
                float2 texcoord0 : TEXCOORD0;
                float4 mask      : TEXCOORD2;
            };

            sampler2D _MainTex;
            fixed4    _Color;
            float     _DiffusePower;

            uniform float  _VertexOffsetX;
            uniform float  _VertexOffsetY;
            uniform float4 _ClipRect;
            uniform float  _MaskSoftnessX;
            uniform float  _MaskSoftnessY;
            uniform float  _UIMaskSoftnessX;
            uniform float  _UIMaskSoftnessY;
            uniform int    _UIVertexColorAlwaysGammaSpace;

            v2f vert (appdata_t v)
            {
                v2f OUT;
                float4 vert = v.vertex;
                vert.x += _VertexOffsetX;
                vert.y += _VertexOffsetY;

                vert.xy += (vert.w * 0.5) / _ScreenParams.xy;
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                {
                    v.color.rgb = UIGammaToLinear(v.color.rgb);
                }
                OUT.vertex = UnityPixelSnap(UnityObjectToClipPos(vert));
                OUT.color = v.color;
                OUT.color *= _Color;
                OUT.color.rgb *= _DiffusePower;
                OUT.texcoord0 = v.texcoord0;

                float2 pixelSize = OUT.vertex.w;

                const float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                const half2 maskSoftness = half2(max(_UIMaskSoftnessX, _MaskSoftnessX), max(_UIMaskSoftnessY, _MaskSoftnessY));
                OUT.mask = float4(vert.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * maskSoftness + pixelSize.xy));

                return OUT;
            }

            fixed4 frag (v2f IN) : COLOR
            {
                // A bitmap atlas carries coverage in ALPHA only, exactly like an SDF atlas carries distance there.
                fixed a = IN.color.a * tex2D(_MainTex, IN.texcoord0).a;
                fixed4 color = fixed4(IN.color.rgb * a, a);

                #if UNITY_UI_CLIP_RECT
                    half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                    // Scaling a premultiplied pair by the same factor keeps it premultiplied, so the clip fades
                    // colour and coverage together instead of leaving colour behind at full strength.
                    color *= m.x * m.y;
                #endif

                #if UNITY_UI_ALPHACLIP
                    clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
