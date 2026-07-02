using UnityEngine;
using UnityEngine.Rendering;

namespace Game.World
{
    /// <summary>
    /// Fabrique un lens flare PROCÉDURAL (système "Lens Flare SRP" d'URP) pour une lumière
    /// directionnelle : halo central + cercles fantômes le long de l'axe optique, sans texture.
    /// L'occlusion est activée → le flare disparaît quand la source passe derrière le décor.
    /// Utilisé par <see cref="DayNightCycle"/> pour le soleil et la lune.
    /// </summary>
    public static class LensFlareUtil
    {
        /// <summary>Ajoute (une fois) un flare à la lumière. Retourne le composant pour le piloter.</summary>
        public static LensFlareComponentSRP Attach(Light light, float intensity, Color tint)
        {
            if (light == null) return null;
            var existing = light.GetComponent<LensFlareComponentSRP>();
            if (existing != null) return existing;

            var flare = light.gameObject.AddComponent<LensFlareComponentSRP>();
            flare.lensFlareData = BuildData(tint);
            flare.intensity = intensity;
            flare.scale = 1f;
            flare.attenuationByLightShape = true; // suit l'intensité/couleur de la lumière
            flare.useOcclusion = true;            // caché derrière l'île/objets
            flare.allowOffScreen = false;
            return flare;
        }

        private static LensFlareDataSRP BuildData(Color tint)
        {
            var data = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            data.name = "ProceduralFlare";
            data.elements = new[]
            {
                // Cœur lumineux sur la source.
                Circle(tint,          0.90f, 0.00f, 0.55f, 1.00f),
                // Fantômes le long de l'axe (source → centre écran → au-delà).
                Circle(tint * 0.95f,  0.12f, 0.35f, 0.20f, 0.60f),
                Circle(tint * 0.85f,  0.10f, 0.65f, 0.11f, 0.55f),
                Circle(tint * 0.85f,  0.07f, 1.05f, 0.28f, 0.45f),
                Circle(tint,          0.06f, -0.40f, 0.14f, 0.60f),
            };
            return data;
        }

        private static LensFlareDataElementSRP Circle(Color tint, float intensity, float position,
                                                      float scale, float fallOff)
        {
            return new LensFlareDataElementSRP
            {
                flareType = SRPLensFlareType.Circle, // procédural : aucune texture requise
                tint = tint,
                localIntensity = intensity,
                position = position,
                uniformScale = scale,
                sizeXY = Vector2.one,
                count = 1,
                blendMode = SRPLensFlareBlendMode.Additive,
                autoRotate = false,
                modulateByLightColor = true,         // orange au couchant, bleuté pour la lune
                fallOff = fallOff,
                edgeOffset = 0.1f,
                inverseSDF = false,
            };
        }
    }
}
