Shader "Hidden/ChromaGLS/ParabolicBloomValidation"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 Frag(v2f_img input) : SV_Target
            {
                float4 source = tex2D(_MainTex, input.uv);
                float3 bloom = 0.0;
                for (int x = -2; x <= 2; x++)
                {
                    for (int y = -2; y <= 2; y++)
                    {
                        float4 texel = tex2D(_MainTex, input.uv + float2(x, y) * _MainTex_TexelSize.xy * 3.0);
                        bloom += texel.rgb * texel.a / 25.0;
                    }
                }
                return float4(source.rgb + source.aaa + bloom * 0.3, 0.0);
            }
            ENDCG
        }
    }
}
