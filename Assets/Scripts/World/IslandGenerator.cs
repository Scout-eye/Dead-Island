using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Génère une île procédurale (zéro asset, déterministe via seed). Pipeline :
    ///
    ///   1. FORME — champ radial SIGNÉ "e" = (rayon - distance), union de 1..N lobes elliptiques
    ///      (orientation / élongation / rayon aléatoires) → formes variées. Coordonnées déformées par
    ///      DOMAIN WARPING → côtes organiques.
    ///   2. CÔTE — rayon perturbé par FBM (irrégularité du rivage).
    ///   3. PROFIL CÔTIER — "e" passé dans un profil de plage réel (beach face raide au rivage → berm
    ///      → upper/lower shoreface). Cf. paramètres 3a.
    ///   4. RELIEF — RIDGED MULTIFRACTAL (Musgrave : octaves pondérées par la précédente) domain-warpé,
    ///      masqué par l'intériorité → crêtes/vallées naturelles au lieu d'un dôme symétrique.
    ///   5. ÉROSION HYDRAULIQUE (optionnelle) — simulation de gouttes (à la Sebastian Lague) qui creusent
    ///      des vallées/rivières auto-renforcées → terrain crédible.
    ///
    /// Niveau de l'eau = y 0 (cf. WaterPlane). Couleurs par vertex (sable / herbe / roche).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    public sealed class IslandGenerator : MonoBehaviour
    {
        [Header("Taille (selon nb de joueurs)")]
        [SerializeField] private float _baseSize = 200f;
        [SerializeField] private float _sizePerPlayer = 26f;
        [Tooltip("Taille de quad visée (m). La résolution s'adapte à la taille de l'île.")]
        [SerializeField] private float _targetQuadSize = 1.4f;
        [SerializeField] private int _maxResolution = 220;

        [Header("1. Forme — lobes & rayon")]
        [Range(0.3f, 0.85f)] [SerializeField] private float _radius = 0.62f;
        [Range(0, 3)] [SerializeField] private int _maxExtraLobes = 2;
        [Range(0f, 0.5f)] [SerializeField] private float _lobeSpread = 0.34f;
        [SerializeField] private float _lobeRadiusMin = 0.20f;
        [SerializeField] private float _lobeRadiusMax = 0.42f;
        [Tooltip("Élongation des lobes (1 = rond). Axes X/Y tirés indépendamment → ellipses orientées.")]
        [SerializeField] private float _aspectMin = 0.70f;
        [SerializeField] private float _aspectMax = 1.45f;

        [Header("1b. Domain warping (côtes organiques)")]
        [Range(0f, 0.5f)] [SerializeField] private float _warpStrength = 0.18f;
        [SerializeField] private float _warpFrequency = 1.3f;
        [SerializeField] private int _warpOctaves = 2;

        [Header("2. Irrégularité de la côte")]
        [SerializeField] private float _coastFrequency = 1.7f;
        [SerializeField] private int _coastOctaves = 4;
        [Range(0f, 0.35f)] [SerializeField] private float _coastAmplitude = 0.14f;

        [Header("3a. Profil côtier (d'après un vrai profil de plage)")]
        [Tooltip("Raideur du bas de plage / swash AU RIVAGE (petit = plus raide).")]
        [Range(0.02f, 0.15f)] [SerializeField] private float _beachFaceTau = 0.05f;
        [Tooltip("Hauteur du berm / plage sèche (m).")]
        [SerializeField] private float _beachHeight = 1.1f;
        [Tooltip("Intériorité (champ) à partir de laquelle le relief prend le relais du berm.")]
        [Range(0.05f, 0.4f)] [SerializeField] private float _beachReach = 0.09f;
        [Tooltip("Raideur de la chute SOUS L'EAU au rivage (upper shoreface). Petit = drop plus rapide.")]
        [Range(0.02f, 0.15f)] [SerializeField] private float _shorefaceTau = 0.035f;
        [Tooltip("Profondeur de la terrasse (m) où la pente s'adoucit.")]
        [SerializeField] private float _shelfDepth = 1.6f;
        [Tooltip("Distance (champ) où commence le lower shoreface (pente douce vers le fond).")]
        [Range(0.05f, 0.4f)] [SerializeField] private float _shelfReach = 0.13f;
        [SerializeField] private float _deepSlope = 22f;
        [SerializeField] private float _seabedDepth = 12f;

        [Header("4. Relief — ridged multifractal (Musgrave)")]
        [Tooltip("Intériorité (champ) sur laquelle le relief monte depuis la côte.")]
        [Range(0.1f, 0.8f)] [SerializeField] private float _inlandFull = 0.22f;
        [Tooltip("Hauteur max du relief (m) au cœur de l'île.")]
        [SerializeField] private float _reliefHeight = 38f;
        [Tooltip("Nb de features de relief à travers l'île. Bas = 1 bosse lisse, haut = chaîne.")]
        [SerializeField] private float _reliefFrequency = 6f;
        [SerializeField] private int _reliefOctaves = 5;
        [SerializeField] private float _lacunarity = 2f;
        [Tooltip("Netteté des crêtes (Musgrave gain). Haut = arêtes marquées.")]
        [SerializeField] private float _ridgeGain = 2f;
        [Range(0.8f, 1.2f)] [SerializeField] private float _ridgeOffset = 1f;
        [Range(0f, 1f)] [SerializeField] private float _ridgeNormalize = 0.72f;
        [Tooltip("Domain warp du relief → crêtes sinueuses (tectonique).")]
        [Range(0f, 0.6f)] [SerializeField] private float _terrainWarp = 0.25f;
        [Tooltip("0 = collines douces (fbm), 1 = tout en crêtes (ridged).")]
        [Range(0f, 1f)] [SerializeField] private float _ridgeBlend = 0.65f;

        [Header("5. Érosion hydraulique (gouttes)")]
        [SerializeField] private bool _erosion = true;
        [Tooltip("Nombre de gouttes simulées. Plus = plus érodé (et plus lent).")]
        [SerializeField] private int _erosionDroplets = 45000;
        [Range(1, 6)] [SerializeField] private int _erosionRadius = 3;
        [Range(0f, 1f)] [SerializeField] private float _inertia = 0.05f;
        [SerializeField] private float _sedimentCapacity = 4f;
        [SerializeField] private float _minSlope = 0.01f;
        [Range(0f, 1f)] [SerializeField] private float _erodeSpeed = 0.3f;
        [Range(0f, 1f)] [SerializeField] private float _depositSpeed = 0.3f;
        [Range(0f, 0.1f)] [SerializeField] private float _evaporate = 0.02f;
        [SerializeField] private float _gravity = 4f;
        [SerializeField] private int _dropletLifetime = 30;

        [Header("Couleurs (par vertex)")]
        [SerializeField] private Color _sand = new Color(0.85f, 0.78f, 0.55f);
        [SerializeField] private Color _grass = new Color(0.27f, 0.5f, 0.22f);
        [SerializeField] private Color _rock = new Color(0.42f, 0.41f, 0.43f);
        [Tooltip("Hauteur (m) jusqu'où le sable domine.")]
        [SerializeField] private float _sandColorHeight = 1.4f;
        [Range(0f, 1f)] [SerializeField] private float _rockSlope = 0.6f;

        [Header("Style")]
        [SerializeField] private bool _flatShading = true;

        [Header("Test (hors réseau)")]
        [SerializeField] private bool _generateOnStart = true;
        [SerializeField] private int _testSeed = 1337;
        [SerializeField] private int _testPlayers = 4;

        public float CurrentSize { get; private set; }
        public bool GenerateOnStart { set => _generateOnStart = value; }

        // --- État de génération (par seed) ---
        private struct Lobe { public Vector2 C; public float Cos, Sin, Ax, Ay, Radius; }
        private Lobe[] _lobes;
        private Vector2 _warpA, _warpB, _coastOff, _reliefOff, _terrainWarpOff;

        private void Start()
        {
            if (_generateOnStart) Generate(_testSeed, _testPlayers);
        }

        [ContextMenu("Régénérer (test)")]
        private void RegenerateForEditor() => Generate(_testSeed, _testPlayers);

        public void Generate(int seed, int players)
        {
            float size = _baseSize + _sizePerPlayer * Mathf.Max(0, players - 1);
            CurrentSize = size;
            int res = Mathf.Clamp(Mathf.RoundToInt(size / Mathf.Max(0.3f, _targetQuadSize)), 16, _maxResolution);

            var rng = new System.Random(seed);
            _warpA = RandOffset(rng); _warpB = RandOffset(rng);
            _coastOff = RandOffset(rng); _reliefOff = RandOffset(rng); _terrainWarpOff = RandOffset(rng);
            BuildLobes(rng);

            int side = res + 1;

            // 1-4. Heightfield analytique.
            var heights = new float[side * side];
            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    float u = (float)x / res;
                    float v = (float)z / res;
                    heights[z * side + x] = Elevation(new Vector2(u - 0.5f, v - 0.5f) * 2f);
                }
            }

            // 5. Érosion hydraulique (creuse vallées/rivières).
            if (_erosion) Erode(heights, side, new System.Random(seed ^ 0x5f3759df));

            // Sommets.
            var vertices = new Vector3[side * side];
            var uvs = new Vector2[side * side];
            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    int idx = z * side + x;
                    float u = (float)x / res, v = (float)z / res;
                    vertices[idx] = new Vector3((u - 0.5f) * size, heights[idx], (v - 0.5f) * size);
                    uvs[idx] = new Vector2(u, v);
                }
            }

            var triangles = new int[res * res * 6];
            int t = 0;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i0 = z * side + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + side;
                    int i3 = i2 + 1;
                    triangles[t++] = i0; triangles[t++] = i2; triangles[t++] = i1;
                    triangles[t++] = i1; triangles[t++] = i2; triangles[t++] = i3;
                }
            }

            var mesh = _flatShading
                ? BuildFlatShaded(vertices, triangles, seed, players)
                : BuildSmooth(vertices, uvs, triangles, seed, players);

            ApplyMesh(mesh);
        }

        // --- Forme ---

        private void BuildLobes(System.Random rng)
        {
            int extra = rng.Next(0, _maxExtraLobes + 1);
            _lobes = new Lobe[1 + extra];
            _lobes[0] = MakeLobe(Vector2.zero, _radius, rng);
            for (int k = 1; k <= extra; k++)
            {
                Vector2 c = RandInDisc(rng) * _lobeSpread;
                float rad = Mathf.Lerp(_lobeRadiusMin, _lobeRadiusMax, (float)rng.NextDouble());
                _lobes[k] = MakeLobe(c, rad, rng);
            }
        }

        private Lobe MakeLobe(Vector2 c, float radius, System.Random rng)
        {
            float rot = (float)rng.NextDouble() * Mathf.PI;
            float ax = Mathf.Lerp(_aspectMin, _aspectMax, (float)rng.NextDouble());
            float ay = Mathf.Lerp(_aspectMin, _aspectMax, (float)rng.NextDouble());
            return new Lobe { C = c, Cos = Mathf.Cos(rot), Sin = Mathf.Sin(rot), Ax = ax, Ay = ay, Radius = radius };
        }

        // --- Champ d'altitude analytique ---

        /// <summary>Altitude monde (m) au point normalisé p ∈ [-1,1]². Niveau d'eau = 0.</summary>
        private float Elevation(Vector2 p)
        {
            // Domain warping de la forme.
            float wx = Fbm(p * _warpFrequency + _warpA, _warpOctaves) - 0.5f;
            float wy = Fbm(p * _warpFrequency + _warpB, _warpOctaves) - 0.5f;
            Vector2 pw = p + new Vector2(wx, wy) * _warpStrength;

            // Champ radial signé (union des lobes) + irrégularité de côte.
            float e = -10f;
            for (int k = 0; k < _lobes.Length; k++)
            {
                Lobe lobe = _lobes[k];
                Vector2 d = pw - lobe.C;
                float rx = (d.x * lobe.Cos + d.y * lobe.Sin) / lobe.Ax;
                float ry = (-d.x * lobe.Sin + d.y * lobe.Cos) / lobe.Ay;
                e = Mathf.Max(e, lobe.Radius - Mathf.Sqrt(rx * rx + ry * ry));
            }
            e += (Fbm(pw * _coastFrequency + _coastOff, _coastOctaves) - 0.5f) * _coastAmplitude;

            if (e >= 0f)
            {
                // Beach face (raide au rivage) → berm.
                float h = (1f - Mathf.Exp(-e / _beachFaceTau)) * _beachHeight;
                // Relief : monte depuis la côte (masque), piloté par le bruit (non symétrique).
                float mask = SmoothStep01(Mathf.Clamp01((e - _beachReach) / _inlandFull));
                if (mask > 0f) h += mask * TerrainNoise(pw) * _reliefHeight;
                return h;
            }
            else
            {
                float a = -e;
                float depth = (1f - Mathf.Exp(-a / _shorefaceTau)) * _shelfDepth;
                depth += Mathf.Max(0f, a - _shelfReach) * _deepSlope;
                return Mathf.Max(-depth, -_seabedDepth);
            }
        }

        /// <summary>Relief 0..1 : ridged multifractal domain-warpé, mélangé à des collines douces.</summary>
        private float TerrainNoise(Vector2 pw)
        {
            Vector2 s = pw * _reliefFrequency + _reliefOff;
            // Domain warp du relief (crêtes sinueuses).
            float twx = Fbm(s * 0.5f + _terrainWarpOff, 2) - 0.5f;
            float twy = Fbm(s * 0.5f + _terrainWarpOff * 1.7f, 2) - 0.5f;
            s += new Vector2(twx, twy) * _terrainWarp;

            float ridged = RidgedMultifractal(s, _reliefOctaves);
            float hills = Fbm(s * 0.6f, 3);
            return Mathf.Clamp01(Mathf.Lerp(hills, ridged, _ridgeBlend));
        }

        // --- Bruits ---

        /// <summary>FBM Perlin (0..1).</summary>
        private float Fbm(Vector2 s, int octaves)
        {
            float amp = 1f, freq = 1f, sum = 0f, norm = 0f;
            for (int i = 0; i < Mathf.Max(1, octaves); i++)
            {
                sum += amp * Mathf.PerlinNoise(s.x * freq, s.y * freq);
                norm += amp;
                amp *= 0.5f;
                freq *= _lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>
        /// Ridged multifractal de Musgrave (0..1) : chaque octave est pondérée par la précédente
        /// (poids = signal×gain borné). Les crêtes gagnent du détail, les vallées restent lisses.
        /// </summary>
        private float RidgedMultifractal(Vector2 s, int octaves)
        {
            const float H = 1f;
            float freq = 1f;
            float n = Mathf.PerlinNoise(s.x, s.y) * 2f - 1f;
            float signal = _ridgeOffset - Mathf.Abs(n);
            signal *= signal;
            float result = signal;

            for (int i = 1; i < Mathf.Max(1, octaves); i++)
            {
                freq *= _lacunarity;
                float weight = Mathf.Clamp01(signal * _ridgeGain);
                n = Mathf.PerlinNoise(s.x * freq + 0.37f, s.y * freq + 0.11f) * 2f - 1f;
                signal = _ridgeOffset - Mathf.Abs(n);
                signal *= signal;
                signal *= weight;
                result += signal * Mathf.Pow(freq, -H);
            }
            return Mathf.Clamp01(result * _ridgeNormalize);
        }

        // --- Érosion hydraulique (droplets, façon Sebastian Lague) ---

        private void Erode(float[] map, int mapSize, System.Random rng)
        {
            // Brosse d'érosion (répartit le creusement sur un voisinage → pas de pics).
            BuildBrush(mapSize, out int[] brushDx, out int[] brushDy, out float[] brushW);

            for (int drop = 0; drop < _erosionDroplets; drop++)
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

                for (int life = 0; life < _dropletLifetime; life++)
                {
                    int nodeX = (int)posX;
                    int nodeY = (int)posY;
                    int nodeIdx = nodeY * mapSize + nodeX;
                    float offX = posX - nodeX;
                    float offY = posY - nodeY;

                    HeightAndGradient(map, mapSize, posX, posY, out float gradX, out float gradY, out float height);

                    // Direction : inertie vs pente.
                    dirX = dirX * _inertia - gradX * (1f - _inertia);
                    dirY = dirY * _inertia - gradY * (1f - _inertia);
                    float len = Mathf.Sqrt(dirX * dirX + dirY * dirY);
                    if (len > 1e-6f) { dirX /= len; dirY /= len; }
                    else break; // pas de pente → la goutte s'arrête

                    posX += dirX;
                    posY += dirY;
                    if (posX < 0f || posX >= mapSize - 1 || posY < 0f || posY >= mapSize - 1) break;

                    HeightAndGradient(map, mapSize, posX, posY, out _, out _, out float newHeight);
                    float deltaHeight = newHeight - height;

                    float capacity = Mathf.Max(-deltaHeight, _minSlope) * speed * water * _sedimentCapacity;

                    if (sediment > capacity || deltaHeight > 0f)
                    {
                        // Dépose (remonte ou trop-plein) — aux 4 coins de la cellule d'origine.
                        float deposit = (deltaHeight > 0f)
                            ? Mathf.Min(deltaHeight, sediment)
                            : (sediment - capacity) * _depositSpeed;
                        sediment -= deposit;
                        map[nodeIdx] += deposit * (1f - offX) * (1f - offY);
                        map[nodeIdx + 1] += deposit * offX * (1f - offY);
                        map[nodeIdx + mapSize] += deposit * (1f - offX) * offY;
                        map[nodeIdx + mapSize + 1] += deposit * offX * offY;
                    }
                    else
                    {
                        // Érode (répartit via la brosse), sans dépasser le dénivelé.
                        float erode = Mathf.Min((capacity - sediment) * _erodeSpeed, -deltaHeight);
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

                    speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed + deltaHeight * _gravity));
                    water *= (1f - _evaporate);
                    if (water < 1e-3f) break;
                }
            }
        }

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

        private void BuildBrush(int mapSize, out int[] dx, out int[] dy, out float[] weights)
        {
            int r = Mathf.Clamp(_erosionRadius, 1, 6);
            var lx = new System.Collections.Generic.List<int>();
            var ly = new System.Collections.Generic.List<int>();
            var lw = new System.Collections.Generic.List<float>();
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

        // --- Maillage & couleurs ---

        private Mesh BuildSmooth(Vector3[] vertices, Vector2[] uvs, int[] triangles, int seed, int players)
        {
            var mesh = new Mesh { name = $"Island_{seed}_{players}" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var normals = mesh.normals;
            var colors = new Color[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                colors[i] = VertexColor(vertices[i].y, 1f - Mathf.Clamp01(normals[i].y));
            mesh.colors = colors;
            return mesh;
        }

        private Mesh BuildFlatShaded(Vector3[] vertices, int[] triangles, int seed, int players)
        {
            int n = triangles.Length;
            var fv = new Vector3[n];
            var fn = new Vector3[n];
            var fc = new Color[n];
            var ft = new int[n];

            for (int i = 0; i < n; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];

                Vector3 nrm = Vector3.Cross(b - a, c - a).normalized;
                if (nrm.y < 0f) nrm = -nrm;
                float slope = 1f - Mathf.Clamp01(nrm.y);

                fv[i] = a; fv[i + 1] = b; fv[i + 2] = c;
                fn[i] = fn[i + 1] = fn[i + 2] = nrm;
                fc[i] = VertexColor(a.y, slope);
                fc[i + 1] = VertexColor(b.y, slope);
                fc[i + 2] = VertexColor(c.y, slope);
                ft[i] = i; ft[i + 1] = i + 1; ft[i + 2] = i + 2;
            }

            var mesh = new Mesh { name = $"Island_{seed}_{players}_flat" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = fv;
            mesh.normals = fn;
            mesh.triangles = ft;
            mesh.colors = fc;
            mesh.RecalculateBounds();
            return mesh;
        }

        private Color VertexColor(float height, float slope)
        {
            float sandWeight = 1f - SmoothStep(_sandColorHeight - 0.5f, _sandColorHeight + 0.5f, height);
            Color baseCol = Color.Lerp(_grass, _sand, sandWeight);
            float rockWeight = SmoothStep(_rockSlope, _rockSlope + 0.15f, slope);
            return Color.Lerp(baseCol, _rock, rockWeight);
        }

        private void ApplyMesh(Mesh mesh)
        {
            GetComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial == null)
            {
                var shader = Shader.Find("DeadIsland/VertexColorLit");
                if (shader != null) renderer.sharedMaterial = new Material(shader);
                else Debug.LogWarning("[Island] Shader DeadIsland/VertexColorLit introuvable.");
            }

            var collider = GetComponent<MeshCollider>();
            if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }

        // --- Utilitaires ---

        private static Vector2 RandOffset(System.Random rng)
            => new Vector2((float)rng.NextDouble() * 10000f, (float)rng.NextDouble() * 10000f);

        private static Vector2 RandInDisc(System.Random rng)
        {
            double ang = rng.NextDouble() * 2.0 * System.Math.PI;
            double rr = System.Math.Sqrt(rng.NextDouble());
            return new Vector2((float)(System.Math.Cos(ang) * rr), (float)(System.Math.Sin(ang) * rr));
        }

        private static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            if (Mathf.Approximately(edge0, edge1)) return x < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
