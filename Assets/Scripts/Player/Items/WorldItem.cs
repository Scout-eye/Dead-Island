using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Objet posé dans le monde, ramassable. Porte un <see cref="ItemDefinition"/>, affiche son modèle
    /// (le même <c>ViewPrefab</c> que dans la main) et se laisse surligner (contour blanc) puis ramasser.
    /// À ramasser via <see cref="PlayerInteractor"/> : à la frame de saisie, l'objet est <see cref="Consume"/>
    /// (retiré du monde) et ajouté à l'inventaire.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public sealed class WorldItem : MonoBehaviour
    {
        [SerializeField] private ItemDefinition _item;
        [Tooltip("Instancie le modèle de l'objet comme visuel au démarrage (sinon mets ton propre mesh).")]
        [SerializeField] private bool _spawnView = true;

        public ItemDefinition Item => _item;

        private Rigidbody _rb;
        private float _stillTime;

        private void Awake()
        {
            if (_spawnView && _item != null && _item.ViewPrefab != null)
            {
                var view = Instantiate(_item.ViewPrefab, transform);
                view.transform.localPosition = Vector3.zero;
                view.transform.localRotation = Quaternion.identity;
                foreach (var rb in view.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            }

            // Soumis à la gravité : tombe et se pose. Friction + rotation bloquée + amorti -> ne glisse pas.
            if (!TryGetComponent<Rigidbody>(out _rb))
            {
                _rb = gameObject.AddComponent<Rigidbody>();
                _rb.mass = 0.3f;
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
            _rb.linearDamping = 1.5f;

            var mat = new PhysicsMaterial("ItemGrip")
            {
                dynamicFriction = 0.8f, staticFriction = 0.9f,
                frictionCombine = PhysicsMaterialCombine.Maximum
            };
            foreach (var c in GetComponents<Collider>()) c.sharedMaterial = mat;

            if (GetComponent<Highlightable>() == null) gameObject.AddComponent<Highlightable>();
            if (GetComponent<WorldItemLabel>() == null) gameObject.AddComponent<WorldItemLabel>();
        }

        // Une fois posé et immobile, on fige l'objet (kinematic) : il reste pile en place et ciblable.
        private void FixedUpdate()
        {
            if (_rb == null || _rb.isKinematic) return;
            if (_rb.linearVelocity.sqrMagnitude < 0.0025f)
            {
                _stillTime += Time.fixedDeltaTime;
                if (_stillTime > 0.4f) _rb.isKinematic = true;
            }
            else _stillTime = 0f;
        }

        /// <summary>Accroche l'objet à la main (il la suit jusqu'à la fin de l'anim de ramassage).</summary>
        public void AttachToHand(Transform hand)
        {
            if (hand == null) return;
            if (_rb != null) _rb.isKinematic = true;
            foreach (var c in GetComponents<Collider>()) c.enabled = false; // ne bloque plus le raycast
            if (TryGetComponent<WorldItemLabel>(out var label)) label.enabled = false;
            if (TryGetComponent<Highlightable>(out var hl)) { hl.SetHighlighted(false); hl.enabled = false; }

            transform.SetParent(hand, false);
            transform.localPosition = PlayerHandItem.RightHandGrip;
            transform.localRotation = Quaternion.identity;
        }

        /// <summary>Retire l'objet du monde.</summary>
        public void Consume() => Destroy(gameObject);

        /// <summary>Crée un objet ramassable dans le monde (drop / dépose au runtime).</summary>
        public static WorldItem Spawn(ItemDefinition item, Vector3 position, Vector3 velocity = default)
        {
            if (item == null) return null;

            // Inactif d'abord : on règle _item AVANT que Awake tourne (sinon pas de visuel).
            var go = new GameObject($"Item ({item.DisplayName})");
            go.SetActive(false);
            go.transform.position = position;
            go.AddComponent<SphereCollider>().radius = 0.07f;
            var world = go.AddComponent<WorldItem>();
            world._item = item;
            go.SetActive(true);

            if (go.TryGetComponent<Rigidbody>(out var rb)) rb.linearVelocity = velocity;
            return world;
        }
    }
}
