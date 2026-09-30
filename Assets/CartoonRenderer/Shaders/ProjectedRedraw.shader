Shader "Hidden/CartoonProjection/ProjectedRedraw"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            struct A { float3 p:POSITION; float4 c:COLOR; };
            struct V { float4 p:SV_POSITION; float4 c:COLOR; };
            V Vert(A i) { V o; o.p=float4(i.p.xy*2-1,0,1);
            #if UNITY_UV_STARTS_AT_TOP
                o.p.y=-o.p.y;
            #endif
                o.c=i.c; return o; }
            float4 Frag(V i):SV_Target { return float4(SRGBToLinear(i.c.rgb),1); }
            ENDHLSL
        }
    }
}
