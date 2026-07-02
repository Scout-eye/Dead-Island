using System.Collections.Generic;
using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Érosion hydraulique par gouttes (façon Sebastian Lague) sur un heightfield carré.
    /// Chaque goutte descend la pente, arrache du sédiment quand elle accélère et le dépose
    /// quand elle ralentit → vallées / ravines auto-renforcées, terrain crédible.
    ///
    /// Classe PURE (aucun état, aucun composant) : déterministe via le rng fourni.
    /// </summary>
    public static class HydraulicErosion
    {
        [System.Serializable]
        public struct Settings
        {
            [Tooltip("Nombre de gouttes simulées. Plus = plus érodé (et plus lent).")]
            public int Droplets;
            [Range(1, 6)] public int Radius;
            [Range(0f, 1f)] public float Inertia;
            public float SedimentCapacity;
            public float MinSlope;
            [Range(0f, 1f)] public float ErodeSpeed;
            [Range(0f, 1f)] public float DepositSpeed;
            [Range(0f, 0.1f)] public float Evaporate;
            public float Gravity;
            public int DropletLifetime;

            public static Settings Default => new Settings
            {
                Droplets = 45000,
                Radius = 3,
                Inertia = 0.05f,
                SedimentCapacity = 4f,
                MinSlope = 0.01f,
                ErodeSpeed = 0.3f,
                DepositSpeed = 0.3f,
                Evaporate = 0.02f,
                Gravity = 4f,
                DropletLifetime = 30,
            };
        }

        /// <summary>Érode le heightfield en place. map = mapSize × mapSize (row-major).</summary>
        public static void Erode(float[] map, int mapSize, Settings s, System.Random rng)
        {
            // Brosse d'érosion (répartit le creusement sur un voisinage → pas de pics).
            BuildBrush(s.Radius, out int[] brushDx, out int[] brushDy, out float[] brushW);

            for (int drop = 0; drop < s.Droplets; drop++)
            {
                // Départ biaisé vers la terre émergée (évite de gaspiller des gouttes sur le fond plat).
                float posX = 0f, posY = 0f;
                for (int tries = 0; tries < 8; tries++)
                {
                    posX = (float)rng.NextDouble() * (mapSize - 1);
                    posY = (float)rng.NextDouble() * (mapSize - 1);
                    if (map[(int)posY * mapSize + (int)posX] > 0f) break;
                }
                float dirX = 0f, dirY = 0f;
                float speed = 1f, water = 1f, sediment = 0f;

                for (int life = 0; life < s.DropletLifetime; life++)
                {
                    int nodeX = (int)posX;
                    int nodeY = (int)posY;
                    int nodeIdx = nodeY * mapSize + nodeX;
                    float offX = posX - nodeX;
                    float offY = posY - nodeY;

                    HeightAndGradient(map, mapSize, posX, posY, out float gradX, out float gradY, out float height);

                    // Direction : inertie vs pente.
                    dirX = dirX * s.Inertia - gradX * (1f - s.Inertia);
                    dirY = dirY * s.Inertia - gradY * (1f - s.Inertia);
                    float len = Mathf.Sqrt(dirX * dirX + dirY * dirY);
                    if (len > 1e-6f) { dirX /= len; dirY /= len; }
                    else break; // pas de pente → la goutte s'arrête

                    posX += dirX;
                    posY += dirY;
                    if (posX < 0f || posX >= mapSize - 1 || posY < 0f || posY >= mapSize - 1) break;

                    HeightAndGradient(map, mapSize, posX, posY, out _, out _, out float newHeight);
                    float deltaHeight = newHeight - height;

                    float capacity = Mathf.Max(-deltaHeight, s.MinSlope) * speed * water * s.SedimentCapacity;

                    if (sediment > capacity || deltaHeight > 0f)
                    {
                        // Dépose (remonte ou trop-plein) — aux 4 coins de la cellule d'origine.
                        float deposit = (deltaHeight > 0f)
                            ? Mathf.Min(deltaHeight, sediment)
                            : (sediment - capacity) * s.DepositSpeed;
                        sediment -= deposit;
                        map[nodeIdx] += deposit * (1f - offX) * (1f - offY);
                        map[nodeIdx + 1] += deposit * offX * (1f - offY);
                        map[nodeIdx + mapSize] += deposit * (1f - offX) * offY;
                        map[nodeIdx + mapSize + 1] += deposit * offX * offY;
                    }
                    else
                    {
                        // Érode (répartit via la brosse), sans dépasser le dénivelé.
                        float erode = Mathf.Min((capacity - sediment) * s.ErodeSpeed, -deltaHeight);
                        for (int b = 0; b < brushDx.Length; b++)
                        {
                            int bx = nodeX + brushDx[b];
                            int by = nodeY + brushDy[b];
                            if (bx < 0 || by < 0 || bx >= mapSize || by >= mapSize) continue;
                            int bi = by * mapSize + bx;
                            float delta = erode * brushW[b];
                            map[bi] -= delta;
                            sediment += delta;
                        }
                    }

                    speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed + deltaHeight * s.Gravity));
                    water *= (1f - s.Evaporate);
                    if (water < 1e-3f) break;
                }
            }
        }

        /// <summary>Hauteur + gradient bilinéaires à une position fractionnaire.</summary>
        private static void HeightAndGradient(float[] map, int mapSize, float posX, float posY,
                                              out float gradX, out float gradY, out float height)
        {
            int nx = (int)posX;
            int ny = (int)posY;
            float fx = posX - nx;
            float fy = posY - ny;
            int i = ny * mapSize + nx;
            float hNW = map[i], hNE = map[i + 1], hSW = map[i + mapSize], hSE = map[i + mapSize + 1];

            gradX = (hNE - hNW) * (1f - fy) + (hSE - hSW) * fy;
            gradY = (hSW - hNW) * (1f - fx) + (hSE - hNE) * fx;
            height = hNW * (1f - fx) * (1f - fy) + hNE * fx * (1f - fy)
                   + hSW * (1f - fx) * fy + hSE * fx * fy;
        }

        private static void BuildBrush(int radius, out int[] dx, out int[] dy, out float[] weights)
        {
            int r = Mathf.Clamp(radius, 1, 6);
            var lx = new List<int>();
            var ly = new List<int>();
            var lw = new List<float>();
            float sum = 0f;
            for (int oy = -r; oy <= r; oy++)
            {
                for (int ox = -r; ox <= r; ox++)
                {
                    float dist = Mathf.Sqrt(ox * ox + oy * oy);
                    if (dist > r) continue;
                    float w = 1f - dist / r; // décroît vers le bord
                    lx.Add(ox); ly.Add(oy); lw.Add(w); sum += w;
                }
            }
            dx = lx.ToArray(); dy = ly.ToArray(); weights = lw.ToArray();
            if (sum > 0f) for (int i = 0; i < weights.Length; i++) weights[i] /= sum; // normalisé
        }
    }
}
