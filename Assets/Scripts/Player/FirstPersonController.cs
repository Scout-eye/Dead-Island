using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Controller première personne classique, basé sur un <see cref="CharacterController"/> :
    /// déplacement WASD relatif au regard, course, saut, gravité. Simple, fluide, robuste
    /// (façon Content Warning / Lethal Company).
    ///
    /// Responsabilité unique : déplacer le corps. La rotation (regard) est gérée par
    /// <see cref="PlayerCamera"/> ; l'animation de l'avatar par <see cref="PlayerAnimator"/>.
    /// Communique avec le réseau via un contrat isolé (position/yaw/vélocité), comme avant —
    /// si on supprime ce script, rien d'autre ne casse hormis le mouvement.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [Header("Owner / Remote")]
        [SerializeField] private bool _isOwner = true;

        [Header("Déplacement (m/s — cohérents avec les clips Mixamo)")]
        [Tooltip("Vitesse de marche : doit matcher le seuil 'walk' du blend tree (1.9) pour ne pas glisser.")]
        [SerializeField] private float _walkSpeed = 1.9f;
        [Tooltip("Vitesse de course : doit matcher le seuil 'run' du blend tree (4.3).")]
        [SerializeField] private float _runSpeed = 4.3f;
        [SerializeField] private float _crouchSpeed = 1.1f;
        [Tooltip("Lissage de la vitesse horizontale (plus haut = plus réactif).")]
        [SerializeField] private float _acceleration = 14f;
        [Tooltip("Fraction de contrôle en l'air.")]
        [SerializeField] private float _airControl = 0.5f;

        [Header("Accroupi")]
        [SerializeField] private float _standHeight = 1.8f;
        [SerializeField] private float _crouchHeight = 1.1f;
        [SerializeField] private float _crouchLerp = 8f;

        [Header("Saut / Gravité")]
        [SerializeField] private float _jumpHeight = 1.1f;
        [SerializeField] private float _gravity = -22f;
        [Tooltip("Tolérance de saut juste après avoir quitté le sol (s).")]
        [SerializeField] private float _coyoteTime = 0.12f;

        private CharacterController _cc;
        private PlayerInputReader _input;

        private Vector3 _horizontalVel;
        private float _verticalVel;
        private bool _grounded;
        private bool _crouching;
        private float _lastGroundedTime;
        private bool _jumpQueued;
        private Vector3 _netVelocity;

        // --- État exposé (caméra, animator, vitals, réseau) ---
        public bool IsOwner => _isOwner;
        public bool IsGrounded => _grounded;
        public bool IsSprinting { get; private set; }
        public bool IsCrouching => _crouching;
        /// <summary>0 (debout) → 1 (accroupi), pour abaisser la caméra en douceur.</summary>
        public float CrouchFactor { get; private set; }
        public float WalkSpeed => _walkSpeed;
        /// <summary>Orientation du corps = orientation du regard (FPS).</summary>
        public float BodyYaw => transform.eulerAngles.y;
        public Vector3 CurrentVelocity => _isOwner ? (_horizontalVel + Vector3.up * _verticalVel) : _netVelocity;

        // --- Contrat réseau (isolé, compatible NetworkManager / RemotePlayer) ---
        public Vector3 NetworkPosition => transform.position;
        public Vector3 NetworkVelocity => _horizontalVel + Vector3.up * _verticalVel;

        public void SetOwner(bool owner)
        {
            _isOwner = owner;
            if (_cc != null) _cc.enabled = owner;      // remote : déplacé par le réseau (kinematic-like)
            if (_input != null) _input.enabled = owner;
        }

        /// <summary>Remote : impose le transform interpolé (position + yaw).</summary>
        public void ApplyNetworkTransform(Vector3 position, float bodyYaw, Vector3 velocity)
        {
            _netVelocity = velocity;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, bodyYaw, 0f));
        }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputReader>();
            SetOwner(_isOwner);
        }

        private void Update()
        {
            if (!_isOwner) return;
            if (_input != null && _input.JumpPressedThisFrame) _jumpQueued = true;

            float dt = Time.deltaTime;
            _grounded = _cc.isGrounded;
            if (_grounded) _lastGroundedTime = Time.time;

            UpdateCrouch(dt);
            Move(dt);
        }

        /// <summary>Accroupi : abaisse la capsule et la caméra. On ne se relève pas sous un obstacle.</summary>
        private void UpdateCrouch(float dt)
        {
            bool wantCrouch = _input != null && _input.CrouchHeld;
            if (_crouching && !wantCrouch && BlockedAbove()) wantCrouch = true; // forcé accroupi sous un plafond
            _crouching = wantCrouch;

            float targetH = _crouching ? _crouchHeight : _standHeight;
            float h = Mathf.MoveTowards(_cc.height, targetH, _crouchLerp * dt);
            _cc.height = h;
            _cc.center = new Vector3(0f, h * 0.5f, 0f);
            CrouchFactor = Mathf.InverseLerp(_standHeight, _crouchHeight, h);
        }

        private bool BlockedAbove()
        {
            // Rayon vers le haut depuis le sommet de la capsule accroupie : y a-t-il la place de se lever ?
            Vector3 top = transform.position + Vector3.up * _cc.height;
            float need = _standHeight - _cc.height + 0.1f;
            return Physics.Raycast(top, Vector3.up, need, ~0, QueryTriggerInteraction.Ignore);
        }

        private void Move(float dt)
        {
            Vector2 input = _input != null ? _input.Move : Vector2.zero;
            // Déplacement relatif au REGARD (le corps fait face au regard en FPS).
            Vector3 wishDir = transform.right * input.x + transform.forward * input.y;
            wishDir = Vector3.ClampMagnitude(wishDir, 1f);

            bool wantSprint = _input != null && _input.SprintHeld && !_crouching;
            IsSprinting = wantSprint && input.y > 0.1f && wishDir.sqrMagnitude > 0.01f;
            float speed = _crouching ? _crouchSpeed : (IsSprinting ? _runSpeed : _walkSpeed);

            // Vitesse horizontale lissée (réactif au sol, plus mou en l'air).
            float accel = _acceleration * (_grounded ? 1f : _airControl);
            _horizontalVel = Vector3.MoveTowards(_horizontalVel, wishDir * speed, accel * speed * dt);

            // Vertical : gravité + saut (avec coyote time).
            if (_grounded && _verticalVel < 0f) _verticalVel = -2f; // colle au sol
            bool canJump = _grounded || (Time.time - _lastGroundedTime) <= _coyoteTime;
            if (_jumpQueued)
            {
                if (canJump) _verticalVel = Mathf.Sqrt(2f * _jumpHeight * -_gravity);
                _jumpQueued = false;
            }
            _verticalVel += _gravity * dt;

            _cc.Move((_horizontalVel + Vector3.up * _verticalVel) * dt);
        }
    }
}
