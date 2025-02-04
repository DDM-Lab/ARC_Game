Shader "Custom/SpriteOverlay" {
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap("Pixel snap", Float) = 0
        [PerRendererData]_HeightMap ("Height Map", 2D) = "black" {}
        _Size("HMap Size", float) = 32
        _Height("HMap Height",float) = 32
        _HeightOffset("HMap Offset", float) = 0
    }

        SubShader
        {
            Tags
            {
                "Queue" = "Transparent"
                "IgnoreProjector" = "True"
                "RenderType" = "Transparent"
                "PreviewType" = "Plane"
                "CanUseSpriteAtlas" = "True"
            }

            ZTest Always
            Cull Off
            Lighting Off
            ZWrite On
            Fog { Mode Off }
            Blend One OneMinusSrcAlpha

            Pass
            {
            CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma multi_compile DUMMY PIXELSNAP_ON
                #include "UnityCG.cginc"
                
                sampler2D _MainTex;
                fixed4 _Color;
                
                sampler2D _HeightMap;
                float _Size;
                float _Height;
                float _HeightOffset;

                struct appdata_t
                {
                    float4 vertex   : POSITION;
                    float4 color    : COLOR;
                    float2 texcoord : TEXCOORD0;
                };

                struct v2f
                {
                    float4 vertex   : SV_POSITION;
                    fixed4 color : COLOR;
                    half2 texcoord  : TEXCOORD0;
                };
                
                v2f vert(appdata_t i)
                {
                    v2f o;
                    
                    float4 h = tex2Dlod(_HeightMap, float4(i.vertex.x,i.vertex.z,0,0)/_Size);   
                    o.vertex = i.vertex + float4(0,h.x*_Height+_HeightOffset,0,0);
                    o.vertex = UnityObjectToClipPos(o.vertex);
                    o.texcoord = i.texcoord;
                    o.color = i.color * _Color;
                    #ifdef PIXELSNAP_ON
                    o.vertex = UnityPixelSnap(o.vertex);
                    #endif

                    return o;
                }

                fixed4 frag(v2f IN) : SV_Target
                {
                    fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;
                    c.rgb *= c.a;
                    return c;
                }
            ENDCG
            }
        }
}