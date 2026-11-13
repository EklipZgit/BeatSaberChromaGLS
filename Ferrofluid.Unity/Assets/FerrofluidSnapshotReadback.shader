Shader "Hidden/ChromaGLS/FerrofluidSnapshotReadback"
{
    Properties
    {
        _MainTex ("Planar Source", 2D) = "black" {}
        _Cube ("Room Source", Cube) = "" {}
        _Array ("Stereo Source", 2DArray) = "" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        samplerCUBE _Cube;
        UNITY_DECLARE_TEX2DARRAY(_Array);
        float3 _Forward;
        float3 _Up;
        float _Layer;

        float4 Planar(v2f_img input) : SV_Target
        {
            return tex2Dlod(_MainTex, float4(input.uv, 0.0, 0.0));
        }

        float4 Room(v2f_img input) : SV_Target
        {
            float2 position = input.uv * 2.0 - 1.0;
            float3 right = cross(_Up, _Forward);
            float3 direction = normalize(_Forward + right * position.x + _Up * position.y);
            return texCUBElod(_Cube, float4(direction, 0.0));
        }

        float4 Stereo(v2f_img input) : SV_Target
        {
            return UNITY_SAMPLE_TEX2DARRAY_LOD(_Array, float3(input.uv, _Layer), 0.0);
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Planar
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Room
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Stereo
            ENDCG
        }
    }
}
