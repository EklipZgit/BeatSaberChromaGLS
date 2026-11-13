// Destination attenuation for menu lights: multiplies whatever is under the core
// (halos, backdrop, bloom alpha) by 1-coverage before the additive strip draws.
Shader "Hidden/ChromaGLS/MenuLightCoreOcclusion"
{
    SubShader
    {
        Tags { "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Back
            ZTest LEqual
            ZWrite Off
            Blend Zero OneMinusSrcAlpha
            ColorMask RGBA
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct Input
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float coverage : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Input input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                // Raw mesh vertex alpha only; material brightness must not widen the mask.
                output.coverage = input.color.a;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return float4(0.0, 0.0, 0.0, saturate(input.coverage));
            }
            ENDCG
        }
    }
}
