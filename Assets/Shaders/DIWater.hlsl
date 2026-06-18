#ifndef DI_WATER_INCLUDED
#define DI_WATER_INCLUDED

// Fonctions PARTAGÉES (eau, terrain, personnages). Les paramètres de vague sont passés en
// arguments (pas de globales) => robuste, aucune dépendance à un composant. Mêmes valeurs par
// défaut sur chaque matériau => surface et écume synchronisées (même _Time).

float DI_Hash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float DI_Noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float a = DI_Hash(i), b = DI_Hash(i + float2(1, 0));
    float c = DI_Hash(i + float2(0, 1)), d = DI_Hash(i + float2(1, 1));
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Hauteur de vague brute (3 directions) ~ [-2.2, 2.2].
float DI_WaveRaw(float2 p, float freq, float speed)
{
    float t = _Time.y * speed;
    float h = sin(dot(p, float2(1.0, 0.4)) * freq + t);
    h += 0.7 * sin(dot(p, float2(-0.35, 1.0)) * freq * 0.85 + t * 1.3);
    h += 0.5 * sin(dot(p, float2(0.8, -0.6)) * freq * 1.7 + t * 0.9);
    return h;
}

float DI_WaterSurfaceY(float2 xz, float level, float amp, float freq, float speed)
{
    return level + DI_WaveRaw(xz, freq, speed) * amp;
}

float DI_Crest(float2 xz, float freq, float speed)
{
    return saturate(DI_WaveRaw(xz, freq, speed) / 2.2 * 0.5 + 0.5);
}

// Écume pour une géométrie OPAQUE près de la ligne d'eau (suit la forme : rivage, jambes, objets).
// Pleine au contact (above=0), s'estompe vers le haut. La montée OSCILLE avec les crêtes (run-up)
// => la lame d'eau s'échoue plus ou moins haut, calée sur les vagues. Patché par bruit.
float DI_ShoreFoam(float3 positionWS, float foamRise, float foamNoiseScale,
                   float level, float amp, float freq, float speed)
{
    float waterY = DI_WaterSurfaceY(positionWS.xz, level, amp, freq, speed);
    float above = positionWS.y - waterY;

    float wave01 = DI_Crest(positionWS.xz, freq, speed);
    float rise = foamRise * (0.35 + 1.3 * wave01);

    float patch = 0.55 + 0.45 * DI_Noise(positionWS.xz * foamNoiseScale + _Time.y * speed);
    float foam = (1.0 - smoothstep(0.0, max(rise, 0.02), above)) * step(-0.03, above) * patch;
    return saturate(foam);
}

#endif
