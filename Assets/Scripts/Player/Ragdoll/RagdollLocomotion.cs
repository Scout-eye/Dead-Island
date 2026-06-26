using UnityEngine;

namespace Game.Player.Ragdoll
{
    /// <summary>
    /// Pilote l'AnimRig (le squelette animé "leader") façon PEAK. Ce composant NE touche PAS le corps
    /// physique : c'est <see cref="RagdollBalance"/> qui fait poursuivre au bassin physique la position
    /// du bassin animé. Ici on se contente de faire MARCHER le leader, posé au sol :
    ///   1. On oriente l'AnimRig vers la direction de déplacement (le corps se tourne — style PEAK).
    ///   2. On choisit l'allure (idle/marche/course) via le paramètre Speed de l'Animator.
    ///   3. On avance l'AnimRig à la vitesse de son ROOT MOTION, tenu en LAISSE près du corps physique
    ///      (il ne peut pas s'enfuir si le corps est bloqué) et collé au sol (raycast).
    ///
    /// Porte aussi le contrat réseau (compat NetworkManager / RemotePlayer).
    /// </summary>
    [DefaultExecutionOrder(-20)]
    [DisallowMultipleComponent]
    public sealed class RagdollLocomotion : MonoBehaviour
    {
        [Header("Owner / Remote")]
        [SerializeField] private bool _isOwner = true;

        [Header("Allure (paramètre Speed de l'Animator)")]
        [SerializeField] private float _walkParam = 1.6f;
        [SerializeField] private float _runParam = 4f;
        [Tooltip("Vitesse de rotation du corps vers la direction de déplacement (deg/s).")]
        [SerializeField] private float _turnSpeed = 540f;

        [Header("Laisse du leader")]
        [Tooltip("Distance horizontale max dont l'AnimRig peut devancer le corps physique (m).")]
        [SerializeField] private float _leash = 0.45f;

        [Header("Saut")]
        [SerializeField] private float _jumpHeight = 1.2f;
        [SerializeField] private float _jumpGrace = 0.2f;
        [SerializeField] private LayerMask _groundMask = ~0;

        private PlayerInputReader _input;
        private PlayerCamera _camera;
        private RagdollBalance _balance;
        private ActiveRagdoll _ragdoll;
        private AnimatedReference _anim;
        private Rigidbody _pelvis;

        private bool _jumpQueued;
        private float _faceYaw;
        private Vector3 _netVelocity;
        private readonly RaycastHit[] _groundHits = new RaycastHit[8];

        // --- État exposé ---
        public bool IsOwner => _isOwner;
        public bool IsGrounded => _balance != null && _balance.IsGrounded;
        public bool IsSprinting { get; private set; }
        public float WalkSpeed => _walkParam;
        public float BodyYaw => _faceYaw;
        public Vector3 CurrentVelocity => _isOwner ? (_pelvis != null ? _pelvis.linearVelocity : Vector3.zero) : _netVelocity;

        // --- Contrat réseau ---
        public Vector3 NetworkPosition => _pelvis != null ? _pelvis.position : transform.position;
        public Vector3 NetworkVelocity => _pelvis != null ? _pelvis.linearVelocity : Vector3.zero;

        private float LookYaw => _camera != null ? _camera.LookYaw : _faceYaw;

        public void SetOwner(bool owner)
        {
            _isOwner = owner;
            if (_input != null) _input.enabled = owner;
            if (_balance != null) _balance.SetOwner(owner);
            if (_ragdoll != null) _ragdoll.SetOwner(owner);
        }

