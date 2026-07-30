Shader "Hidden/Laubrary/TextSplash/PremultipliedUI"
{
    // Presents a PREMULTIPLIED-alpha texture on a uGUI Graphic (SplashPixelRig's RawImage).
    //
    // TMP's SDF shaders blend `One OneMinusSrcAlpha`, so anything rendered through them lands in the target with its
    // colour already multiplied by its coverage. uGUI's default UI material then blends `SrcAlpha OneMinusSrcAlpha`
    // and multiplies by alpha a SECOND time, so an antialiased edge pixel composites as C·a² instead of C·a and the
    // rim reads dark. `Blend One OneMinusSrcAlpha` here is the whole fix — interior pixels (a = 1) are identical
    // either way, which is why only the rim ever showed the difference.
    //
    // Deliberately plain CG with no pipeline includes: the rig presents through a Screen-Space-OVERLAY canvas, which
    // the engine composites outside any SRP, so this must work unchanged on Built-in, URP and HDRP.
    Properties
    {
        [PerRendererData] _MainTex ("Buffer", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Lighting Off
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // The standard uGUI vertex layout. A Canvas always writes position / colour / uv0; the extra channels a
            // masked or lit UI would need are not read here, so they cost nothing.
            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                // The Graphic's tint and the CanvasRenderer's own colour/alpha both arrive in the vertex colour;
                // `_Color` is the material-level tint, exactly as UI/Default composes them.
                o.color    = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.texcoord);
                // Tinting PREMULTIPLIED colour has to scale RGB by the tint's alpha as well as its RGB, or fading
                // the presenter out would leave the colour at full strength while only the alpha dropped.
                c.rgb *= i.color.rgb * i.color.a;
                c.a   *= i.color.a;
                return c;
            }
            ENDCG
        }
    }
}
