Shader "ChromaGLS/MenuLightCubeSpill"
{
    Properties
    {
        _Color ("Albedo", Color) = (1,1,1,1)
        [HideInInspector] _MenuLightUseFog ("Use Native Fog", Float) = 0
        [HideInInspector] _MenuLightUseHeightFog ("Use Native Height Fog", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ColorMask RGB
            Blend One One
            Cull Back
            ZWrite Off
            ZTest Equal
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 worldPos : TEXCOORD0; float3 worldNormal : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            int _MenuLightSpillCount;
            float4 _MenuLightSpillPositions[32];
            float4 _MenuLightSpillEnergy[32];
            float4 _MenuLightSpillNormals[32];
            float4 _Color;
            float _MenuLightUseFog;
            float _MenuLightUseHeightFog;
            float _CustomFogAttenuation;
            float _CustomFogOffset;
            float _CustomFogHeightFogStartY;
            float _CustomFogHeightFogHeight;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldPos = w;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 normal = normalize(i.worldNormal);
                float3 color = 0;
                [loop]
                for (int source = 0; source < _MenuLightSpillCount; source++)
                {
                    float3 delta = _MenuLightSpillPositions[source].xyz - i.worldPos;
                    float distanceSquared = dot(delta, delta);
                    float3 direction = delta * rsqrt(max(distanceSquared, 1e-8));
                    float facing = saturate(dot(normal, direction));
                    float emitter = abs(dot(_MenuLightSpillNormals[source].xyz, direction));
                    float attenuation = facing * emitter
                        / (UNITY_PI * max(distanceSquared + _MenuLightSpillPositions[source].w, 1e-8));
                    color += _MenuLightSpillEnergy[source].rgb * attenuation;
                }

                float clear = 1;
                if (_MenuLightUseFog > 0.5)
                {
                    float3 viewDelta = i.worldPos - _WorldSpaceCameraPos;
                    float distanceSquared = dot(viewDelta, viewDelta);
                    clear = rcp(max(max(distanceSquared - 1, 0) * 0.08 - _CustomFogOffset, 0)
                        * _CustomFogAttenuation + 1);
                    if (_MenuLightUseHeightFog > 0.5)
                    {
                        float height = saturate((i.worldPos.y - (_CustomFogHeightFogStartY + _CustomFogHeightFogHeight))
                            / _CustomFogHeightFogHeight);
                        clear *= height * height * (3 - 2 * height);
                    }
                }

                // The base pass already adds fog color. Only attenuate the new light.
                return float4(color * _Color.rgb * clear, 0);
            }
            ENDCG
        }
    }
}
