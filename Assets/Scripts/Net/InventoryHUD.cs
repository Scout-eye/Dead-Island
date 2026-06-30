using Game.Player;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace Game.Net
{
    /// <summary>
    /// HUD inventaire : 3 slots en bas de l'écran, avec un cadre BLANC autour du slot actif.
    /// Se branche tout seul sur l'inventaire du joueur local (PlayerInventory.OwnerReady/OwnerGone).
    /// Affiche le MODÈLE 3D de l'objet (aperçu rendu via ItemPreview). Aucune adhérence (events only).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryHUD : MonoBehaviour
    {
        private const float SlotSize = 64f, Gap = 12f;

        private GameObject _root;
        private Image[] _frames = new Image[PlayerInventory.SlotCount];
        private RawImage[] _previews = new RawImage[PlayerInventory.SlotCount];
        private PlayerInventory _inv;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<InventoryHUD>() != null) return;
            var go = new GameObject("InventoryHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<InventoryHUD>();
        }

        private void Start()
        {
            BuildUI();
            _root.SetActive(false);
            PlayerInventory.OwnerReady += Bind;
            PlayerInventory.OwnerGone += Unbind;
            foreach (var inv in FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None))
                if (inv.IsOwner) { Bind(inv); break; }
        }

        private void OnDestroy()
        {
            PlayerInventory.OwnerReady -= Bind;
            PlayerInventory.OwnerGone -= Unbind;
            Unbind();
        }

        private void Bind(PlayerInventory inv)
        {
            Unbind();
            _inv = inv;
            inv.SlotsChanged += RefreshSlots;
            inv.SelectionChanged += RefreshSelection;
            RefreshSlots();
            RefreshSelection(inv.Selected);
            _root.SetActive(true);
        }

        private void Unbind()
        {
            if (_inv != null)
            {
                _inv.SlotsChanged -= RefreshSlots;
                _inv.SelectionChanged -= RefreshSelection;
                _inv = null;
            }
            if (_root != null) _root.SetActive(false);
        }

        private void RefreshSlots()
        {
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                var item = _inv != null ? _inv.GetSlot(i) : null;
                var tex = ItemPreview.Get(item); // aperçu du modèle 3D (null si slot vide)
                _previews[i].texture = tex;
                _previews[i].enabled = tex != null;
            }
        }

        private void RefreshSelection(int selected)
        {
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
                _frames[i].enabled = (i == selected);
        }

        // --- UI ---
        private void BuildUI()
        {
            var canvasGo = new GameObject("InventoryCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            _root = new GameObject("Inventory", typeof(RectTransform));
            _root.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f); // bas-centre
            rt.anchoredPosition = new Vector2(0f, 24f);

            float total = PlayerInventory.SlotCount * SlotSize + (PlayerInventory.SlotCount - 1) * Gap;
            float startX = -total * 0.5f + SlotSize * 0.5f;

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                float x = startX + i * (SlotSize + Gap);
                BuildSlot(rt, i, x);
            }
        }

        private void BuildSlot(RectTransform parent, int index, float x)
        {
            // Cadre blanc (légèrement plus grand, derrière) — visible seulement sur le slot actif.
            var frame = NewImage(parent, "Frame" + index, x, SlotSize + 6f, Color.white);
            _frames[index] = frame;
            frame.enabled = false;

            // Fond sombre
            NewImage(parent, "BG" + index, x, SlotSize, new Color(0f, 0f, 0f, 0.6f));

            // Aperçu du MODÈLE 3D (RenderTexture via ItemPreview)
            var rawGo = new GameObject("Preview" + index, typeof(RectTransform));
            rawGo.transform.SetParent(parent, false);
            var raw = rawGo.AddComponent<RawImage>();
            var rrt = raw.rectTransform;
            rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(SlotSize - 8f, SlotSize - 8f);
            rrt.anchoredPosition = new Vector2(x, 0f);
            raw.enabled = false;
            _previews[index] = raw;
        }

        private static Image NewImage(RectTransform parent, string name, float x, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(x, 0f);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }
    }
}
