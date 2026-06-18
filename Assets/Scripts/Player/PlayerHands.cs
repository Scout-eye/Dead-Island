using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Mains procédurales : IK deux bras + agrippage physique (escalade type Peak).
    ///
    ///   - Chaque main lance un raycast vers la surface regardée et peut s'y agripper.
    ///   - Clic gauche / droit = grip de la main correspondante. Le grip crée un ConfigurableJoint
    ///     RIGIDE entre le Rigidbody du corps et le point de contact : le corps reste accroché et
    ///     peut osciller (angular libre) autour des prises, comme une suspension.
    ///   - Stamina : se vide tant qu'on tient une prise (x2 si les deux), se recharge mains libres.
    ///     À zéro, les deux prises lâchent automatiquement.
    ///
    /// L'IK est résolue en LateUpdate pour owner ET remote (le remote rejoue les targets reçus).
    /// Le calcul des prises/raycast/joints ne tourne que pour le owner.
    ///
    /// Données synchronisables isolées : LeftHandTarget, RightHandTarget, LeftGripping,
    /// RightGripping, Stamina.
    /// </summary>
    // rev: balancement bras autour de l'épaule + repositionnement pieds en virage
    [DisallowMultipleComponent]
    public sealed class PlayerHands : MonoBehaviour
    {
        [Header("Bones bras gauche (Mixamo)")]
        [SerializeField] private Transform _leftUpperArm;
        [SerializeField] private Transform _leftForeArm;
        [SerializeField] private Transform _leftHand;

        [Header("Bones bras droit (Mixamo)")]
        [SerializeField] private Transform _rightUpperArm;
        [SerializeField] private Transform _rightForeArm;
        [SerializeField] private Transform _rightHand;

        [Header("Agrippage")]
        [SerializeField] private LayerMask _gripMask = ~0;
        [SerializeField] private float _reach = 1.9f;
        [Tooltip("Raideur du ressort du joint de prise (N/m). Élevée = grip rigide.")]
        [SerializeField] private float _gripStiffness = 8000f;
        [SerializeField] private float _gripDamping = 200f;
        [Tooltip("Si vrai, le joint verrouille la translation (parfaitement rigide). Sinon ressort.")]
        [SerializeField] private bool _lockGrip = true;

        [Header("Stamina")]
        [SerializeField] private float _staminaDrainPerGrip = 0.18f;  // par prise et par seconde
        [SerializeField] private float _staminaRecharge = 0.35f;

        [Header("Pose au repos (local au corps) — mains à hauteur de hanche, bras légèrement plié")]
        [SerializeField] private Vector3 _leftRestOffset = new Vector3(-0.2f, 1.0f, 0.18f);
        [SerializeField] private Vector3 _rightRestOffset = new Vector3(0.2f, 1.0f, 0.18f);
        [SerializeField] private float _targetSmoothing = 14f;

        [Header("Balancement des bras (marche/course)")]
        [Tooltip("Amplitude de balancement en DEGRÉS (rotation du bras entier autour de l'épaule).")]
        [SerializeField] private float _armSwingAngle = 32f;
        [Tooltip("Vitesse (m/s) de marche de référence pour l'intensité du balancement.")]
        [SerializeField] private float _swingRefSpeed = 2.3f;
        [SerializeField] private float _stepTime = 0.35f;            // doit matcher l'animator

        private PlayerBody _body;
        private PlayerCamera _camera;
        private PlayerInputReader _input;

        // Cadence calculée localement depuis la vitesse (autonome, ne dépend de rien d'externe).
        private float _gaitPhase;
        private float _gaitWeight;

        private GripState _left;
        private GripState _right;
        private float _stamina = 1f;

        // --- Données synchronisables ---
        public Vector3 LeftHandTarget => _left.Target;
        public Vector3 RightHandTarget => _right.Target;
        public bool LeftGripping => _left.Active;
        public bool RightGripping => _right.Active;
        public float Stamina => _stamina;

        /// <summary>Pour le remote : injecter les targets/états reçus du réseau (pas de physique).</summary>
        public void SetNetworkHands(Vector3 leftTarget, Vector3 rightTarget,
                                    bool leftGrip, bool rightGrip, float stamina)
        {
            _left.Target = leftTarget;
            _right.Target = rightTarget;
            _left.Active = leftGrip;
            _right.Active = rightGrip;
            _stamina = stamina;
        }

        private void Awake()
        {
            _body = GetComponent<PlayerBody>();
            _camera = GetComponent<PlayerCamera>();
            _input = GetComponent<PlayerInputReader>();

            // Initialise les targets sur la pose de repos pour éviter un saut d'IK au premier frame.
            _left.Target = transform.TransformPoint(_leftRestOffset);
            _right.Target = transform.TransformPoint(_rightRestOffset);
        }

        private void FixedUpdate()
        {
            if (_body == null || !_body.IsOwner) return;

            bool wantLeft = _input != null && _input.LeftGripHeld;
            bool wantRight = _input != null && _input.RightGripHeld;

            UpdateGait();

            // Bras opposés (déphasage PI) pour un balancement naturel marche/course.
            ResolveHand(ref _left, wantLeft, _leftUpperArm, _leftRestOffset, 0f);
            ResolveHand(ref _right, wantRight, _rightUpperArm, _rightRestOffset, Mathf.PI);

            UpdateStamina(Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            // L'IK tourne pour tout le monde, à partir des targets (locales ou réseau).
            SolveArm(_leftUpperArm, _leftForeArm, _leftHand, _left.Target, -1f);
            SolveArm(_rightUpperArm, _rightForeArm, _rightHand, _right.Target, +1f);
        }

        /// <summary>Logique de prise + cible pour une main (owner uniquement).</summary>
        private void ResolveHand(ref GripState grip, bool wantGrip, Transform shoulder,
                                 Vector3 restOffset, float swingPhase)
        {
            // Direction de visée = regard caméra (yaw du corps + pitch).
            Vector3 lookDir = _camera != null && _camera.CameraRig != null
                ? _camera.CameraRig.forward
                : transform.forward;
            Vector3 origin = shoulder != null ? shoulder.position : transform.position + Vector3.up * 1.4f;

            bool hasSurface = Physics.Raycast(origin, lookDir, out RaycastHit hit, _reach,
                                              _gripMask, QueryTriggerInteraction.Ignore);

            if (grip.Active)
            {
                // Relâche si on lâche le bouton ou si la prise n'est plus valide.
                if (!wantGrip || _stamina <= 0f)
                {
                    Release(ref grip);
                }
                else
                {
                    // La cible suit le point d'ancrage (utile si la prise est sur un corps mobile).
                    grip.Target = grip.ConnectedBody != null
                        ? grip.ConnectedBody.transform.TransformPoint(grip.LocalAnchor)
                        : grip.WorldAnchor;
                    return;
                }
            }

            Vector3 desired;
            if (wantGrip)
            {
                // On veut s'agripper : si une surface est à portée, on l'attrape ; sinon on tend la main vers elle.
                if (hasSurface && _stamina > 0f)
                {
                    StartGrip(ref grip, hit);
                    return;
                }
                desired = hasSurface ? hit.point : transform.TransformPoint(restOffset);
            }
            else
            {
                // Mains libres : pose de repos + balancement de marche (toujours, sans condition).
                desired = RestWithSwing(restOffset, swingPhase, shoulder);
            }

            grip.Target = Vector3.Lerp(grip.Target, desired, Time.fixedDeltaTime * _targetSmoothing);
        }

        /// <summary>Cadence de marche calculée localement (vitesse du Rigidbody), pour le balancement.</summary>
        private void UpdateGait()
        {
            float speed = 0f;
            if (_body != null)
            {
                Vector3 v = _body.CurrentVelocity;
                v.y = 0f;
                speed = v.magnitude;
            }

            float target = speed > 0.3f ? Mathf.Clamp01(speed / Mathf.Max(_swingRefSpeed, 0.1f)) : 0f;
            _gaitWeight = Mathf.Lerp(_gaitWeight, target, Time.fixedDeltaTime * 10f);

            float stride = Mathf.Clamp(speed * _stepTime, 0.1f, 2f);
            float stepsPerSecond = Mathf.Clamp(speed / stride, 0f, 5f);
            _gaitPhase += Time.fixedDeltaTime * stepsPerSecond * Mathf.PI;
        }

        private void StartGrip(ref GripState grip, RaycastHit hit)
        {
            Rigidbody body = _body.Rigidbody;
            var joint = body.gameObject.AddComponent<ConfigurableJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedBody = hit.rigidbody;                 // null => accroché au monde
            joint.anchor = body.transform.InverseTransformPoint(hit.point);

            if (hit.rigidbody != null)
                joint.connectedAnchor = hit.rigidbody.transform.InverseTransformPoint(hit.point);
            else
                joint.connectedAnchor = hit.point;

            // Translation : verrouillée (rigide) ou ressort raide. Rotation libre => oscillation.
            if (_lockGrip)
            {
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            }
            else
            {
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Limited;
                joint.linearLimit = new SoftJointLimit { limit = 0f };
                joint.linearLimitSpring = new SoftJointLimitSpring
                {
                    spring = _gripStiffness,
                    damper = _gripDamping
                };
            }
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;

            grip.Joint = joint;
            grip.Active = true;
            grip.ConnectedBody = hit.rigidbody;
            grip.WorldAnchor = hit.point;
            grip.LocalAnchor = hit.rigidbody != null
                ? hit.rigidbody.transform.InverseTransformPoint(hit.point)
                : hit.point;
            grip.Target = hit.point;
        }

        private void Release(ref GripState grip)
        {
            if (grip.Joint != null) Destroy(grip.Joint);
            grip.Joint = null;
            grip.Active = false;
            grip.ConnectedBody = null;
        }

        private void UpdateStamina(float dt)
        {
            int gripCount = (_left.Active ? 1 : 0) + (_right.Active ? 1 : 0);
            if (gripCount > 0)
                _stamina -= _staminaDrainPerGrip * gripCount * dt;
            else
                _stamina += _staminaRecharge * dt;

            _stamina = Mathf.Clamp01(_stamina);

            if (_stamina <= 0f)
            {
                Release(ref _left);
                Release(ref _right);
            }
        }

        /// <summary>
        /// Pose de repos + balancement : la main tourne AUTOUR DE L'ÉPAULE (arc), pas en translation,
        /// pour que tout le bras oscille comme un pendule (et pas seulement l'avant-bras).
        /// </summary>
        private Vector3 RestWithSwing(Vector3 restOffset, float swingPhase, Transform shoulder)
        {
            Vector3 rest = transform.TransformPoint(restOffset);
            if (shoulder == null) return rest;

            float phase = _gaitPhase + swingPhase;
            float angle = Mathf.Sin(phase) * _armSwingAngle * _gaitWeight;
            // Rotation de la main autour de l'épaule, axe = côté du corps (avant/arrière).
            Vector3 fromShoulder = rest - shoulder.position;
            Vector3 swung = Quaternion.AngleAxis(angle, transform.right) * fromShoulder;
            return shoulder.position + swung;
        }

        private void SolveArm(Transform upper, Transform fore, Transform hand,
                              Vector3 target, float sideSign)
        {
            if (upper == null || fore == null || hand == null) return;

            // Pole : coude vers le bas et vers l'extérieur, pour une pliure naturelle.
            Vector3 pole = upper.position
                           - transform.up * 0.5f
                           + transform.right * (sideSign * 0.4f)
                           + transform.forward * 0.1f;

            TwoBoneIK.Solve(upper, fore, hand, target, pole);
        }

        private void OnDestroy()
        {
            Release(ref _left);
            Release(ref _right);
        }

        /// <summary>État runtime d'une main (non sérialisé réseau directement).</summary>
        private struct GripState
        {
            public bool Active;
            public Vector3 Target;          // cible IK monde
            public ConfigurableJoint Joint;
            public Rigidbody ConnectedBody; // surface agrippée si mobile, sinon null (monde)
            public Vector3 WorldAnchor;     // point de prise monde (surface statique)
            public Vector3 LocalAnchor;     // point de prise local au corps connecté (surface mobile)
        }
    }
}
