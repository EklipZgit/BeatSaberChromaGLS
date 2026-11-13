Shader "ChromaGLS/FerrofluidFloorShadow"
{
    Properties
    {
        _FerroShadowEdge0 ("Shadow Edge 0", Vector) = (0, 0, 0, -1)
        _FerroShadowEdge1 ("Shadow Edge 1", Vector) = (0, 0, 0, -1)
        _FerroShadowEdge2 ("Shadow Edge 2", Vector) = (0, 0, 0, -1)
        _FerroShadowCenter ("Shadow Center", Vector) = (0, 0, 0, 0)
        _FerroShadowQInv ("Shadow QInv", Vector) = (1, 0, 0, 1)
        _FerroShadowMinRadius ("Shadow Min Radius", Float) = 0.1
        _FerroShadowSoftness ("Shadow Softness", Float) = 0.04
        _FerroShadowOpacity ("Shadow Opacity", Range(0, 1)) = 0.32
        [HideInInspector] _MainTex ("Blit Source", 2D) = "black" {}
        _FerroShadowShape ("Shadow Shape", Float) = 0
        _FerroShadowUseMask ("Shadow Use Mask", Float) = 0
        _FerroShadowMask ("Shadow Mask", 2D) = "black" {}
        _FerroShadowMaskBounds ("Shadow Mask Bounds", Vector) = (0, 0, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+20" "RenderType" = "Opaque" }
        Blend DstColor Zero
        ColorMask RGB
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex FerrofluidShadowVertex
            #pragma fragment FerrofluidShadowFragment
            #pragma multi_compile __ FERROFLUID_ENVIRONMENT_CAPTURE
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _FerroShadowEdge0;
            float4 _FerroShadowEdge1;
            float4 _FerroShadowEdge2;
            float4 _FerroShadowCenter;
            float4 _FerroShadowQInv;
            float _FerroShadowMinRadius;
            float _FerroShadowSoftness;
            float _FerroShadowOpacity;
            float _FerroShadowShape;
            float _FerroShadowUseMask;
            sampler2D _FerroShadowMask;
            float4 _FerroShadowMaskBounds;

            struct FerrofluidShadowInput
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct FerrofluidShadowVaryings
            {
                float4 vertex : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            FerrofluidShadowVaryings FerrofluidShadowVertex(FerrofluidShadowInput input)
            {
                FerrofluidShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }

            float4 FerrofluidShadowFragment(FerrofluidShadowVaryings input) : SV_Target
            {
            #if defined(FERROFLUID_ENVIRONMENT_CAPTURE)
                clip(-1.0);
            #endif
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_FerroShadowUseMask > 0.5)
                {
                    // The GPU mask carries the actual deformed silhouette; its blur
                    // already encodes softness, so coverage is a direct sample.
                    float2 maskUV = (input.worldPosition.xz - _FerroShadowMaskBounds.xy) * _FerroShadowMaskBounds.zw;
                    float maskCoverage = all(maskUV >= 0.0) && all(maskUV <= 1.0)
                        ? tex2D(_FerroShadowMask, maskUV).r
                        : 0.0;
                    float maskedFactor = 1.0 - (_FerroShadowOpacity * maskCoverage);
                    return float4(maskedFactor, maskedFactor, maskedFactor, 1.0);
                }

                float2 p = input.worldPosition.xz - _FerroShadowCenter.xy;
                float signedDistance;
                if (_FerroShadowShape > 0.5)
                {
                    float qd = (p.x * ((_FerroShadowQInv.x * p.x) + (_FerroShadowQInv.y * p.y)))
                        + (p.y * ((_FerroShadowQInv.z * p.x) + (_FerroShadowQInv.w * p.y)));
                    signedDistance = (sqrt(max(qd, 0.0)) - 1.0) * _FerroShadowMinRadius;
                }
                else
                {
                    float d0 = abs(dot(_FerroShadowEdge0.xy, p)) - _FerroShadowEdge0.w;
                    float d1 = abs(dot(_FerroShadowEdge1.xy, p)) - _FerroShadowEdge1.w;
                    float d2 = abs(dot(_FerroShadowEdge2.xy, p)) - _FerroShadowEdge2.w;
                    signedDistance = max(d0, max(d1, d2));
                }

                float coverage = 1.0 - smoothstep(-_FerroShadowSoftness, _FerroShadowSoftness, signedDistance);
                float factor = 1.0 - (_FerroShadowOpacity * coverage);
                return float4(factor, factor, factor, 1.0);
            }
            ENDCG
        }

        // Separable Gaussian blur over the shadow mask. A custom LightMode keeps scene
        // cameras from drawing it; only the explicit Graphics.Blit pass index uses it.
        Pass
        {
            Name "FERROFLUID_SHADOW_BLUR"
            Tags { "LightMode" = "FerrofluidShadowBlur" }
            Blend Off
            ZTest Always
            ColorMask R

            CGPROGRAM
            #pragma vertex FerrofluidShadowBlurVertex
            #pragma fragment FerrofluidShadowBlurFragment
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float2 _FerroShadowBlurStep;

            struct FerrofluidShadowBlurVaryings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            FerrofluidShadowBlurVaryings FerrofluidShadowBlurVertex(appdata_img input)
            {
                FerrofluidShadowBlurVaryings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.texcoord;
                return output;
            }

            // Nine taps as five bilinear fetches.
            fixed4 FerrofluidShadowBlurFragment(FerrofluidShadowBlurVaryings input) : SV_Target
            {
                float coverage = tex2D(_MainTex, input.uv).r * 0.227027;
                coverage += tex2D(_MainTex, input.uv + (_FerroShadowBlurStep * 1.384615)).r * 0.316216;
                coverage += tex2D(_MainTex, input.uv - (_FerroShadowBlurStep * 1.384615)).r * 0.316216;
                coverage += tex2D(_MainTex, input.uv + (_FerroShadowBlurStep * 3.230769)).r * 0.070270;
                coverage += tex2D(_MainTex, input.uv - (_FerroShadowBlurStep * 3.230769)).r * 0.070270;
                return fixed4(coverage, coverage, coverage, 1.0);
            }
            ENDCG
        }
    }
}
