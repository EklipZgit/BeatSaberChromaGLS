Shader "ChromaGLS/NativeMenuCubeBase"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,0.36)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite On
            CGPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            float4 _Color;

            struct Varyings
            {
                float4 position : SV_POSITION;
            };

            Varyings Vertex(float4 position : POSITION)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(position);
                return output;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                return float4(0, 0, 0, _Color.a);
            }
            ENDCG
        }
    }
}
