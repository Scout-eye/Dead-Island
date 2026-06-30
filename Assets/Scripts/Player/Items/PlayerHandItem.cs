using System.Collections;
using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Affiche le modèle de l'objet tenu :
    ///  - Joueur LOCAL (owner) : "viewmodel" accroché à la caméra (bas-droite), piloté par l'inventaire.
    ///  - Joueurs DISTANTS : sur l'os de la main droite, piloté par le RÉSEAU (<see cref="SetNetworkItem"/>).
    /// Au changement d'objet, la main descend puis remonte (montre le changement).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHandItem : MonoBehaviour
    {
        [Header("Viewmodel (joueur local) — accroché à la caméra")]
        [Tooltip("Position relative à la caméra (X droite, Y bas, Z avant). À régler à l'œil en Play.")]
        [SerializeField] private Vector3 _viewPosition = new Vector3(0.174f, -0.093f, 0.231f);
        [SerializeField] private Vector3 _viewEuler = Vector3.zero;
        [SerializeField] private float _viewScale = 1f;

        [Header("Main (joueurs distants) — sur l'os")]
        [SerializeField] private string _handBoneName = "RightHand";
        [SerializeField] private Vector3 _handPosition = new Vector3(0.0213f, 0.1115f, 0.0639f);
        [SerializeField] private Vector3 _handEuler = Vector3.zero;
        [SerializeField] private float _handScale = 1f;

        [Header("Changement d'objet (descente/montée)")]
        [SerializeField] private float _dipDistance = 0.4f;
        [SerializeField] private float _dipDownTime = 0.09f;
        [SerializeField] private float _dipUpTime = 0.13f;

        private PlayerInventory _inventory;
        private FirstPersonController _body;
        private Transform _anchor;
        private Vector3 _pos, _euler;
        private float _scale;

        private GameObject _current;
        private ItemDefinition _shown;
        private ItemDefinition _networkItem; // objet tenu reçu du réseau (remote)
        private Coroutine _co;
        private bool _ready;

        private bool Owner => _body == null || _body.IsOwner;

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            _body = GetComponent<FirstPersonController>();
        }

        private void OnEnable()
        {
            if (_inventory == null) return;
            _inventory.SelectionChanged += OnSelection;
            _inventory.SlotsChanged += OnSlots;
        }

        private void OnDisable()
        {
            if (_inventory == null) return;
            _inventory.SelectionChanged -= OnSelection;
            _inventory.SlotsChanged -= OnSlots;
        }

        private void Start()
        {
            ResolveAnchor();
            _ready = true;
            Show(Owner ? (_inventory != null ? _inventory.SelectedItem : null) : _networkItem, animate: false);
        }

        /// <summary>Pour les joueurs DISTANTS : objet tenu reçu du réseau.</summary>
        public void SetNetworkItem(ItemDefinition item)
        {
            _networkItem = item;
            if (_ready && !Owner) Show(item, animate: true);
        }

        private void ResolveAnchor()
        {
            if (Owner)
            {
                var cam = GetComponentInChildren<Camera>(true);
                _anchor = cam != null ? cam.transform : transform;
                _pos = _viewPosition; _euler = _viewEuler; _scale = _viewScale;
            }
            else
            {
                _anchor = FindBone(_handBoneName);
                _pos = _handPosition; _euler = _handEuler; _scale = _handScale;
            }
        }

        private void OnSelection(int _) { if (Owner) Show(_inventory.SelectedItem, true); }
        private void OnSlots() { if (Owner) Show(_inventory.SelectedItem, true); }

        private void Show(ItemDefinition item, bool animate)
        {
            if (!_ready || item == _shown) return;
            _shown = item;
            if (_co != null) StopCoroutine(_co);
            if (animate && isActiveAndEnabled) _co = StartCoroutine(DipSwap(item));
            else Spawn(item);
        }

        private IEnumerator DipSwap(ItemDefinition item)
        {
            yield return Move(0f, 1f, _dipDownTime);
            Spawn(item);
            yield return Move(1f, 0f, _dipUpTime);
            _co = null;
        }

        private IEnumerator Move(float from, float to, float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                SetDip(Mathf.Lerp(from, to, Mathf.Clamp01(t / dur)));
                yield return null;
            }
            SetDip(to);
        }

        private void SetDip(float down)
        {
            if (_current != null) _current.transform.localPosition = _pos + Vector3.down * (_dipDistance * down);
        }

        private void Spawn(ItemDefinition item)
        {
            if (_current != null) { Destroy(_current); _current = null; }
            if (item == null || item.ViewPrefab == null || _anchor == null) return;

            _current = Instantiate(item.ViewPrefab, _anchor);
            _current.transform.localPosition = _pos;
            _current.transform.localRotation = Quaternion.Euler(_euler);
            _current.transform.localScale *= _scale;

            foreach (var c in _current.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var rb in _current.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        }

        private Transform FindBone(string bone)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == bone || n == "mixamorig:" + bone || n.EndsWith(":" + bone)) return t;
            }
            return null;
        }
    }
}
