Shader "DeadIsland/CharacterFoam"
{
    // Personnage : couleur unie + éclairage diffus URP + MÊME écume que le rivage (DIWater.hlsl).
    // L'écume apparaît là où le modèle est près de la ligne d'eau => anneaux autour des jambes,
    // forme qui suit le corps selon l'immersion. Pas de halo, pas de depth texture.
    Properties
    {
        _BaseColor ("Teinte", Color) = (0.8, 0.8, 0.8, 1)
        _BaseMap ("Texture (albedo du modèle)", 2D) = "white" {}
        _FoamColor ("Couleur écume", Color) = (1,1,1,1)
        _FoamRise ("Montée de l'écume au-dessus de l'eau (m)", Float) = 0.3
        _FoamNoiseScale ("Variation de l'écume", Float) = 3.0

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
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float2 uv          : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
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
                OUT.uv = IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(n, mainLight.direction));
                float3 ambient = SampleSH(n);
                float3 albedo = _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;
                float3 lit = albedo * (mainLight.color * ndotl + ambient);

                // Même écume que le rivage, sans masque de pente => suit les jambes/le corps.
                float foam = DI_ShoreFoam(IN.positionWS, _FoamRise, _FoamNoiseScale,
                                          _WaterLevel, _WaveAmplitude, _WaveFrequency, _WaveSpeed);
                lit = lerp(lit, _FoamColor.rgb, foam);

                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

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
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionHCS = cs;
                return o;
            }

            half ShadowFrag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
