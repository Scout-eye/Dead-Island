Shader "DeadIsland/VertexColorLit"
{
    // Terrain : couleur par vertex + éclairage diffus simple (URP) + écume de rivage partagée
    // (DIWater.hlsl) : foam là où le terrain est près de la ligne d'eau animée. Même système que
    // pour les personnages. Passes DepthOnly + ShadowCaster pour les ombres.
    Properties
    {
        _FoamColor ("Couleur écume", Color) = (1,1,1,1)
        _FoamRise ("Montée de l'écume au-dessus de l'eau (m)", Float) = 0.5
        _FoamNoiseScale ("Variation le long de la côte", Float) = 1.5

        [Header(Vagues  identiques au materiau de l eau)]
        _WaterLevel ("Niveau mer (y)", Float) = 0.0
        _WaveAmplitude ("Amplitude vagues", Float) = 0.12
        _WaveFrequency ("Fréquence vagues", Float) = 0.25
        _WaveSpeed ("Vitesse vagues", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "DIWater.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float4 color       : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _FoamColor;
                float _FoamRise;
                float _FoamNoiseScale;
                float _WaterLevel;
                float _WaveAmplitude;
                float _WaveFrequency;
                float _WaveSpeed;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(n, mainLight.direction));
                float3 ambient = SampleSH(n);
                float3 lit = IN.color.rgb * (mainLight.color * ndotl + ambient);

                // Écume de rivage (partagée) : seulement sur les surfaces ~planes (plage, pas falaises).
                float slope = smoothstep(0.45, 0.78, n.y);
                float foam = DI_ShoreFoam(IN.positionWS, _FoamRise, _FoamNoiseScale,
                                          _WaterLevel, _WaveAmplitude, _WaveFrequency, _WaveSpeed) * slope;
                lit = lerp(lit, _FoamColor.rgb, foam);

                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionHCS : SV_POSITION; };

            V DepthVert(A IN) { V o; o.positionHCS = TransformObjectToHClip(IN.positionOS.xyz); return o; }
            half DepthFrag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionHCS : SV_POSITION; };

            V ShadowVert(A IN)
            {
                V o;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionHCS = positionCS;
                return o;
            }

            half ShadowFrag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
