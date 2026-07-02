using System.Collections;
using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Ramassage d'objets du monde (owner uniquement). Raycast depuis la caméra : si un
    /// <see cref="WorldItem"/> est visé, il est surligné (contour blanc). À l'appui sur la touche
    /// d'interaction, joue l'anim de ramassage ; au moment de la saisie (frame 30 via _grabDelay),
    /// l'objet disparaît du monde et entre dans l'inventaire (→ visible en main si sélectionné).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private float _range = 3.5f;
        [Tooltip("Délai jusqu'à la frame de SAISIE (l'objet s'accroche alors à la main).")]
        [SerializeField] private float _grabDelay = 1f;
        [Tooltip("Délai de la saisie jusqu'à la DERNIÈRE frame (l'objet pop alors à l'écran).")]
        [SerializeField] private float _settleDelay = 0.6f;
        [SerializeField] private LayerMask _mask = ~0;
        private static readonly RaycastHit[] _hits = new RaycastHit[8];

        private FirstPersonController _body;
        private PlayerInputReader _input;
        private PlayerInventory _inventory;
        private PlayerAnimator _animator;
        private Camera _camera;
        private Transform _hand;
        private WorldItem _target;
        private bool _busy;

        private void Awake()
        {
            _body = GetComponent<FirstPersonController>();
            _input = GetComponent<PlayerInputReader>();
            _inventory = GetComponent<PlayerInventory>();
            _animator = GetComponent<PlayerAnimator>();
            _hand = BoneUtils.Find(transform, "RightHand");
        }

        private void OnDisable() => SetTarget(null);

        private void Update()
        {
            // Owner vivant uniquement (le ragdoll désactive FirstPersonController).
            if (_body == null || !_body.IsOwner || !_body.enabled) { SetTarget(null); return; }
            if (_camera == null) _camera = GetComponentInChildren<Camera>();
            if (_busy || _camera == null) { SetTarget(null); return; }

            SetTarget(Raycast());

            if (_target != null && _input != null && _input.InteractPressedThisFrame)
                StartCoroutine(Pickup(_target));
        }

        // WorldItem le plus proche sur la ligne de visée. On ignore tout le reste (corps du joueur,
        // sol…) : ainsi regarder vers le bas et "voir son corps" ne bloque plus le ramassage.
        private WorldItem Raycast()
        {
            var ray = new Ray(_camera.transform.position, _camera.transform.forward);
            int n = Physics.RaycastNonAlloc(ray, _hits, _range, _mask, QueryTriggerInteraction.Collide);
            WorldItem best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var wi = _hits[i].collider.GetComponentInParent<WorldItem>();
                if (wi != null && _hits[i].distance < bestDist) { best = wi; bestDist = _hits[i].distance; }
            }
            return best;
        }

        private void SetTarget(WorldItem item)
        {
            if (item == _target) return;
            if (_target != null) { var h = _target.GetComponent<Highlightable>(); if (h != null) h.SetHighlighted(false); }
            _target = item;
            if (_target != null) { var h = _target.GetComponent<Highlightable>(); if (h != null) h.SetHighlighted(true); }
        }

        private IEnumerator Pickup(WorldItem item)
        {
            _busy = true;
            SetTarget(null);
            if (_animator != null) _animator.PlayPickup();

            // 1) Pendant le wind-up, l'objet reste posé.
            yield return new WaitForSeconds(_grabDelay);

            // 2) Frame de saisie : si encore à portée, l'objet s'accroche à la main et la suit.
            //    (Sinon on annule : le joueur est sorti de la zone → l'objet reste au sol.)
            bool inRange = item != null && _camera != null &&
                           Vector3.Distance(_camera.transform.position, item.transform.position) <= _range;
            if (!inRange) { _busy = false; yield break; }
            item.AttachToHand(_hand);

            // 3) Dernière frame : l'objet entre dans l'inventaire → pop en viewmodel à l'écran.
            yield return new WaitForSeconds(_settleDelay);
            if (item != null)
            {
                var def = item.Item;
                item.Consume();
                if (_inventory != null) _inventory.AddItem(def);
            }
            _busy = false;
        }

    }
}
