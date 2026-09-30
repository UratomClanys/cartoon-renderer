Shader "Colorido/Coastline Stylized Post"
{
    Properties
    {
        _Enabled ("Enabled", Float) = 1
        _BrushRadius ("Brush radius", Range(1,12)) = 5
        _Blend ("Paint blending", Range(0,1)) = 0.85
        _Texture ("Pigment variation", Range(0,1)) = 0.16
        _Saturation ("Saturation", Float) = 1.04
        _Contrast ("Contrast", Float) = 1.025
        _Tint ("Warm light / cool shadows", Range(0,1)) = 0.12
        _Compare ("Original on left", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "CoastalOilPaint"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            Texture2D _BlitTexture;
            SamplerState sampler_linear_clamp;
            float4 _BlitScaleBias;
            float _Enabled, _BrushRadius, _Blend, _Texture, _Saturation, _Contrast, _Tint, _Compare;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o;
                float2 uv = float2((id << 1) & 2, id & 2);
                o.positionCS = float4(uv * 2 - 1, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1 - uv.y;
                #endif
                o.uv = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
                return o;
            }
            float3 Read(float2 uv) { return _BlitTexture.SampleLevel(sampler_linear_clamp, saturate(uv), 0).rgb; }
            float Luma(float3 c) { return dot(c, float3(.2126,.7152,.0722)); }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 cell=floor(p), f=frac(p);
                f=f*f*(3-2*f);
                return lerp(lerp(Hash(cell),Hash(cell+float2(1,0)),f.x),
                            lerp(Hash(cell+float2(0,1)),Hash(cell+1),f.x),f.y);
            }
            float4 Frag(Varyings i) : SV_Target
            {
                float3 original = Read(i.uv);
                if (_Enabled < .5 || (_Compare > .5 && i.uv.x < .5)) return float4(original,1);
                float2 texel = 1 / _ScreenParams.xy;
                float2 gradient = float2(
                    Luma(Read(i.uv+float2(texel.x,0))) - Luma(Read(i.uv-float2(texel.x,0))),
                    Luma(Read(i.uv+float2(0,texel.y))) - Luma(Read(i.uv-float2(0,texel.y))));
                float2 normal = dot(gradient,gradient) > .00001 ? normalize(gradient) : normalize(float2(.55,.83));
                float2 tangent = float2(-normal.y,normal.x);
                float radius = _BrushRadius * max(1,_ScreenParams.y/900);
                float2 axisX = tangent * texel * radius * .65;
                float2 axisY = normal * texel * radius * .45;
                // Four overlapping, oriented regions. Prefer the region with the least
                // colour variance, retaining silhouettes while merging tiny surface details.
                float3 sum = 0;
                float weightSum = 0;
                [unroll] for (int q=0;q<4;q++)
                {
                    float2 signQ = float2((q&1)==0 ? -1 : 1,(q&2)==0 ? -1 : 1);
                    float3 mean=0, moment=0;
                    [unroll] for (int y=0;y<3;y++)
                    [unroll] for (int x=0;x<3;x++)
                    {
                        float3 s=Read(i.uv+axisX*(x*signQ.x)+axisY*(y*signQ.y));
                        mean+=s; moment+=s*s;
                    }
                    mean/=9; moment=abs(moment/9-mean*mean);
                    float variance=Luma(moment);
                    float w=1/pow(1+variance*600,3);
                    sum+=mean*w; weightSum+=w;
                }
                float3 c=lerp(original,sum/max(weightSum,1e-12),_Blend);
                #ifndef UNITY_COLORSPACE_GAMMA
                    c=LinearToGammaSpace(max(c,0));
                #endif
                float l=Luma(c);
                c=lerp(l.xxx,c,_Saturation);
                c=(c-.5)*_Contrast+.5;
                c+=_Tint*lerp(float3(-.045,.018,.030),float3(.040,.026,-.030),smoothstep(.15,.85,l));
                // Stable, low-amplitude pigment variation: no animated grain or pixel grid.
                float2 paper=i.uv*_ScreenParams.xy;
                float pigment=Noise(float2(paper.x*.055+paper.y*.017,paper.y*.15));
                float fibre=Noise(paper*.52);
                c*=1+((pigment-.5)*.075+(fibre-.5)*.025)*_Texture;
                c=saturate(c);
                #ifndef UNITY_COLORSPACE_GAMMA
                    c=GammaToLinearSpace(c);
                #endif
                if (_Compare>.5 && abs(i.uv.x-.5)<1/_ScreenParams.x) c=float3(.8,.7,.15);
                return float4(c,1);
            }
            ENDHLSL
        }
    }
}
