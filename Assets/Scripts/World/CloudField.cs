using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Champ de nuages stylisés : chaque nuage = quelques sphères aplaties fusionnées en UN mesh
    /// (façon "low poly cloud generator"), qui dérive avec le vent et reboucle sur la zone.
    /// Déterministe via seed (même seed → mêmes nuages chez tous les joueurs). Très bon marché :
    /// un draw call par nuage, pas d'ombre, matériau partagé (Lit → teinté par le soleil/la lune,
    /// donc orange au couchant et sombre la nuit gratuitement).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CloudField : MonoBehaviour
    {
        [Header("Zone")]
        [SerializeField] private int _cloudCount = 14;
        [Tooltip("Rayon de la zone couverte (m). En partie : ~1.4× la taille de l'île.")]
        [SerializeField] private float _areaRadius = 240f;
        [SerializeField] private float _minAltitude = 60f;
        [SerializeField] private float _maxAltitude = 95f;

        [Header("Forme des nuages")]
        [Tooltip("Longueur d'un nuage (m), tirée entre x et y.")]
        [SerializeField] private Vector2 _cloudLength = new Vector2(16f, 42f);
        [Tooltip("Aplatissement vertical des blobs (1 = sphère).")]
        [Range(0.15f, 0.6f)] [SerializeField] private float _flatten = 0.32f;
        [SerializeField] private Vector2Int _blobsPerCloud = new Vector2Int(3, 7);
        [SerializeField] private Color _tint = new Color(0.97f, 0.97f, 1f);

        [Header("Vent")]
        [SerializeField] private float _windSpeed = 3f;

        [Header("Test (hors réseau)")]
        [SerializeField] private bool _generateOnStart = true;
        [SerializeField] private int _testSeed = 1337;

        private static Material _cloudMaterial; // partagé (pas de fuite à la régénération)
        private Transform[] _clouds;
        private Vector3 _wind;

        private void Start()
        {
            if (_generateOnStart && _clouds == null) Generate(_testSeed, _areaRadius);
        }

        public static CloudField Create(Transform parent, int seed, float areaRadius)
        {
            // Un seul champ de nuages à la fois (la scène de test en a déjà un).
            var existing = FindFirstObjectByType<CloudField>();
            if (existing != null) return existing;

            var go = new GameObject("Clouds");
            go.transform.SetParent(parent, false);
            var field = go.AddComponent<CloudField>();
            field._generateOnStart = false;
            field.Generate(seed, areaRadius);
            return field;
        }

        public void Generate(int seed, float areaRadius)
        {
            Clear();
            _areaRadius = areaRadius;
            var rng = new System.Random(seed);

            float windAngle = (float)rng.NextDouble() * 360f;
            _wind = Quaternion.Euler(0f, windAngle, 0f) * Vector3.forward * _windSpeed;

            _clouds = new Transform[_cloudCount];
            for (int i = 0; i < _cloudCount; i++)
            {
                var cloud = BuildCloud(rng, $"Cloud{i}");
                double ang = rng.NextDouble() * 2.0 * System.Math.PI;
                float dist = Mathf.Sqrt((float)rng.NextDouble()) * _areaRadius; // uniforme dans le disque
                float alt = Mathf.Lerp(_minAltitude, _maxAltitude, (float)rng.NextDouble());
                cloud.localPosition = new Vector3(
                    (float)System.Math.Cos(ang) * dist, alt, (float)System.Math.Sin(ang) * dist);
                cloud.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                _clouds[i] = cloud;
            }
        }

        private void Update()
        {
            if (_clouds == null) return;
            Vector3 delta = _wind * Time.deltaTime;
            for (int i = 0; i < _clouds.Length; i++)
            {
                var c = _clouds[i];
                if (c == null) continue;
                c.localPosition += delta;

                // Sorti de la zone → réapparaît côté au-vent (boucle discrète, loin de la caméra).
                Vector3 flat = c.localPosition; flat.y = 0f;
                if (flat.magnitude > _areaRadius)
                    c.localPosition -= flat.normalized * (_areaRadius * 1.95f);
            }
        }

        /// <summary>Un nuage = chapelet de sphères aplaties (grosses au centre) fusionné en un mesh.</summary>
        private Transform BuildCloud(System.Random rng, string name)
        {
            Mesh blob = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            int blobs = rng.Next(_blobsPerCloud.x, _blobsPerCloud.y + 1);
            float length = Mathf.Lerp(_cloudLength.x, _cloudLength.y, (float)rng.NextDouble());

            var combine = new CombineInstance[blobs];
            for (int b = 0; b < blobs; b++)
            {
                float t = blobs > 1 ? (float)b / (blobs - 1) : 0.5f;
                // Plus gros au centre du chapelet, plus petit aux extrémités.
                float mid = 1f - Mathf.Abs(t - 0.5f) * 2f;
                float diameter = length * Mathf.Lerp(0.28f, 0.52f, mid) * Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble());

                Vector3 pos = new Vector3(
                    (t - 0.5f) * length * 0.8f,
                    ((float)rng.NextDouble() - 0.5f) * diameter * 0.2f,
                    ((float)rng.NextDouble() - 0.5f) * length * 0.22f);

                combine[b].mesh = blob;
                combine[b].transform = Matrix4x4.TRS(
                    pos, Quaternion.identity,
                    new Vector3(diameter, diameter * _flatten, diameter * 0.8f));
            }

            var mesh = new Mesh { name = name };
            mesh.CombineMeshes(combine, true, true);

            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<GeneratedMeshCleanup>();

            var mr = go.AddComponent<MeshRenderer>();
            if (_cloudMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                {
                    _cloudMaterial = new Material(shader);
                    _cloudMaterial.SetFloat("_Smoothness", 0f);
                }
            }
            if (_cloudMaterial != null)
            {
                _cloudMaterial.SetColor("_BaseColor", _tint);
                mr.sharedMaterial = _cloudMaterial;
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        private void Clear()
        {
            if (_clouds == null) return;
            foreach (var c in _clouds)
            {
                if (c == null) continue;
                if (Application.isPlaying) Destroy(c.gameObject);
                else DestroyImmediate(c.gameObject);
            }
            _clouds = null;
        }
    }
}
