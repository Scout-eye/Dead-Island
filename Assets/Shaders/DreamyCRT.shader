Shader "Custom/DreamyCRT"
{
    // Effet "Dreamy CRT" plein écran (URP, compatible Blit RenderGraph).
    // Couleurs chaudes/saturées, basse résolution FONDUE (pas pixelisée), bloom doux,
    // color bleed RGB (phosphore), courbure d'écran subtile, scan lines très douces.
    Properties
    {
        // Défauts sains : même sans le script, le rendu reste lisible (pas de noir).
        _Warmth("Warmth", Float) = 0.55
        _Saturation("Saturation", Float) = 1.25
        _Brightness("Brightness", Float) = 1.02
        _Softness("Softness", Float) = 2.2
        _BloomThreshold("Bloom Threshold", Float) = 0.6
        _BloomIntensity("Bloom Intensity", Float) = 0.5
        _BloomSize("Bloom Size", Float) = 6
        _ColorBleed("Color Bleed", Float) = 0.35
        _Curvature("Curvature", Float) = 0.08
        _Vignette("Vignette", Float) = 0.25
        _ScanlineCount("Scanline Count", Float) = 600
        _ScanlineIntensity("Scanline Intensity", Float) = 0.06
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "DreamyCRT"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Passe plein écran auto-suffisante (pas de dépendance à Blit.hlsl).
            // AddBlitPass fournit _BlitTexture (TEXTURE2D_X pour compat XR) et _BlitScaleBias.
            TEXTURE2D_X(_BlitTexture);
            float4 _BlitScaleBias;
            // (sampler_LinearClamp est déjà déclaré par les includes URP)

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float2 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                float2 uv  = GetFullScreenTriangleTexCoord(input.vertexID);
                output.positionCS = float4(pos, 0.0, 1.0);
                output.texcoord = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
                return output;
            }

            float _Warmth;
            float _Saturation;
            float _Softness;          // rayon du flou "basse résolution" (en pixels)
            float _BloomThreshold;
            float _BloomIntensity;
            float _BloomSize;         // rayon du flou bloom (en pixels)
            float _ColorBleed;        // décalage chromatique (phosphore)
            float _Curvature;         // courbure de l'écran
            float _ScanlineCount;     // nombre de lignes
            float _ScanlineIntensity; // intensité (très faible)
            float _Vignette;
            float _Brightness;

            #define SAMPLE(uv) SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, (uv)).rgb

            // Flou doux à 5 taps (centre + 4 diagonales), rayon en pixels.
            float3 Blur(float2 uv, float2 texel, float spreadPx)
            {
                float2 o = texel * spreadPx;
                float3 c = SAMPLE(uv) * 0.4;
                c += SAMPLE(uv + float2( o.x,  o.y)) * 0.15;
                c += SAMPLE(uv + float2(-o.x,  o.y)) * 0.15;
                c += SAMPLE(uv + float2( o.x, -o.y)) * 0.15;
                c += SAMPLE(uv + float2(-o.x, -o.y)) * 0.15;
                return c;
            }

            // Courbure type tube cathodique (distorsion en barillet).
            float2 Curve(float2 uv, float amount)
            {
                float2 c = uv * 2.0 - 1.0;
                float2 bend = c.yx * c.yx * amount;
                c += c * bend;
                return c * 0.5 + 0.5;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = Curve(input.texcoord, _Curvature);

                // Bord d'écran courbé : noir uniquement HORS [0,1] (par multiplication, pas de
                // return brutal). On échantillonne en clamp pour ne jamais lire hors texture.
                float2 inb = step(0.0, uv) * step(uv, 1.0);
                float border = inb.x * inb.y;
                uv = saturate(uv);

                float2 texel = 1.0 / _ScreenParams.xy;
                float2 dir = uv - 0.5;

                // --- Basse résolution fondue + color bleed RGB (canaux décalés) ---
                float2 off = dir * _ColorBleed * 0.03;
                float3 cR = Blur(uv + off, texel, _Softness);
                float3 cG = Blur(uv,        texel, _Softness);
                float3 cB = Blur(uv - off,  texel, _Softness);
                float3 col = float3(cR.r, cG.g, cB.b);

                // --- Bloom doux sur les zones lumineuses ---
                float3 wide = Blur(uv, texel, _BloomSize);
                float3 bright = max(wide - _BloomThreshold, 0.0);
                col += bright * _BloomIntensity;

                // --- Couleurs chaudes + saturation ---
                col *= float3(1.0 + _Warmth * 0.12, 1.0 + _Warmth * 0.03, 1.0 - _Warmth * 0.10);
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(luma.xxx, col, _Saturation);
                col *= _Brightness;

                // --- Scan lines très douces (quasi invisibles) ---
                float scan = 0.5 + 0.5 * sin(uv.y * _ScanlineCount * 6.2831853);
                col *= 1.0 - _ScanlineIntensity * (1.0 - scan);

                // --- Vignette douce ---
                col *= 1.0 - _Vignette * smoothstep(0.35, 0.85, length(dir));

                col *= border; // noir hors de l'écran courbé
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
