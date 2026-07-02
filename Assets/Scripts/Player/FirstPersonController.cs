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
    /// Communique avec le réseau via un contrat isolé (position/yaw/vélocité).
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
        [Tooltip("Lissage de la vitesse horizontale (plus haut = plus réactif).")]
        [SerializeField] private float _acceleration = 14f;
        [Tooltip("Fraction de contrôle en l'air.")]
        [SerializeField] private float _airControl = 0.5f;

        [Header("Saut / Gravité")]
        [SerializeField] private float _jumpHeight = 1.1f;
        [SerializeField] private float _gravity = -22f;
        [Tooltip("Tolérance de saut juste après avoir quitté le sol (s).")]
        [SerializeField] private float _coyoteTime = 0.12f;

        [Header("Nage (surface, eau à y = niveau)")]
        [SerializeField] private float _waterLevel = 0f;
        [Tooltip("Profondeur d'eau à partir de laquelle le joueur n'a plus pied.")]
        [SerializeField] private float _swimStartDepth = 1.25f;
        [Tooltip("Profondeur des pieds quand le corps flotte en surface (plus petit = corps plus haut).")]
        [SerializeField] private float _swimBodyDepth = 0.35f;
        [SerializeField] private float _swimSpeed = 2.0f;
        [SerializeField] private float _swimSprintSpeed = 3.2f;

        [Header("Rotation du corps (tête découplée)")]
        [Tooltip("Angle max que le regard peut tourner avant que le corps suive (à l'arrêt).")]
        [SerializeField] private float _maxHeadYaw = 70f;
        [Tooltip("Vitesse d'alignement du corps sur le regard quand on se déplace (deg/s).")]
        [SerializeField] private float _bodyTurnSpeed = 360f;

        private CharacterController _cc;
        private PlayerInputReader _input;
        private PlayerCamera _camera;

        private Vector3 _horizontalVel;
        private float _verticalVel;
        private bool _grounded;
        private float _lastGroundedTime;
        private bool _jumpQueued;
        private bool _jumpedFlag;
        private float _bodyYaw;
        private Vector3 _netVelocity;

        // --- État exposé (caméra, animator, vitals, réseau) ---
        public bool IsOwner => _isOwner;
        /// <summary>Nage en surface (plus pied). Placeholder anim : traité comme "au sol".</summary>
        public bool IsSwimming { get; private set; }
        // Anti-rebond : l'isGrounded du CharacterController clignote ; on garde "au sol" un court
        // délai (lisse l'anim de chute). Les remotes utilisent l'état réseau tel quel.
        // En nage : "au sol" pour l'animator/le réseau (évite l'anim de chute en attendant le clip Swim).
        public bool IsGrounded => IsSwimming || (_isOwner ? (_grounded || (Time.time - _lastGroundedTime) <= 0.12f) : _grounded);
        public bool IsSprinting { get; private set; }
        public float WalkSpeed => _walkSpeed;
        /// <summary>Orientation du corps = orientation du regard (FPS).</summary>
        public float BodyYaw => transform.eulerAngles.y;
        public Vector3 CurrentVelocity => _isOwner ? (_horizontalVel + Vector3.up * _verticalVel) : _netVelocity;

        /// <summary>Vrai une fois par saut (consommé par l'animator pour déclencher le clip à l'instant T).</summary>
        public bool ConsumeJumped() { if (_jumpedFlag) { _jumpedFlag = false; return true; } return false; }

        // --- Contrat réseau (isolé, compatible NetworkManager / RemotePlayer) ---
        public Vector3 NetworkPosition => transform.position;
        public Vector3 NetworkVelocity => _horizontalVel + Vector3.up * _verticalVel;

        public void SetOwner(bool owner)
        {
            _isOwner = owner;
            if (_cc != null) _cc.enabled = owner;      // remote : déplacé par le réseau
            if (_input != null) _input.enabled = owner;
        }

        /// <summary>Remote : impose le transform interpolé (position + yaw) + les états (pour l'anim).</summary>
        public void ApplyNetworkTransform(Vector3 position, float bodyYaw, Vector3 velocity, bool grounded, bool swimming = false)
        {
            _netVelocity = velocity;
            _grounded = grounded;
            IsSwimming = swimming;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, bodyYaw, 0f));
        }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputReader>();
            _camera = GetComponent<PlayerCamera>();
            _grounded = true;                 // évite l'anim de chute au spawn
            _lastGroundedTime = Time.time;
            _bodyYaw = transform.eulerAngles.y;
            SetOwner(_isOwner);
        }

        private void Update()
        {
            if (!_isOwner) return;
            if (_input != null && _input.JumpPressedThisFrame) _jumpQueued = true;

            float dt = Time.deltaTime;
            _grounded = _cc.isGrounded;
            if (_grounded) _lastGroundedTime = Time.time;

            UpdateSwimState();
            if (IsSwimming) MoveSwim(dt);
            else Move(dt);
        }

        // Entrée : les pieds passent sous la profondeur "plus pied". Sortie : un fond praticable
        // remonte à portée de pied sous le joueur (raycast — l'eau n'a pas de collider).
        private void UpdateSwimState()
        {
            if (!IsSwimming)
            {
                if (transform.position.y < _waterLevel - _swimStartDepth)
                    IsSwimming = true;
            }
            else
            {
                if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit,
                                    _swimStartDepth + 0.3f, ~0, QueryTriggerInteraction.Ignore)
                    && hit.point.y > _waterLevel - _swimStartDepth + 0.05f)
                {
                    IsSwimming = false;
                    _verticalVel = 0f;
                }
            }
        }

        private void MoveSwim(float dt)
        {
            // Nage "à la caméra" : on n'avance QUE vers l'avant (pas de recul ni de strafe) ;
            // on se dirige en tournant le regard, le corps reste aligné sur la caméra.
            float forward = _input != null ? Mathf.Clamp01(_input.Move.y) : 0f;
            bool moving = forward > 0.01f;

            float lookYaw = _camera != null ? _camera.LookYaw : _bodyYaw;
            Vector3 wishDir = Quaternion.Euler(0f, lookYaw, 0f) * Vector3.forward * forward;

            TurnBody(true, lookYaw, dt); // toujours face au regard dans l'eau

            bool wantSprint = _input != null && _input.SprintHeld;
            IsSprinting = wantSprint && moving;
            float speed = IsSprinting ? _swimSprintSpeed : _swimSpeed;
            _horizontalVel = Vector3.MoveTowards(_horizontalVel, wishDir * speed, _acceleration * speed * 0.6f * dt);

            // Flottaison : ressort doux vers la profondeur de nage (amorti, borné pour les plongeons).
            float targetY = _waterLevel - _swimBodyDepth;
            _verticalVel = Mathf.Clamp((targetY - transform.position.y) * 4f, -2f, 3f);

            _jumpQueued = false; // pas de saut en nageant
            _cc.Move((_horizontalVel + Vector3.up * _verticalVel) * dt);
        }

        private void Move(float dt)
        {
            Vector2 input = _input != null ? _input.Move : Vector2.zero;
            bool moving = input.sqrMagnitude > 0.01f;

            // Déplacement relatif au REGARD (caméra), pas au corps : on va où on regarde.
            float lookYaw = _camera != null ? _camera.LookYaw : _bodyYaw;
            Vector3 wishDir = Quaternion.Euler(0f, lookYaw, 0f) * new Vector3(input.x, 0f, input.y);
            if (wishDir.sqrMagnitude > 1f) wishDir.Normalize(); // diagonale = même vitesse que tout droit

            TurnBody(moving, lookYaw, dt);

            bool wantSprint = _input != null && _input.SprintHeld;
            IsSprinting = wantSprint && input.y > 0.1f && moving;
            float speed = IsSprinting ? _runSpeed : _walkSpeed;

            // Vitesse horizontale lissée (réactif au sol, plus mou en l'air).
            float accel = _acceleration * (_grounded ? 1f : _airControl);
            _horizontalVel = Vector3.MoveTowards(_horizontalVel, wishDir * speed, accel * speed * dt);

            // Vertical : gravité + saut (avec coyote time).
            if (_grounded && _verticalVel < 0f) _verticalVel = -2f; // colle au sol
            bool canJump = _grounded || (Time.time - _lastGroundedTime) <= _coyoteTime;
            if (_jumpQueued)
            {
                if (canJump) { _verticalVel = Mathf.Sqrt(2f * _jumpHeight * -_gravity); _jumpedFlag = true; }
                _jumpQueued = false;
            }
            _verticalVel += _gravity * dt;

            _cc.Move((_horizontalVel + Vector3.up * _verticalVel) * dt);
        }

        // Tête découplée : le corps s'aligne au regard quand on bouge ; à l'arrêt il ne suit
        // que si le regard dépasse l'angle naturel de la tête.
        private void TurnBody(bool moving, float lookYaw, float dt)
        {
            float delta = Mathf.DeltaAngle(_bodyYaw, lookYaw);
            if (moving)
                _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, lookYaw, _bodyTurnSpeed * dt);
            else if (Mathf.Abs(delta) > _maxHeadYaw)
                _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, lookYaw, Mathf.Abs(delta) - _maxHeadYaw);
            transform.rotation = Quaternion.Euler(0f, _bodyYaw, 0f);
        }
    }
}
