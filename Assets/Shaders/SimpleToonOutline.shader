Shader "RTSLords/Simple Toon Outline"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _ToonSteps ("Toon Steps", Range(2, 6)) = 3
        _ShadowStrength ("Shadow Strength", Range(0.2, 1)) = 0.55
        _OutlineColor ("Outline Color", Color) = (0.08,0.08,0.08,1)
        _OutlineWidth ("Outline Width", Range(0.0005, 0.02)) = 0.003
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 250

        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode" = "Always" }
            Cull Front
            ZWrite On
            ColorMask RGB

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _OutlineColor;
            float _OutlineWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                worldPos += normalize(worldNormal) * _OutlineWidth;
                o.pos = UnityWorldToClipPos(worldPos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }

        CGPROGRAM
        #pragma surface surf ToonRamp fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _ToonSteps;
        half _ShadowStrength;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
        }

        inline half4 LightingToonRamp(SurfaceOutput s, half3 lightDir, half atten)
        {
            half ndotl = saturate(dot(s.Normal, lightDir));
            half steps = max(_ToonSteps, 2);
            half band = floor(ndotl * steps) / (steps - 1);
            half lightAmount = lerp(_ShadowStrength, 1.0h, saturate(band));

            half4 c;
            c.rgb = s.Albedo * (_LightColor0.rgb * lightAmount * atten + UNITY_LIGHTMODEL_AMBIENT.rgb);
            c.a = s.Alpha;
            return c;
        }
        ENDCG
    }

    Fallback "Diffuse"
}
