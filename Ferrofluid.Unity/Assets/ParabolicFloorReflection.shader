Shader "Hidden/ChromaGLS/ParabolicFloorReflection"
{
    Properties
    {
        _Color ("Radiance", Color) = (1, 1, 1, 1)
        _BakeBounds ("World XZ Bounds", Vector) = (0, 0, 1, 1)
        _BloomMultiplier ("Native Bloom Payload", Float) = 0
        _BakeSample ("Subpixel Offset And Weight", Vector) = (0, 0, 1, 0)
    }
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One One
            ColorMask RGBA
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            float4 _Color;
            float4 _BakeBounds;
            float _BloomMultiplier;
            // The Save bake offsets each of four coverage passes by a texel fraction in .xy and
            // weights every channel by .z so quarter-strength alpha keeps the native bloom payload.
            float4 _BakeSample;

            struct Input
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
            };

            Varyings Vert(Input input)
            {
                Varyings output;
                float2 uv = (input.vertex.xz - _BakeBounds.xy) * _BakeBounds.zw;
                uv += _BakeSample.xy;
                output.position = float4(uv * 2.0 - 1.0, 0.0, 1.0);
            #if UNITY_UV_STARTS_AT_TOP
                output.position.y = -output.position.y;
            #endif
                output.color = input.color * _Color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return float4(input.color.rgb * input.color.a, input.color.a * _BloomMultiplier) * _BakeSample.z;
            }
            ENDCG
        }
    }
}
