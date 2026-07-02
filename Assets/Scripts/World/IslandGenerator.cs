using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Génère une île procédurale (zéro asset, déterministe via seed). Pipeline :
    ///
    ///   1. FORME — champ radial SIGNÉ "e" = (rayon - distance), union de 1..N lobes elliptiques
    ///      (orientation / élongation / rayon aléatoires) → formes variées. Coordonnées déformées par
    ///      DOMAIN WARPING → côtes organiques.
    ///   2. CÔTE — grandes ondulations basse fréquence (baies/presqu'îles) + détail fin FBM
    ///      + baies creusées explicites (coves).
    ///   3. PROFIL CÔTIER — "e" passé dans un profil de plage réel (beach face raide au rivage → berm
    ///      → upper/lower shoreface). Cf. paramètres 3a.
    ///   4. RELIEF — RIDGED MULTIFRACTAL (Musgrave) domain-warpé, masqué par l'intériorité
    ///      → crêtes/vallées naturelles au lieu d'un dôme symétrique.
    ///   5. ÉROSION HYDRAULIQUE (<see cref="HydraulicErosion"/>) — vallées/rivières auto-renforcées.
    ///
    /// Le maillage/couleurs est délégué à <see cref="IslandMeshBuilder"/>.
    /// Niveau de l'eau = y 0 (cf. WaterPlane).
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
        [Range(0.3f, 0.85f)] [SerializeField] private float _radius = 0.60f;
        [Range(0, 3)] [SerializeField] private int _maxExtraLobes = 2;
        [Range(0f, 0.5f)] [SerializeField] private float _lobeSpread = 0.34f;
        [SerializeField] private float _lobeRadiusMin = 0.20f;
        [SerializeField] private float _lobeRadiusMax = 0.42f;
        [Tooltip("Élongation des lobes (1 = rond). Axes X/Y tirés indépendamment → ellipses orientées.")]
        [SerializeField] private float _aspectMin = 0.70f;
        [SerializeField] private float _aspectMax = 1.45f;

        [Header("1b. Domain warping (côtes organiques)")]
        [Range(0f, 0.5f)] [SerializeField] private float _warpStrength = 0.24f;
        [SerializeField] private float _warpFrequency = 1.3f;
        [SerializeField] private int _warpOctaves = 3;

        [Header("2. Côte — grandes baies + détail fin")]
        [Tooltip("Grandes ondulations (basse fréquence, forte amplitude) → baies & presqu'îles asymétriques.")]
        [SerializeField] private float _largeCoastFrequency = 1.0f;
        [Range(0f, 0.5f)] [SerializeField] private float _largeCoastAmplitude = 0.26f;
        [Tooltip("Détail fin du rivage (haute fréquence).")]
        [SerializeField] private float _coastFrequency = 1.7f;
        [SerializeField] private int _coastOctaves = 4;
        [Range(0f, 0.35f)] [SerializeField] private float _coastAmplitude = 0.14f;

        [Header("2b. Baies creusées (coves)")]
        [Tooltip("Nb max d'échancrures concaves sur le rivage.")]
        [Range(0, 4)] [SerializeField] private int _maxCoves = 2;
        [Range(0f, 0.4f)] [SerializeField] private float _coveDepth = 0.24f;
        [SerializeField] private float _coveRadiusMin = 0.14f;
        [SerializeField] private float _coveRadiusMax = 0.32f;

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

        [Header("5. Érosion hydraulique")]
        [SerializeField] private bool _erosion = true;
        [SerializeField] private HydraulicErosion.Settings _erosionSettings = HydraulicErosion.Settings.Default;

        [Header("Couleurs (par vertex)")]
        [SerializeField] private IslandMeshBuilder.ColorSettings _colors = new IslandMeshBuilder.ColorSettings
        {
            Sand = new Color(0.85f, 0.78f, 0.55f),
            Grass = new Color(0.27f, 0.5f, 0.22f),
            Rock = new Color(0.42f, 0.41f, 0.43f),
            SandHeight = 1.4f,
            RockSlope = 0.6f,
        };

        [Header("Style")]
        [SerializeField] private bool _flatShading = true;

        [Header("Test (hors réseau)")]
        [SerializeField] private bool _generateOnStart = true;
        [SerializeField] private int _testSeed = 1337;
        [SerializeField] private int _testPlayers = 4;

        public float CurrentSize { get; private set; }
        public bool GenerateOnStart { set => _generateOnStart = value; }

        // Le matériau est partagé entre toutes les îles générées (évite d'en fuiter un par génération).
        private static Material _islandMaterial;

        // --- État de génération (par seed) ---
        private struct Lobe { public Vector2 C; public float Cos, Sin, Ax, Ay, Radius; }
        private struct Cove { public Vector2 C; public float R; }
        private Lobe[] _lobes;
        private Cove[] _coves;
        private Vector2 _warpA, _warpB, _coastOff, _largeCoastOff, _reliefOff, _terrainWarpOff;

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
            _coastOff = RandOffset(rng); _largeCoastOff = RandOffset(rng);
            _reliefOff = RandOffset(rng); _terrainWarpOff = RandOffset(rng);
            BuildLobes(rng);
            BuildCoves(rng);

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
            if (_erosion) HydraulicErosion.Erode(heights, side, _erosionSettings, new System.Random(seed ^ 0x5f3759df));

            // Sommets + triangles.
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

            string meshName = $"Island_{seed}_{players}";
            var mesh = _flatShading
                ? IslandMeshBuilder.BuildFlatShaded(vertices, triangles, meshName + "_flat", _colors)
                : IslandMeshBuilder.BuildSmooth(vertices, uvs, triangles, meshName, _colors);

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

        /// <summary>Place des baies (échancrures concaves) sur le pourtour de l'île.</summary>
        private void BuildCoves(System.Random rng)
        {
            int n = rng.Next(0, _maxCoves + 1);
            _coves = new Cove[n];
            for (int i = 0; i < n; i++)
            {
                double ang = rng.NextDouble() * 2.0 * System.Math.PI;
                float dist = _radius * Mathf.Lerp(0.72f, 1.05f, (float)rng.NextDouble()); // sur le rivage
                var c = new Vector2((float)System.Math.Cos(ang), (float)System.Math.Sin(ang)) * dist;
                float r = Mathf.Lerp(_coveRadiusMin, _coveRadiusMax, (float)rng.NextDouble());
                _coves[i] = new Cove { C = c, R = r };
            }
        }

        // --- Champ d'altitude analytique ---

        /// <summary>Altitude monde (m) au point normalisé p ∈ [-1,1]². Niveau d'eau = 0.</summary>
        private float Elevation(Vector2 p)
        {
            // Domain warping de la forme.
            float wx = Fbm(p * _warpFrequency + _warpA, _warpOctaves) - 0.5f;
            float wy = Fbm(p * _warpFrequency + _warpB, _warpOctaves) - 0.5f;
            Vector2 pw = p + new Vector2(wx, wy) * _warpStrength;

            // Champ radial signé (union des lobes).
            float e = -10f;
            for (int k = 0; k < _lobes.Length; k++)
            {
                Lobe lobe = _lobes[k];
                Vector2 d = pw - lobe.C;
                float rx = (d.x * lobe.Cos + d.y * lobe.Sin) / lobe.Ax;
                float ry = (-d.x * lobe.Sin + d.y * lobe.Cos) / lobe.Ay;
                e = Mathf.Max(e, lobe.Radius - Mathf.Sqrt(rx * rx + ry * ry));
            }

            // Grandes ondulations (baies/presqu'îles) + détail fin fractal.
            e += (Fbm(pw * _largeCoastFrequency + _largeCoastOff, 2) - 0.5f) * _largeCoastAmplitude;
            e += (Fbm(pw * _coastFrequency + _coastOff, _coastOctaves) - 0.5f) * _coastAmplitude;

            // Baies creusées : échancrures concaves sur le rivage.
            for (int c = 0; c < _coves.Length; c++)
            {
                float bd = Vector2.Distance(pw, _coves[c].C);
                e -= _coveDepth * SmoothStep01(1f - Mathf.Clamp01(bd / _coves[c].R));
            }

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
                // Upper shoreface : chute rapide au rivage, puis terrasse (s'aplatit).
                float depth = (1f - Mathf.Exp(-a / _shorefaceTau)) * _shelfDepth;
                // Lower shoreface : pente douce vers le fond au-delà de la terrasse.
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

        // --- Application au GameObject ---

        private void ApplyMesh(Mesh mesh)
        {
            DestroyGeneratedMesh(); // libère le mesh de la génération précédente
            GetComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial == null)
            {
                if (_islandMaterial == null)
                {
                    var shader = Shader.Find("DeadIsland/VertexColorLit");
                    if (shader != null) _islandMaterial = new Material(shader);
                    else Debug.LogWarning("[Island] Shader DeadIsland/VertexColorLit introuvable.");
                }
                renderer.sharedMaterial = _islandMaterial;
            }

            var collider = GetComponent<MeshCollider>();
            if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }

        private void DestroyGeneratedMesh()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var old = mf.sharedMesh;
            mf.sharedMesh = null;
            if (Application.isPlaying) Destroy(old);
            else DestroyImmediate(old);
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
    }
}
