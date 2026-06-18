Shader "DeadIsland/WaterURP"
{
    // Surface d'eau stylisée URP. Vagues & niveau viennent des globales publiées par WaterSurface
    // (via DIWater.hlsl) => synchronisées avec l'écume du terrain et des personnages.
    //  - vagues verticales douces (normales analytiques),
    //  - couleur translucide 2 tons (fresnel) + spéculaire,
    //  - moutons (whitecaps) sur les crêtes via bruit fbm.
    Properties
    {
        _ShallowColor ("Couleur bord/peu profond", Color) = (0.35, 0.78, 0.82, 0.55)
        _DeepColor    ("Couleur profond", Color)          = (0.06, 0.30, 0.52, 0.9)
        _Smoothness    ("Brillance", Range(0,1)) = 0.9

        [Header(Vagues  garder identique sur terrain et perso)]
        _WaterLevel ("Niveau mer (y)", Float) = 0.0
        _WaveAmplitude ("Amplitude vagues", Float) = 0.12
        _WaveFrequency ("Fréquence vagues", Float) = 0.25
        _WaveSpeed ("Vitesse vagues", Float) = 1.0

        _SeaFoamColor  ("Couleur moutons", Color) = (1,1,1,1)
        _SeaFoamCutoff ("Seuil moutons (haut=moins)", Range(0,1)) = 0.66
        _SeaFoamAmount ("Intensité moutons", Range(0,1)) = 0.85
        _SeaFoamScale  ("Échelle moutons", Float) = 0.25
        _SeaFoamSpeed  ("Vitesse moutons", Float) = 0.08
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "DIWater.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewWS      : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float  crest       : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float _Smoothness;
                float4 _SeaFoamColor;
                float _SeaFoamCutoff;
                float _SeaFoamAmount;
                float _SeaFoamScale;
                float _SeaFoamSpeed;
                float _WaterLevel;
                float _WaveAmplitude;
                float _WaveFrequency;
                float _WaveSpeed;
            CBUFFER_END

            float DI_Fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                [unroll] for (int i = 0; i < 3; i++) { s += a * DI_Noise(p); p = p * 2.0 + 17.0; a *= 0.5; }
                return s / 0.875;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                // Surface animée (mêmes vagues que l'écume du terrain/perso si params identiques).
                posWS.y = DI_WaterSurfaceY(posWS.xz, _WaterLevel, _WaveAmplitude, _WaveFrequency, _WaveSpeed);
                OUT.crest = DI_Crest(posWS.xz, _WaveFrequency, _WaveSpeed);

                const float e = 0.5;
                float hL = DI_WaterSurfaceY(posWS.xz - float2(e, 0), _WaterLevel, _WaveAmplitude, _WaveFrequency, _WaveSpeed);
                float hR = DI_WaterSurfaceY(posWS.xz + float2(e, 0), _WaterLevel, _WaveAmplitude, _WaveFrequency, _WaveSpeed);
                float hD = DI_WaterSurfaceY(posWS.xz - float2(0, e), _WaterLevel, _WaveAmplitude, _WaveFrequency, _WaveSpeed);
                float hU = DI_WaterSurfaceY(posWS.xz + float2(0, e), _WaterLevel, _WaveAmplitude, _WaveFrequency, _WaveSpeed);
                OUT.normalWS = normalize(float3(hL - hR, 2.0 * e, hD - hU));

                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.viewWS = GetWorldSpaceViewDir(posWS);
                OUT.positionWS = posWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 viewDir = normalize(IN.viewWS);
                float fresnel = pow(1.0 - saturate(dot(n, viewDir)), 4.0);

                float3 baseCol = lerp(_DeepColor.rgb, _ShallowColor.rgb, fresnel);
                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(n, mainLight.direction));
                float3 ambient = SampleSH(n);
                float3 lit = baseCol * (mainLight.color * ndotl * 0.5 + ambient + 0.35);

                float3 hv = normalize(mainLight.direction + viewDir);
                float spec = pow(saturate(dot(n, hv)), lerp(16.0, 200.0, _Smoothness));
                lit += mainLight.color * spec * 0.4;

                float alpha = lerp(_DeepColor.a, _ShallowColor.a, fresnel);

                // Moutons : motif fbm organique, renforcé sur les crêtes.
                float fn = DI_Fbm(IN.positionWS.xz * _SeaFoamScale + _Time.y * _SeaFoamSpeed);
                float foam = smoothstep(_SeaFoamCutoff, 1.0, fn * (0.55 + 0.6 * IN.crest)) * _SeaFoamAmount;
                lit = lerp(lit, _SeaFoamColor.rgb, foam);
                alpha = lerp(alpha, 1.0, foam);

                return half4(lit, saturate(alpha));
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
