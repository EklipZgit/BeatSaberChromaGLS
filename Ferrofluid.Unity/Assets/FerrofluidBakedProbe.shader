Shader "Hidden/FerrofluidBakedProbe"
{
    Properties
    {
        _Cube ("Cube", Cube) = "" {}
    }
    SubShader
    {
        Pass
        {
            ZTest Always
            Cull Off
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            samplerCUBE _Cube;
            float4 _Direction;
            float _FaceMip;

            float4 frag(v2f_img i) : SV_Target
            {
                return texCUBElod(_Cube, float4(normalize(_Direction.xyz), _FaceMip));
            }
            ENDCG
        }
    }
}
