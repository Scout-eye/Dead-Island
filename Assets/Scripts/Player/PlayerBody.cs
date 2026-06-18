using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Locomotion physique du joueur. Aucun CharacterController : tout passe par un Rigidbody
    /// piloté en forces, avec détection de sol au Spherecast et gestion des pentes.
    ///
    /// Hiérarchie attendue sur le GameObject "Player" (root) :
    ///   Player (Rigidbody, CapsuleCollider, PlayerBody, PlayerCamera, PlayerHands, PlayerInputReader)
    ///   ├─ CameraRig (vide, à hauteur des yeux) -> Camera
    ///   └─ Model (Mixamo : Armature + bones). Le modèle SUIT le Rigidbody, jamais l'inverse.
    ///
    /// Séparation owner/remote : seul le owner simule la physique. Un remote sera mis en
    /// kinematic et piloté par interpolation (étape 2). Les données à synchroniser sont
    /// isolées dans NetworkPosition / NetworkRotation / NetworkVelocity.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    [DisallowMultipleComponent]
    public sealed class PlayerBody : MonoBehaviour
    {
        [Header("Owner / Remote")]
        [Tooltip("Vrai = ce joueur est simulé localement. Faux = piloté par le réseau (étape 2).")]
        [SerializeField] private bool _isOwner = true;

        [Header("Déplacement")]
        [SerializeField] private float _maxSpeed = 2.3f;       // vitesse de marche (réaliste)
        [SerializeField] private float _sprintMultiplier = 1.7f; // course ~3.9 m/s
        [SerializeField] private float _acceleration = 60f;
        [SerializeField] private float _airControl = 0.25f;   // fraction de l'accel utilisable en l'air

        [Header("Saut")]
        [SerializeField] private float _jumpHeight = 1.4f;
        [SerializeField] private float _coyoteTime = 0.12f;    // saut toléré peu après avoir quitté le sol
        [Tooltip("Durée (s) après un saut où la détection de sol est ignorée (pour vraiment décoller).")]
        [SerializeField] private float _jumpGrace = 0.2f;

        [Header("Sol & pentes")]
        [SerializeField] private LayerMask _groundMask = ~0;
        [SerializeField] private float _groundCheckDistance = 0.35f;
        [SerializeField] private float _groundSphereRadius = 0.4f;
        [SerializeField] private float _maxSlopeAngle = 50f;
        [SerializeField] private float _slopeStickForce = 40f;  // colle aux pentes en descente

        [Header("Drag")]
        [SerializeField] private float _groundDamping = 6f;
        [SerializeField] private float _airDamping = 0.2f;

        [Header("Découplage tête / corps")]
        [Tooltip("Angle max entre le regard et le corps avant que le corps ne suive (degrés).")]
        [SerializeField] private float _maxHeadYaw = 75f;
        [Tooltip("Vitesse à laquelle le corps se réaligne sur le regard (deg/s) quand on bouge ou qu'on dépasse l'angle.")]
        [SerializeField] private float _bodyTurnSpeed = 540f;
        [SerializeField] private float _moveThreshold = 0.1f;

        private Rigidbody _rb;
        private CapsuleCollider _capsule;
        private PlayerInputReader _input;
        private PlayerCamera _camera;

        private bool _grounded;
        private Vector3 _groundNormal = Vector3.up;
        private float _lastGroundedTime;
        private bool _jumpQueued;
        private float _jumpGraceTimer;
        private float _bodyYaw;   // orientation réelle du corps, distincte du regard (LookYaw caméra)
        private Vector3 _netVelocity; // vitesse fournie par le réseau (remote uniquement)

        // --- Données synchronisables (isolées pour l'étape 2) ---
        public Vector3 NetworkPosition => _rb.position;
        public Quaternion NetworkRotation => Quaternion.Euler(0f, _bodyYaw, 0f);
        public Vector3 NetworkVelocity => _rb.linearVelocity;

        // --- État exposé aux autres composants (caméra, mains, animator) ---
        public bool IsOwner => _isOwner;
        public bool IsGrounded => _grounded;
        public Vector3 GroundNormal => _groundNormal;
        public Rigidbody Rigidbody => _rb;
        /// <summary>Orientation du corps (degrés). Le regard caméra peut diverger jusqu'à _maxHeadYaw.</summary>
        public float BodyYaw => _bodyYaw;
        public bool IsSprinting { get; private set; }
        /// <summary>Vitesse de marche de référence (sans sprint). Utile à l'animator.</summary>
        public float WalkSpeed => _maxSpeed;
        /// <summary>Vitesse courante : physique réelle si owner, vitesse réseau si remote (anime jambes/bras).</summary>
        public Vector3 CurrentVelocity => _isOwner ? _rb.linearVelocity : _netVelocity;
        private float LookYaw => _camera != null ? _camera.LookYaw : _bodyYaw;

        /// <summary>
        /// Applique un état réseau sur un joueur distant (kinematic). Appelé par RemotePlayer après
        /// interpolation : position/rotation imposées, vitesse stockée pour piloter l'animation.
        /// </summary>
        public void ApplyNetworkTransform(Vector3 position, float bodyYaw, Vector3 velocity)
        {
            _bodyYaw = bodyYaw;
            _netVelocity = velocity;
            // Remote = kinematic : on impose directement le transform (déjà lissé par interpolation).
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, bodyYaw, 0f));
        }

        public void SetOwner(bool owner)
        {
            _isOwner = owner;
            ApplyOwnership();
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
            _input = GetComponent<PlayerInputReader>();
            _camera = GetComponent<PlayerCamera>();
            _bodyYaw = transform.eulerAngles.y;

            // Le corps ne doit jamais basculer sous l'effet de la physique : la rotation est
            // entièrement contrôlée (yaw) par la caméra. On laisse la translation libre.
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.useGravity = true;

            ApplyOwnership();
        }

        private void ApplyOwnership()
        {
            if (_rb == null) return;
            // Un remote n'est pas simulé : il sera déplacé par interpolation (kinematic).
            _rb.isKinematic = !_isOwner;
            if (_input != null) _input.enabled = _isOwner;
        }

        private void Update()
        {
            if (!_isOwner) return;
            // Le saut est détecté en Update (frame-rate input) puis consommé en FixedUpdate.
            if (_input != null && _input.JumpPressedThisFrame)
                _jumpQueued = true;
        }

        private void FixedUpdate()
        {
            if (!_isOwner) return;

            if (_jumpGraceTimer > 0f) _jumpGraceTimer -= Time.fixedDeltaTime;
            GroundCheck();

            Vector2 move = _input != null ? _input.Move : Vector2.zero;
            UpdateBodyYaw(move.sqrMagnitude > _moveThreshold * _moveThreshold);
            ApplyRotation();

            Move(move);

            if (_jumpQueued)
            {
                TryJump();
                _jumpQueued = false;
            }

            // Drag dynamique : freine vite au sol, glisse en l'air.
            _rb.linearDamping = _grounded ? _groundDamping : _airDamping;
        }

        private void GroundCheck()
        {
            // Pendant la grâce de saut, on force "en l'air" pour que le perso décolle vraiment
            // (sinon la re-détection immédiate + le collage aux pentes annulent le saut).
            if (_jumpGraceTimer > 0f)
            {
                _grounded = false;
                _groundNormal = Vector3.up;
                return;
            }

            Vector3 origin = _rb.position + Vector3.up * (_groundSphereRadius + 0.05f);
            bool hit = Physics.SphereCast(
                origin, _groundSphereRadius, Vector3.down,
                out RaycastHit info, _groundCheckDistance + 0.05f,
                _groundMask, QueryTriggerInteraction.Ignore);

            if (hit && Vector3.Angle(info.normal, Vector3.up) <= _maxSlopeAngle)
            {
                _grounded = true;
                _groundNormal = info.normal;
                _lastGroundedTime = Time.time;
            }
            else
            {
                _grounded = false;
                _groundNormal = Vector3.up;
            }
        }

        /// <summary>
        /// Découplage tête/corps : le regard (LookYaw) est libre. Le corps ne pivote que
        /// quand le joueur se déplace (il s'aligne alors sur le regard) ou quand l'écart
        /// regard/corps dépasse _maxHeadYaw (le corps est alors "tiré" pour rester dans la limite).
        /// </summary>
        private void UpdateBodyYaw(bool moving)
        {
            float lookYaw = LookYaw;
            float delta = Mathf.DeltaAngle(_bodyYaw, lookYaw);
            float step = _bodyTurnSpeed * Time.fixedDeltaTime;

            if (moving)
            {
                _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, lookYaw, step);
            }
            else if (Mathf.Abs(delta) > _maxHeadYaw)
            {
                // Cible = position où l'écart vaut exactement _maxHeadYaw (on ne dépasse jamais).
                float target = lookYaw - Mathf.Sign(delta) * _maxHeadYaw;
                _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, target, step);
            }
        }

        private void ApplyRotation()
        {
            _rb.MoveRotation(Quaternion.Euler(0f, _bodyYaw, 0f));
        }

        private void Move(Vector2 input)
        {
            // Le déplacement est relatif au REGARD (caméra), pas au corps : WASD va là où on regarde.
            Quaternion yawRot = Quaternion.Euler(0f, LookYaw, 0f);
            Vector3 wishDir = yawRot * new Vector3(input.x, 0f, input.y);
            wishDir = Vector3.ClampMagnitude(wishDir, 1f);

            // Sur une pente franchissable, on projette la direction sur le plan du sol
            // pour monter/descendre proprement sans coller au sol ni décoller.
            if (_grounded)
                wishDir = Vector3.ProjectOnPlane(wishDir, _groundNormal).normalized * wishDir.magnitude;

            // Sprint : seulement au sol et en avançant (pas en reculant).
            bool wantSprint = _input != null && _input.SprintHeld;
            IsSprinting = wantSprint && _grounded && input.y > 0.1f && wishDir.sqrMagnitude > 0.01f;
            float speed = _maxSpeed * (IsSprinting ? _sprintMultiplier : 1f);

            Vector3 currentHorizontal = Vector3.ProjectOnPlane(_rb.linearVelocity, _groundNormal);
            Vector3 targetVel = wishDir * speed;
            Vector3 velDiff = targetVel - currentHorizontal;

            float accel = _acceleration * (_grounded ? 1f : _airControl);
            Vector3 force = Vector3.ClampMagnitude(velDiff, speed) * accel;
            _rb.AddForce(force, ForceMode.Acceleration);

            // Colle aux pentes en descente pour éviter le "boost" de saut involontaire.
            if (_grounded && _groundNormal != Vector3.up && !_jumpQueued)
                _rb.AddForce(-_groundNormal * _slopeStickForce, ForceMode.Acceleration);
        }

        private void TryJump()
        {
            bool canJump = _grounded || (Time.time - _lastGroundedTime) <= _coyoteTime;
            if (!canJump) return;

            float jumpVelocity = Mathf.Sqrt(2f * _jumpHeight * Mathf.Abs(Physics.gravity.y));
            Vector3 v = _rb.linearVelocity;
            v.y = 0f;                       // reset vertical pour une hauteur de saut constante
            _rb.linearVelocity = v;
            _rb.AddForce(Vector3.up * jumpVelocity, ForceMode.VelocityChange);

            _grounded = false;
            _lastGroundedTime = -999f;      // empêche le double-saut via coyote
            _jumpGraceTimer = _jumpGrace;   // ignore le sol un court instant pour décoller
        }
    }
}
