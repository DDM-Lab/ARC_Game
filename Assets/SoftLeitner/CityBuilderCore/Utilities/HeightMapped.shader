Shader "Custom/HeightMapped"
{
    Properties
    {
        [PerRendererData]_MainTex ("MainTex", 2D) = "white" {}
        [PerRendererData]_HeightMap ("HeightMap", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.0
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Size("Size", float) = 32
        _Height("Height",float) = 32
        _HeightOffset("HeightOffset", float) = 0
        _Offset ("Offset", float) = -1
    }
    SubShader 
    {
        PackageRequirements { "com.unity.render-pipelines.universal" }
        
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        LOD 200
        Offset [_Offset], [_Offset]
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma frw
            #pragma target 2.0

            #include "UnityCG.cginc"
            
            sampler2D _MainTex;
            sampler2D _HeightMap;
            half _Glossiness;
            half _Metallic;
            fixed4 _Color;
            float _Size;
            float _Height;
            float _HeightOffset;

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_full i)
            {
                v2f o;
                
                float4 h = tex2Dlod(_HeightMap, float4(i.vertex.x,i.vertex.z,0,0)/_Size);   
    
                o.vertex = i.vertex + float4(0,h.x*_Height+_HeightOffset,0,0);
                o.vertex = UnityObjectToClipPos(o.vertex);
                o.texcoord = i.texcoord;
                o.color = i.color * _Color;
        
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D (_MainTex, i.texcoord) * i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
    SubShader
    {
        LOD 200
        Offset [_Offset], [_Offset]

        CGPROGRAM
        #pragma surface surf Standard decal:blend
        #pragma vertex vert
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _HeightMap;
        half _Glossiness;
        half _Metallic;
        fixed4 _Color;
        float _Size;
        float _Height;
        float _HeightOffset;

        struct Input
        {
            float2 uv_MainTex;
			float4 color: Color;
        };
        
        void vert(inout appdata_full v)
        {
            float4 h = tex2Dlod(_HeightMap, float4(v.vertex.x,v.vertex.z,0,0)/_Size);    
            v.vertex += float4(0,h.x*_Height+_HeightOffset,0,0);
        } 

        void surf (Input i, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D (_MainTex, i.uv_MainTex);
            o.Albedo = c.rgb * i.color.rgb * _Color;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"    
}
