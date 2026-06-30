using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

/// <summary>
/// Effet plein écran "Dreamy CRT" pour URP (Unity 6 / RenderGraph).
///
/// En URP on N'ATTACHE PAS un post-process à la caméra (OnRenderImage n'existe pas) : on ajoute
/// cette feature au URP Renderer asset (Add Renderer Feature ▸ Dreamy CRT Effect). Elle s'applique
/// alors au rendu de la caméra. Tous les paramètres sont exposés ici et réglables en live.
/// </summary>
[DisallowMultipleRendererFeature("Dreamy CRT Effect")]
public sealed class DreamyCRTEffect : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

        [Header("Couleurs")]
        [Range(0f, 1f)] public float warmth = 0.55f;
        [Range(0f, 2f)] public float saturation = 1.25f;
        [Range(0.5f, 1.5f)] public float brightness = 1.02f;

        [Header("Basse résolution (fondue)")]
        [Tooltip("Rayon du flou en pixels : plus haut = plus 'basse résolution' mais fondu.")]
        [Range(0f, 6f)] public float softness = 2.2f;

        [Header("Bloom")]
        [Range(0f, 1f)] public float bloomThreshold = 0.6f;
        [Range(0f, 2f)] public float bloomIntensity = 0.5f;
        [Range(1f, 16f)] public float bloomSize = 6f;

        [Header("Phosphore (color bleed RGB)")]
        [Range(0f, 1f)] public float colorBleed = 0.35f;

        [Header("Écran")]
        [Range(0f, 0.4f)] public float curvature = 0.08f;
        [Range(0f, 0.8f)] public float vignette = 0.25f;

        [Header("Scan lines (très douces)")]
        [Range(100f, 1080f)] public float scanlineCount = 600f;
        [Range(0f, 0.5f)] public float scanlineIntensity = 0.06f;
    }

    public Settings settings = new Settings();
    [SerializeField] private Shader shader;

    private Material _material;
    private CRTPass _pass;

    public override void Create()
    {
        if (shader == null) shader = Shader.Find("Custom/DreamyCRT");
        if (shader == null) return;

        _material = CoreUtils.CreateEngineMaterial(shader);
        _pass = new CRTPass(_material, settings)
        {
            renderPassEvent = settings.injectionPoint,
            // Force une texture couleur intermédiaire (sinon la source serait le back buffer = blit impossible).
            requiresIntermediateTexture = true
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_material == null) return;
        var camType = renderingData.cameraData.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection) return; // pas en aperçu

        _pass.renderPassEvent = settings.injectionPoint;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
    }

    // --- Passe RenderGraph : blit caméra -> texture via le matériau CRT ---
    private sealed class CRTPass : ScriptableRenderPass
    {
        private readonly Material _mat;
        private readonly Settings _s;

        private static readonly int Warmth = Shader.PropertyToID("_Warmth");
        private static readonly int Saturation = Shader.PropertyToID("_Saturation");
        private static readonly int Brightness = Shader.PropertyToID("_Brightness");
        private static readonly int Softness = Shader.PropertyToID("_Softness");
        private static readonly int BloomThreshold = Shader.PropertyToID("_BloomThreshold");
        private static readonly int BloomIntensity = Shader.PropertyToID("_BloomIntensity");
        private static readonly int BloomSize = Shader.PropertyToID("_BloomSize");
        private static readonly int ColorBleed = Shader.PropertyToID("_ColorBleed");
        private static readonly int Curvature = Shader.PropertyToID("_Curvature");
        private static readonly int Vignette = Shader.PropertyToID("_Vignette");
        private static readonly int ScanlineCount = Shader.PropertyToID("_ScanlineCount");
        private static readonly int ScanlineIntensity = Shader.PropertyToID("_ScanlineIntensity");

        public CRTPass(Material mat, Settings s) { _mat = mat; _s = s; }

        private void ApplySettings()
        {
            _mat.SetFloat(Warmth, _s.warmth);
            _mat.SetFloat(Saturation, _s.saturation);
            _mat.SetFloat(Brightness, _s.brightness);
            _mat.SetFloat(Softness, _s.softness);
            _mat.SetFloat(BloomThreshold, _s.bloomThreshold);
            _mat.SetFloat(BloomIntensity, _s.bloomIntensity);
            _mat.SetFloat(BloomSize, _s.bloomSize);
            _mat.SetFloat(ColorBleed, _s.colorBleed);
            _mat.SetFloat(Curvature, _s.curvature);
            _mat.SetFloat(Vignette, _s.vignette);
            _mat.SetFloat(ScanlineCount, _s.scanlineCount);
            _mat.SetFloat(ScanlineIntensity, _s.scanlineIntensity);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return; // sécurité : besoin d'une cible intermédiaire

            ApplySettings();

            TextureHandle source = resourceData.activeColorTexture;

            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "DreamyCRT";
            desc.clearBuffer = false;
            desc.depthBufferBits = 0;
            TextureHandle dest = renderGraph.CreateTexture(desc);

            var blit = new RenderGraphUtils.BlitMaterialParameters(source, dest, _mat, 0);
            renderGraph.AddBlitPass(blit, "DreamyCRT");

            // Le reste du pipeline lit désormais le résultat CRT.
            resourceData.cameraColor = dest;
        }
    }
}
