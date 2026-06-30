using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Player
{
    /// <summary>
    /// Génère (et met en cache) un aperçu RenderTexture du MODÈLE d'un item, pour l'afficher dans le HUD.
    /// Le modèle est instancié loin de la scène (y = -5000), éclairé par une lumière locale et rendu par
    /// une caméra dédiée → fond transparent, aucune interférence avec le jeu.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemPreview : MonoBehaviour
    {
        private const int Resolution = 128;

        private static ItemPreview _instance;
        private Camera _cam;
        private Transform _stage;
        private readonly Dictionary<ItemDefinition, RenderTexture> _cache = new Dictionary<ItemDefinition, RenderTexture>();

        public static Texture Get(ItemDefinition item)
        {
            if (item == null || item.ViewPrefab == null) return null;
            return Ensure().Render(item);
        }

        private static ItemPreview Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("ItemPreview");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ItemPreview>();
            _instance.Build();
            return _instance;
        }

        private void Build()
        {
            _stage = new GameObject("Stage").transform;
            _stage.SetParent(transform, false);
            _stage.position = new Vector3(0f, -5000f, 0f); // loin de la scène

            var camGo = new GameObject("PreviewCam");
            camGo.transform.SetParent(_stage, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.enabled = false; // rendu manuel (Render())
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent
            _cam.orthographic = true;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 50f;

            // Lumière LOCALE (point light de portée limitée) : éclaire l'aperçu sans toucher la scène (trop loin).
            var lightGo = new GameObject("PreviewLight");
            lightGo.transform.SetParent(_stage, false);
            lightGo.transform.localPosition = new Vector3(1f, 2f, -2f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 12f;
            light.intensity = 3f;
        }

        private RenderTexture Render(ItemDefinition item)
        {
            if (_cache.TryGetValue(item, out var cached) && cached != null) return cached;

            var model = Instantiate(item.ViewPrefab, _stage);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, 150f, 0f); // vue 3/4
            foreach (var rb in model.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) c.enabled = false;

            Bounds b = ComputeBounds(model);
            float ext = Mathf.Max(b.size.x, b.size.y, b.size.z) * 0.5f;
            if (ext < 0.001f) ext = 0.1f;
            _cam.orthographicSize = ext * 1.35f;
            _cam.transform.position = b.center + new Vector3(0f, 0f, -10f);
            _cam.transform.LookAt(b.center);

            var rt = new RenderTexture(Resolution, Resolution, 16, RenderTextureFormat.ARGB32) { name = "Preview_" + item.name };

            // URP : Camera.Render() ne fonctionne pas -> on passe par une RenderRequest.
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(_cam, request))
                RenderPipeline.SubmitRenderRequest(_cam, request);
            else
            {
                _cam.targetTexture = rt;
                _cam.Render(); // fallback (Built-in RP)
                _cam.targetTexture = null;
            }

            DestroyImmediate(model); // synchrone : le prochain aperçu repart d'une scène vide
            _cache[item] = rt;
            return rt;
        }

        private static Bounds ComputeBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.2f);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}
