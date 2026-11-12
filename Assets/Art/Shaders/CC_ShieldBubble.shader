// The Commodore Card shield around the scooter: see-through in the middle, glowing
// at the edges (fresnel), with faint scan lines drifting upward. Additive, so it
// only ever brightens what's behind it. Single-pass instanced stereo safe (Quest).
// ScooterShieldBubble drives _Color and _Intensity per renderer.
Shader "CommodoreCircuit/ShieldBubble"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.85, 1, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Fill ("Inner Fill", Range(0, 1)) = 0.06
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _RimStrength ("Rim Strength", Range(0, 4)) = 1.4
        _LineDensity ("Scan Line Density", Float) = 14
        _LineSpeed ("Scan Line Speed", Float) = 0.6
        _LineStrength ("Scan Line Strength", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Blend SrcAlpha One
        ZWrite Off
        Cull Back
        Lighting Off

        Pass
        {
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos         : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos    : TEXCOORD1;
                float  localY      : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float _Intensity, _Fill, _RimPower, _RimStrength, _LineDensity, _LineSpeed, _LineStrength;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.localY = v.vertex.y;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float facing = saturate(abs(dot(normalize(i.worldNormal), viewDir)));
                float rim = pow(1.0 - facing, _RimPower);

                // Thin bright lines that drift up the bubble.
                float band = frac(i.localY * _LineDensity - _Time.y * _LineSpeed);
                float scan = saturate(1.0 - abs(band - 0.5) / 0.08);

                float a = (_Fill + rim * _RimStrength + scan * _LineStrength * (0.25 + rim)) * _Intensity * _Color.a;
                return fixed4(_Color.rgb, saturate(a));
            }
        ENDCG
        }
    }
}