        public void ApplyNetworkTransform(Vector3 position, float bodyYaw, Vector3 velocity)
        {
            _netVelocity = velocity;
            _faceYaw = bodyYaw;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, bodyYaw, 0f));
        }

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _camera = GetComponent<PlayerCamera>();
            _balance = GetComponent<RagdollBalance>();
            _ragdoll = GetComponent<ActiveRagdoll>();
            _anim = GetComponent<AnimatedReference>();
            _faceYaw = transform.eulerAngles.y;
        }

        private void Start()
        {
            _pelvis = _ragdoll != null ? _ragdoll.Pelvis : null;
            SetOwner(_isOwner);
        }

        private void Update()
        {
            if (!_isOwner) return;
            if (_input != null && _input.JumpPressedThisFrame) _jumpQueued = true;
        }

        private void FixedUpdate()
        {
            if (!_isOwner || _pelvis == null) return;

            Vector2 move = _input != null ? _input.Move : Vector2.zero;
            bool upright = _balance == null || _balance.IsUpright;
            bool moving = upright && move.sqrMagnitude > 0.01f;

            UpdateFacing(move, moving);
            DriveAnimator(move, moving);
            MoveAnimRig(moving);

            if (_jumpQueued)
            {
                if (upright) TryJump();
                _jumpQueued = false;
            }
        }

        /// <summary>Oriente le corps vers la direction de déplacement (relative au regard).</summary>
        private void UpdateFacing(Vector2 move, bool moving)
        {
            if (!moving) return;
            Vector3 wish = Quaternion.Euler(0f, LookYaw, 0f) * new Vector3(move.x, 0f, move.y);
            float target = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
            _faceYaw = Mathf.MoveTowardsAngle(_faceYaw, target, _turnSpeed * Time.fixedDeltaTime);
        }

        /// <summary>Choisit idle / marche / course (le root motion fournira la vitesse réelle).</summary>
        private void DriveAnimator(Vector2 move, bool moving)
        {
            bool wantSprint = _input != null && _input.SprintHeld;
            IsSprinting = moving && wantSprint && move.y > 0.1f;
            float speedParam = !moving ? 0f : (IsSprinting ? _runParam : _walkParam);
            if (_anim != null) { _anim.SetSpeed(speedParam); _anim.SetGrounded(IsGrounded); }
        }

        /// <summary>
        /// Avance l'AnimRig (leader) à la vitesse de son root motion, tenu en laisse près du corps
        /// physique et collé au sol. À l'arrêt, il se recolle au corps.
        /// </summary>
        private void MoveAnimRig(bool moving)
        {
            if (_anim == null || _anim.Root == null) return;

            Vector3 pos = _anim.Root.position;
            if (moving)
            {
                Vector3 rm = _anim.RootMotionVelocity; rm.y = 0f;
                Vector3 fwd = Quaternion.Euler(0f, _faceYaw, 0f) * Vector3.forward;
                pos += fwd * (rm.magnitude * Time.fixedDeltaTime);
            }

            // Laisse : l'AnimRig ne devance jamais le corps physique de plus de _leash (à l'arrêt, recollé).
            Vector3 phys = _pelvis.position;
            float maxOff = moving ? _leash : 0.05f;
            Vector2 off = new Vector2(pos.x - phys.x, pos.z - phys.z);
            if (off.magnitude > maxOff) off = off.normalized * maxOff;
            pos.x = phys.x + off.x;
            pos.z = phys.z + off.y;
            pos.y = GroundY(phys);

            _anim.Root.SetPositionAndRotation(pos, Quaternion.Euler(0f, _faceYaw, 0f));
        }

        private void TryJump()
        {
            if (!IsGrounded) return;
            float jumpVelocity = Mathf.Sqrt(2f * _jumpHeight * Mathf.Abs(Physics.gravity.y));
            Vector3 v = _pelvis.linearVelocity;
            v.y = jumpVelocity;
            _pelvis.linearVelocity = v;
            if (_balance != null) _balance.SuspendGround(_jumpGrace);
        }

        /// <summary>Hauteur du sol sous un point, en ignorant les propres colliders du joueur.</summary>
        private float GroundY(Vector3 around)
        {
            Vector3 origin = around + Vector3.up * 0.5f;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, 4f, _groundMask, QueryTriggerInteraction.Ignore);
            float bestY = around.y - 0.9f;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = _groundHits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < bestDist) { bestDist = h.distance; bestY = h.point.y; }
            }
            return bestY;
        }
    }
}
