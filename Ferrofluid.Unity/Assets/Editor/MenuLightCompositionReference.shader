// Editor-only stand-in for the game's Custom/CustomParticles pass state and colour path as
// configured by MenuLightRenderer. It implements the material-driven blend factors, cull/depth
// state, and the vertex-colour-alpha bloom payload the renderer actually sets; it is not a port
// of the game's full shader (no fog, spectrogram, or whiteboost paths).
Shader "Hidden/ChromaGLS/MenuLightCompositionReference"
{
    Properties
    {
        _Color ("Base Color", Color) = (1, 1, 1, 1)
        _MainTex ("Main Texture", 2D) = "white" {}
        _Intensity ("Intensity", Float) = 1
        _AlphaMultiplier ("Alpha Multiplier", Float) = 1
        _BloomMultiplier ("PC Bloom Multiplier", Float) = 1
        _WhiteBoostType ("White Boost", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _BlendSrcFactor ("Color Contribution", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _BlendDstFactor ("Color Background", Float) = 1
        [Enum(UnityEngine.Rendering.BlendOp)] _BlendOp ("Blend Operation", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _BlendSrcFactorA ("Alpha Contribution", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _BlendDstFactorA ("Alpha Background", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull Mode", Float) = 0
        [Toggle] _CustomZWrite ("Z Write", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _OffsetFactor ("Offset Factor", Float) = 0
        _OffsetUnits ("Offset Units", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend [_BlendSrcFactor] [_BlendDstFactor], [_BlendSrcFactorA] [_BlendDstFactorA]
            BlendOp [_BlendOp]
            ZWrite [_CustomZWrite]
            ZTest [_ZTest]
            Cull [_CullMode]
            Offset [_OffsetFactor], [_OffsetUnits]
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _Color;
            float _Intensity;
            float _AlphaMultiplier;
            float _BloomMultiplier;
            sampler2D _MainTex;

            struct Input
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Input input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 color = _Color * input.color * tex2D(_MainTex, input.uv);
                float alpha = color.a * _AlphaMultiplier;
                return float4(color.rgb * _Intensity * alpha, alpha * _BloomMultiplier);
            }
            ENDCG
        }
    }
}
