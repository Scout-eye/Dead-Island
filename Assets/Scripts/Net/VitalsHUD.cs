using Game.Player;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace Game.Net
{
    /// <summary>
    /// HUD de survie : barres vie / faim / soif en bas à gauche. Se branche automatiquement sur
    /// les vitals du joueur LOCAL (événements statiques PlayerVitals.OwnerReady/OwnerGone), et se
    /// masque hors jeu. Aucune adhérence : il ne connaît que l'interface événementielle des vitals.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VitalsHUD : MonoBehaviour
    {
        private static Font UIFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private GameObject _root;
        private RectTransform _healthFill, _hungerFill, _thirstFill;
        private PlayerVitals _vitals;

        // Auto-création dans N'IMPORTE QUELLE scène (jeu OU scène de test solo), sans dépendre du menu.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<VitalsHUD>() != null) return;
            var go = new GameObject("VitalsHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<VitalsHUD>();
        }

        private void Start()
        {
            BuildUI();
            _root.SetActive(false);
            PlayerVitals.OwnerReady += Bind;
            PlayerVitals.OwnerGone += Unbind;

            // Un joueur owner peut déjà exister (prefab posé dans une scène de test) : on s'y branche.
            foreach (var v in PlayerVitals.All)
                if (v != null && v.IsOwner) { Bind(v); break; }
        }

        private void OnDestroy()
        {
            PlayerVitals.OwnerReady -= Bind;
            PlayerVitals.OwnerGone -= Unbind;
            Unbind();
        }

        private void Bind(PlayerVitals v)
        {
            Unbind();
            _vitals = v;
            v.HealthChanged += OnHealth;
            v.HungerChanged += OnHunger;
            v.ThirstChanged += OnThirst;
            OnHealth(v.Health, v.MaxHealth);
            OnHunger(v.Hunger, v.MaxHunger);
            OnThirst(v.Thirst, v.MaxThirst);
            _root.SetActive(true);
        }

        private void Unbind()
        {
            if (_vitals != null)
            {
                _vitals.HealthChanged -= OnHealth;
                _vitals.HungerChanged -= OnHunger;
                _vitals.ThirstChanged -= OnThirst;
                _vitals = null;
            }
            if (_root != null) _root.SetActive(false);
        }

        private void OnHealth(float c, float m) => SetFill(_healthFill, c, m);
        private void OnHunger(float c, float m) => SetFill(_hungerFill, c, m);
        private void OnThirst(float c, float m) => SetFill(_thirstFill, c, m);

        // Remplit la barre en réglant la largeur (ancre droite) sur le pourcentage courant.
        private static void SetFill(RectTransform fill, float c, float m)
        {
            if (fill == null) return;
            float pct = m > 0f ? Mathf.Clamp01(c / m) : 0f;
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(pct, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }

        // --- UI ---

        private void BuildUI()
        {
            var canvasGo = new GameObject("VitalsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            _root = new GameObject("Vitals", typeof(RectTransform));
            _root.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f); // bas-gauche
            rt.anchoredPosition = new Vector2(20f, 20f);
            rt.sizeDelta = new Vector2(220f, 90f);

            _healthFill = MakeBar(rt, 60f, "Vie", new Color(0.85f, 0.2f, 0.2f));
            _hungerFill = MakeBar(rt, 32f, "Faim", new Color(0.85f, 0.55f, 0.15f));
            _thirstFill = MakeBar(rt, 4f, "Soif", new Color(0.2f, 0.55f, 0.9f));
        }

        private static RectTransform MakeBar(RectTransform parent, float y, string label, Color color)
        {
            // Fond
            var bg = new GameObject(label + "BG", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(parent, false);
            var bgRt = (RectTransform)bg.transform;
            bgRt.anchorMin = bgRt.anchorMax = bgRt.pivot = new Vector2(0f, 0f);
            bgRt.anchoredPosition = new Vector2(0f, y);
            bgRt.sizeDelta = new Vector2(200f, 22f);
            bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            // Zone interne (marge de 2 px tout autour)
            var area = new GameObject(label + "Area", typeof(RectTransform));
            area.transform.SetParent(bg.transform, false);
            var areaRt = (RectTransform)area.transform;
            areaRt.anchorMin = Vector2.zero; areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(2f, 2f); areaRt.offsetMax = new Vector2(-2f, -2f);

            // Barre de remplissage : largeur pilotée par anchorMax.x (= pourcentage)
            var fillGo = new GameObject(label + "Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(area.transform, false);
            var fRt = (RectTransform)fillGo.transform;
            fRt.pivot = new Vector2(0f, 0.5f);
            fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
            fRt.offsetMin = Vector2.zero; fRt.offsetMax = Vector2.zero;
            fillGo.GetComponent<Image>().color = color;

            // Libellé par-dessus
            var txtGo = new GameObject(label + "Txt", typeof(RectTransform));
            txtGo.transform.SetParent(bg.transform, false);
            var t = txtGo.AddComponent<Text>();
            t.font = UIFont; t.text = label; t.fontSize = 13; t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            var tRt = t.rectTransform;
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = new Vector2(8f, 0f); tRt.offsetMax = new Vector2(-8f, 0f);

            return fRt;
        }
    }
}
