Shader "Colorido/Coastal Water"
{
    Properties
    {
        _Color ("Ocean turquoise", Color) = (0.015,0.54,0.63,1)
        _Highlight ("Ripples", Color) = (0.32,0.85,0.83,1)
        _Speed ("Wave speed", Range(0,2)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };
            fixed4 _Color, _Highlight;
            float _Speed;
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = floor(i.world.xz * 18.0) / 18.0;
                float t = _Time.y * _Speed;
                float waves = sin(p.x * 1.9 + p.y * 3.1 + t) * sin(p.y * 2.5 - t * .7);
                float bands = floor((waves * .5 + .5) * 4.0) / 4.0;
                float sparkle = step(.987, sin(p.x * 7.7 + p.y * 3.2 + t)) * step(.7, waves);
                return fixed4(lerp(_Color.rgb, _Highlight.rgb, bands * .23 + sparkle * .35), 1);
            }
            ENDCG
        }
    }
}
