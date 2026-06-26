using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player.Ragdoll
{
    /// <summary>
    /// Tend une main vers le point visé tant que le bouton est maintenu :
    ///   - clic GAUCHE  -> main gauche,
    ///   - clic DROIT   -> main droite.
    /// La cible est le point sous le viseur (raycast depuis la caméra), borné à l'allonge du bras.
    /// Quand on relâche, aucune force n'est appliquée : le drive des joints ramène le bras à sa pose
    /// de repos tout seul. Aucun système de grip/accroche (itération future).
    ///
    /// Pilote la main par FORCE sur son Rigidbody (le reste du bras suit physiquement), pas par
    /// rotation de joint — robuste quelle que soit la convention d'axes de l'épaule.
    /// Expose Left/RightHandTarget pour rester compatible avec le netcode existant.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HandReach : MonoBehaviour
    {
        [Header("Allonge & ressort")]
        [Tooltip("Distance max de la main au-delà de l'épaule (m).")]
        [SerializeField] private float _maxReach = 0.62f;
        [SerializeField] private float _reachSpring = 220f;
        [SerializeField] private float _reachDamper = 20f;
        [Tooltip("Calques testés par le raycast de visée.")]
        [SerializeField] private LayerMask _aimMask = ~0;

        private ActiveRagdoll _ragdoll;
        private RagdollLocomotion _loco;
        private Camera _camera;

        private Vector3 _leftTarget;
        private Vector3 _rightTarget;

        // --- Compat réseau ---
        public Vector3 LeftHandTarget => _leftTarget;
        public Vector3 RightHandTarget => _rightTarget;
        public void SetNetworkHands(Vector3 left, Vector3 right) { _leftTarget = left; _rightTarget = right; }

        private void Awake()
        {
            _ragdoll = GetComponent<ActiveRagdoll>();
            _loco = GetComponent<RagdollLocomotion>();
            _camera = GetComponentInChildren<Camera>(true);
        }

        private void FixedUpdate()
        {
            if (_ragdoll == null || !_ragdoll.IsBuilt || _loco == null || !_loco.IsOwner) return;

            var mouse = Mouse.current;
            bool left = mouse != null && mouse.leftButton.isPressed;
            bool right = mouse != null && mouse.rightButton.isPressed;

            Reach(_ragdoll.LeftHand, _ragdoll.GetTransform(RagdollPart.LeftUpperArm), left, ref _leftTarget);
            Reach(_ragdoll.RightHand, _ragdoll.GetTransform(RagdollPart.RightUpperArm), right, ref _rightTarget);
        }

        private void Reach(Rigidbody hand, Transform shoulder, bool active, ref Vector3 target)
        {
            if (hand == null) return;

            if (!active || _camera == null || shoulder == null)
            {
                target = hand.position; // au repos : pas de force, le bras retombe via le drive des joints
                return;
            }

            // Point visé : sous le viseur, borné à l'allonge depuis l'épaule.
            Ray ray = new Ray(_camera.transform.position, _camera.transform.forward);
            Vector3 aimDir = _camera.transform.forward;
            float reach = _maxReach;
            if (Physics.Raycast(ray, out RaycastHit hit, 50f, _aimMask, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform))
            {
                aimDir = (hit.point - shoulder.position);
                reach = Mathf.Min(_maxReach, aimDir.magnitude);
                aimDir = aimDir.sqrMagnitude > 1e-6f ? aimDir.normalized : _camera.transform.forward;
            }

            target = shoulder.position + aimDir * reach;

            Vector3 force = (target - hand.position) * _reachSpring - hand.linearVelocity * _reachDamper;
            hand.AddForce(force, ForceMode.Acceleration);
        }
    }
}
