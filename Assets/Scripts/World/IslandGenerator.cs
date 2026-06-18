using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Génère une île procédurale (zéro asset, déterministe via seed) en s'appuyant sur une
    /// DISTANCE À LA CÔTE lissée :
    ///   1-2. FORME & CÔTE : distance radiale au centre, perturbée par du bruit (côtes organiques)
    ///        + une 2e noise pour les baies/presqu'îles. La transition est douce par construction
    ///        => plage en pente (pas de mur).
    ///   3.   ALTITUDE : dôme doux (landDepth × _heightScale) pour une côte progressive + relief
    ///        (montagnes) pondéré par "l'intériorité" (loin de la côte = plus haut, crêtes qui
    ///        s'effacent au bord). Couleurs par vertex.
    ///
    /// _heightScale règle la pente côtière : bas = plage très douce, haut = côtes raides/falaises.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    public sealed class IslandGenerator : MonoBehaviour
    {
        [Header("Taille (selon nb de joueurs)")]
        [SerializeField] private float _baseSize = 80f;
        [SerializeField] private float _sizePerPlayer = 18f;
        [SerializeField] private int _resolution = 150;

        [Header("Étapes 1-2 — Forme & côte")]
        [Tooltip("Rayon de l'île (0..1 ; ~distance normalisée). Plus grand = île plus large.")]
        [Range(0.2f, 0.9f)] [SerializeField] private float _coastRadius = 0.6f;
        [SerializeField] private float _coastFrequency = 1.5f;
        [SerializeField] private int _coastOctaves = 3;
        [Tooltip("Irrégularité de la côte (déforme le rayon).")]
        [Range(0f, 0.5f)] [SerializeField] private float _coastWarp = 0.2f;
        [SerializeField] private float _bayFrequency = 4f;
        [Tooltip("Profondeur des baies / presqu'îles.")]
        [Range(0f, 0.4f)] [SerializeField] private float _bayStrength = 0.12f;

        [Header("Étape 3 — Altitude")]
        [Tooltip("Pente de la côte : BAS = plage douce, HAUT = côtes raides / falaises.")]
        [SerializeField] private float _heightScale = 8f;
        [Tooltip("Distance à la côte (normalisée) pour atteindre le relief plein (montagnes au centre).")]
        [Range(0.05f, 0.5f)] [SerializeField] private float _interiorWidth = 0.2f;
        [SerializeField] private float _maxHeight = 14f;
        [SerializeField] private float _reliefFrequency = 3f;
        [SerializeField] private int _reliefOctaves = 5;
        [SerializeField] private float _persistence = 0.5f;
        [SerializeField] private float _lacunarity = 2f;
        [Range(1f, 4f)] [SerializeField] private float _heightExponent = 2f;
        [SerializeField] private float _underwaterScale = 25f;
        [SerializeField] private float _underwaterDepth = 8f;

        [Header("Couleurs (par vertex)")]
        [SerializeField] private Color _sand = new Color(0.85f, 0.78f, 0.55f);
        [SerializeField] private Color _grass = new Color(0.27f, 0.5f, 0.22f);
        [SerializeField] private Color _rock = new Color(0.42f, 0.41f, 0.43f);
        [SerializeField] private float _beachHeight = 1.0f;
        [Range(0f, 1f)] [SerializeField] private float _rockSlope = 0.6f;

        [Header("Style")]
        [SerializeField] private bool _flatShading = true;

        [Header("Test (hors réseau)")]
        [SerializeField] private bool _generateOnStart = true;
        [SerializeField] private int _testSeed = 1337;
        [SerializeField] private int _testPlayers = 4;

        public float CurrentSize { get; private set; }
        public bool GenerateOnStart { set => _generateOnStart = value; }

        private void Start()
        {
            if (_generateOnStart) Generate(_testSeed, _testPlayers);
        }

        [ContextMenu("Régénérer (test)")]
        private void RegenerateForEditor() => Generate(_testSeed, _testPlayers);

        public void Generate(int seed, int players)
        {
            int res = Mathf.Max(8, _resolution);
            float size = _baseSize + _sizePerPlayer * Mathf.Max(0, players - 1);
            CurrentSize = size;

            var rng = new System.Random(seed);
            Vector2 coastOff = RandOffset(rng);
            Vector2 bayOff = RandOffset(rng);
            Vector2 reliefOff = RandOffset(rng);

            int side = res + 1;
            var vertices = new Vector3[side * side];
            var uvs = new Vector2[side * side];

            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    int idx = z * side + x;
                    float u = (float)x / res;
                    float v = (float)z / res;

                    float landDepth = LandDepth(u, v, coastOff, bayOff); // >0 = à l'intérieur des terres
                    float h;
                    if (landDepth > 0f)
                    {
                        // Dôme doux (côte progressive) + relief pondéré par l'intériorité.
                        float interiority = SmoothStep(0f, _interiorWidth, landDepth);
                        float relief = Mathf.Pow(Mathf.Clamp01(Fbm(u, v, reliefOff, _reliefFrequency, _reliefOctaves)), _heightExponent);
                        h = landDepth * _heightScale + relief * interiority * _maxHeight;
                    }
                    else
                    {
                        // Pente sous-marine douce, plafonnée au fond.
                        h = Mathf.Max(landDepth * _underwaterScale, -_underwaterDepth);
                    }

                    vertices[idx] = new Vector3((u - 0.5f) * size, h, (v - 0.5f) * size);
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

        /// <summary>
        /// "Profondeur de terre" : positif à l'intérieur, négatif en mer. Basé sur une distance
        /// radiale lissée, déformée par du bruit (côte organique) + baies. Transition douce.
        /// </summary>
        private float LandDepth(float u, float v, Vector2 coastOff, Vector2 bayOff)
        {
            float distC = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) / 0.5f; // 0 centre, ~1.41 coin

            float coastN = Fbm(u, v, coastOff, _coastFrequency, _coastOctaves) - 0.5f;          // -0.5..0.5
            float bayN = Mathf.PerlinNoise(bayOff.x + u * _bayFrequency, bayOff.y + v * _bayFrequency) - 0.5f;

            float warpedDist = distC + coastN * _coastWarp + bayN * _bayStrength;
            return _coastRadius - warpedDist;
        }

        private float Fbm(float u, float v, Vector2 off, float frequency, int octaves)
        {
            float amp = 1f, freq = 1f, sum = 0f, norm = 0f;
            for (int i = 0; i < Mathf.Max(1, octaves); i++)
            {
                sum += amp * Mathf.PerlinNoise(off.x + u * frequency * freq, off.y + v * frequency * freq);
                norm += amp;
                amp *= _persistence;
                freq *= _lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
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
            float sandWeight = 1f - SmoothStep(_beachHeight - 0.4f, _beachHeight + 0.4f, height);
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

        private static Vector2 RandOffset(System.Random rng)
            => new Vector2((float)rng.NextDouble() * 10000f, (float)rng.NextDouble() * 10000f);

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            if (Mathf.Approximately(edge0, edge1)) return x < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
