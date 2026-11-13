Shader "Hidden/FerrofluidCorrespondenceProbe"
{
    CGINCLUDE
    #include "UnityCG.cginc"
    #include "../FerrofluidSurfaceCorrespondence.cginc"

    float3 _CameraPosition;
    float _FieldCurvature;
    float _ConstantField;

    struct ProbeVaryings
    {
        float4 vertex : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    ProbeVaryings ProbeVertex(uint vertexId : SV_VertexID)
    {
        ProbeVaryings output;
        float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
        output.uv = uv;
        float2 clip = uv * 2 - 1;
        #if UNITY_UV_STARTS_AT_TOP
        clip.y = -clip.y;
        #endif
        output.vertex = float4(clip, 0, 1);
        return output;
    }

    float4 ProbeFragment(ProbeVaryings input) : SV_Target
    {
        float2 uv = input.uv;
        float3 meshPosition = float3(uv.x, uv.y, 0);
        float3 fieldPosition = _ConstantField > 0.5
            ? float3(0.4, 0.4, 0)
            : float3(uv.x + _FieldCurvature * uv.x * (1 - uv.x), uv.y, 0);
        float2 corrected = FerrofluidCorrectSurfaceUv(uv, meshPosition, fieldPosition, _CameraPosition, 512.0);
        return float4(corrected, uv);
    }
    ENDCG

    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex ProbeVertex
            #pragma fragment ProbeFragment
            ENDCG
        }
    }
}
