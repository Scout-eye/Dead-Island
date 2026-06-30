using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player
{
    /// <summary>
    /// Étiquette flottante au-dessus d'un objet du monde : "[touche] Prendre {nom}". Fait face à la
    /// caméra (billboard) et apparaît en FONDU à l'approche du joueur. TextMesh 3D (aucune dépendance UI).
    /// Auto-ajoutée par WorldItem.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldItemLabel : MonoBehaviour
    {
        [Tooltip("Distance max d'affichage (m).")]
        [SerializeField] private float _range = 8f;
        [Tooltip("Demi-angle de vue où le label est plein (degrés).")]
        [SerializeField] private float _viewAngle = 16f;
        [Tooltip("Angle supplémentaire sur lequel il s'estompe (degrés).")]
        [SerializeField] private float _fadeAngle = 12f;
        [Tooltip("Hauteur du label au-dessus de l'objet (0 = sur l'objet).")]
        [SerializeField] private float _height = 0f;
        [SerializeField] private Color _color = Color.white;

        private WorldItem _item;
        private Transform _label;
        private TextMesh _text;
        private Camera _cam;
        private bool _textSet;

        private void Awake()
        {
            _item = GetComponent<WorldItem>();
            Build();
        }

        private void Build()
        {
            var go = new GameObject("Label");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * _height;
            _label = go.transform;

            _text = go.AddComponent<TextMesh>();
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.characterSize = 0.012f; // petit
            _text.fontSize = 140;
            _text.color = new Color(_color.r, _color.g, _color.b, 0f);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void LateUpdate()
        {
            var cam = Cam();
            if (cam == null || _text == null) return;

            if (!_textSet) { _text.text = BuildText(); _textSet = true; }

            _label.rotation = cam.transform.rotation; // billboard (face écran, lisible)

            // Visible quand on REGARDE vers l'objet (dans le cône de vue) et qu'il est à portée.
            Vector3 toItem = transform.position - cam.transform.position;
            float a = 0f;
            if (toItem.magnitude <= _range)
            {
                float angle = Vector3.Angle(cam.transform.forward, toItem);
                a = 1f - Mathf.InverseLerp(_viewAngle, _viewAngle + _fadeAngle, angle); // 1 centré → 0 hors champ
            }
            var c = _color; c.a = Mathf.Clamp01(a);
            _text.color = c;
        }

        private string BuildText()
        {
            string key = GameControls.Instance != null
                ? GameControls.Instance.Interact.GetBindingDisplayString(0, InputBinding.DisplayStringOptions.DontUseShortDisplayNames)
                : "E";
            string itemName = _item != null && _item.Item != null ? _item.Item.DisplayName : "";
            return $"[{key}] Prendre {itemName}";
        }

        private Camera Cam()
        {
            if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
            return _cam;
        }
    }
}
