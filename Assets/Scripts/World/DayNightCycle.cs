using UnityEngine;
using UnityEngine.Rendering;

namespace Game.World
{
    /// <summary>
    /// Cycle jour/nuit : le SOLEIL (directional light) parcourt son arc pendant la journée, la LUNE
    /// pendant la nuit. Jour et nuit ont des durées différentes (16/24 et 8/24 du cycle par défaut),
    /// donc des vitesses angulaires différentes. Pilote aussi la lumière ambiante (dégradé
    /// aube/jour/nuit) et RenderSettings.sun (le skybox procédural Unity suit automatiquement :
    /// disque solaire, ciel qui s'assombrit).
    ///
    /// t normalisé : 0 = lever du soleil, _dayFraction = coucher, 1 = lever suivant.
    /// <see cref="SetNormalizedTime"/> permet une future synchro réseau (l'hôte fait autorité).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DayNightCycle : MonoBehaviour
    {
        public static DayNightCycle Instance { get; private set; }

        [Header("Durées")]
        [Tooltip("Durée d'un cycle complet matin→matin (s). 600 = 10 min.")]
        [SerializeField] private float _cycleDuration = 600f;
        [Tooltip("Part du cycle passée de JOUR. 16/24 ≈ 0.667.")]
        [Range(0.1f, 0.9f)] [SerializeField] private float _dayFraction = 16f / 24f;
        [Tooltip("Heure de départ normalisée (0 = lever du soleil).")]
        [Range(0f, 1f)] [SerializeField] private float _startTime = 0.02f;

        [Header("Soleil")]
        [Tooltip("Vide = prend la Directional Light la plus intense de la scène (ou en crée une).")]
        [SerializeField] private Light _sun;
        [SerializeField] private float _sunMaxIntensity = 1.3f;
        [SerializeField] private Color _sunHorizon = new Color(1f, 0.48f, 0.18f);
        [SerializeField] private Color _sunZenith = new Color(1f, 0.96f, 0.88f);
        [Tooltip("Orientation de la course du soleil (deg).")]
        [SerializeField] private float _sunAzimuth = -30f;

        [Header("Lune")]
        [SerializeField] private float _moonIntensity = 0.25f;
        [SerializeField] private Color _moonColor = new Color(0.58f, 0.66f, 0.86f);

        [Header("Ambiance (lumière ambiante)")]
        [SerializeField] private Color _ambientDay = new Color(0.52f, 0.56f, 0.62f);
        [SerializeField] private Color _ambientHorizon = new Color(0.42f, 0.34f, 0.34f);
        [SerializeField] private Color _ambientNight = new Color(0.09f, 0.11f, 0.19f);

        private Light _moon;
        private float _time01;

        // Sauvegarde des RenderSettings pour les restaurer quand le monde est détruit
        // (retour salle d'attente : on ne veut pas laisser le menu dans le noir).
        private AmbientMode _prevAmbientMode;
        private Color _prevAmbientColor;
        private Light _prevSun;

        /// <summary>Temps normalisé du cycle (0 = aube, _dayFraction = crépuscule).</summary>
        public float NormalizedTime => _time01;
        public bool IsDay => _time01 < _dayFraction;
        /// <summary>Heure "en jeu" (0..24) : jour = 6h→22h, nuit = 22h→6h.</summary>
        public float Hour => IsDay
            ? Mathf.Lerp(6f, 22f, _time01 / _dayFraction)
            : Mathf.Repeat(Mathf.Lerp(22f, 30f, (_time01 - _dayFraction) / (1f - _dayFraction)), 24f);

        /// <summary>Impose l'heure (0..1). Pour une future synchro réseau (hôte autoritaire).</summary>
        public void SetNormalizedTime(float t)
        {
            _time01 = Mathf.Repeat(t, 1f);
            Apply();
        }

        public static DayNightCycle Create(Transform parent)
        {
            if (Instance != null) return Instance; // un seul cycle à la fois
            var go = new GameObject("DayNight");
            go.transform.SetParent(parent, false);
            return go.AddComponent<DayNightCycle>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            EnsureSun();
            EnsureMoon();

            _prevAmbientMode = RenderSettings.ambientMode;
            _prevAmbientColor = RenderSettings.ambientLight;
            _prevSun = RenderSettings.sun;
            RenderSettings.ambientMode = AmbientMode.Flat; // on pilote l'ambiante nous-mêmes
            RenderSettings.sun = _sun;                     // le skybox procédural suit le soleil

            _time01 = _startTime;
            Apply();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            RenderSettings.ambientMode = _prevAmbientMode;
            RenderSettings.ambientLight = _prevAmbientColor;
            RenderSettings.sun = _prevSun;
            if (_sun != null) { _sun.enabled = true; } // ne pas laisser la scène sans lumière
        }

        private void Update()
        {
            _time01 = Mathf.Repeat(_time01 + Time.deltaTime / Mathf.Max(1f, _cycleDuration), 1f);
            Apply();
        }

        private void Apply()
        {
            if (_sun == null) return;

            if (IsDay)
            {
                // Le soleil parcourt 180° sur la durée du jour (plus lentement que la lune la nuit).
                float d = _time01 / _dayFraction;
                float elev = d * 180f;
                float height = Mathf.Clamp01(Mathf.Sin(elev * Mathf.Deg2Rad)); // 0 horizon, 1 zénith

                _sun.transform.rotation = Quaternion.Euler(elev, _sunAzimuth, 0f);
                _sun.intensity = _sunMaxIntensity * Mathf.Pow(height, 0.6f);
                _sun.color = Color.Lerp(_sunHorizon, _sunZenith, height);
                _sun.enabled = _sun.intensity > 0.01f;
                if (_moon != null) _moon.enabled = false;

                RenderSettings.ambientLight = Color.Lerp(_ambientHorizon, _ambientDay, height);
            }
            else
            {
                // La lune parcourt son arc pendant la nuit, opposée au soleil.
                float n = (_time01 - _dayFraction) / (1f - _dayFraction);
                float elev = n * 180f;
                float height = Mathf.Clamp01(Mathf.Sin(elev * Mathf.Deg2Rad));

                _sun.enabled = false;
                if (_moon != null)
                {
                    _moon.transform.rotation = Quaternion.Euler(elev, _sunAzimuth + 180f, 0f);
                    _moon.intensity = _moonIntensity * Mathf.Pow(height, 0.5f);
                    _moon.enabled = _moon.intensity > 0.01f;
                }

                // Fondu aube/crépuscule aux bords de la nuit, noir bleuté au cœur.
                RenderSettings.ambientLight = Color.Lerp(_ambientHorizon, _ambientNight, Mathf.Pow(height, 0.5f));
            }
        }

        // --- Setup lumières ---

        private void EnsureSun()
        {
            if (_sun != null) return;

            // La directional la plus intense de la scène (celle posée dans SampleScene, par ex.).
            float best = -1f;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional || l == _moon) continue;
                if (l.intensity > best) { best = l.intensity; _sun = l; }
            }
            if (_sun != null) return;

            var go = new GameObject("Sun");
            go.transform.SetParent(transform, false);
            _sun = go.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;
        }

        private void EnsureMoon()
        {
            if (_moon != null) return;
            var go = new GameObject("Moon");
            go.transform.SetParent(transform, false);
            _moon = go.AddComponent<Light>();
            _moon.type = LightType.Directional;
            _moon.shadows = LightShadows.Soft;
            _moon.color = _moonColor;
            _moon.enabled = false;
        }
    }
}
