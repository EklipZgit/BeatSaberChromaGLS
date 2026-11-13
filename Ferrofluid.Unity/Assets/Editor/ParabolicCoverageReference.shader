Shader "Hidden/ChromaGLS/ParabolicCoverageReference"
{
    Properties
    {
        _MainTex ("Supersampled Radiance", 2D) = "black" {}
    }
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;

            float4 Frag(v2f_img input) : SV_Target
            {
                return tex2Dlod(_MainTex, float4(input.uv, 0.0, 2.0));
            }
            ENDCG
        }
    }
}
