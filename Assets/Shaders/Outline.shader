Shader "DeadIsland/Outline"
{
    // Contour par "coque inversée" : on dessine le mesh légèrement gonflé le long des normales,
    // en n'affichant que les faces arrière (Cull Front) -> il dépasse autour de l'objet.
    Properties
    {
        _OutlineColor ("Color", Color) = (1,1,1,1)
        _OutlineWidth ("Width (m)", Float) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }

        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings  { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                // Extrusion en espace MONDE -> épaisseur constante en mètres, quelle que soit l'échelle.
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(IN.normalOS);
                posWS += normalize(nWS) * _OutlineWidth;
                OUT.positionHCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }
    }
}
