using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Bras procéduraux : IK deux bras vers une pose de repos le long du corps, avec un balancement
    /// de marche/course (rotation autour de l'épaule). 100% C#, pas d'Animator.
    ///
    /// L'IK est résolue en LateUpdate pour owner ET remote (le remote rejoue les cibles reçues).
    /// Données synchronisables isolées : LeftHandTarget, RightHandTarget.
    /// </summary>
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

        [Header("Pose au repos (local au corps) — mains à hauteur de hanche, bras légèrement plié")]
        [SerializeField] private Vector3 _leftRestOffset = new Vector3(-0.2f, 1.0f, 0.18f);
        [SerializeField] private Vector3 _rightRestOffset = new Vector3(0.2f, 1.0f, 0.18f);
        [SerializeField] private float _targetSmoothing = 14f;

        [Header("Balancement des bras (marche/course)")]
        [Tooltip("Amplitude de balancement en DEGRÉS (rotation du bras entier autour de l'épaule).")]
        [SerializeField] private float _armSwingAngle = 32f;
        [Tooltip("Vitesse (m/s) de marche de référence pour l'intensité du balancement.")]
        [SerializeField] private float _swingRefSpeed = 2.3f;
        [SerializeField] private float _stepTime = 0.35f;   // doit matcher l'animator

        private PlayerBody _body;

        // Cadence calculée localement depuis la vitesse (autonome).
        private float _gaitPhase;
        private float _gaitWeight;

        private Vector3 _leftTarget;
        private Vector3 _rightTarget;

        // --- Données synchronisables ---
        public Vector3 LeftHandTarget => _leftTarget;
        public Vector3 RightHandTarget => _rightTarget;

        /// <summary>Pour le remote : injecter les cibles de mains reçues du réseau.</summary>
        public void SetNetworkHands(Vector3 leftTarget, Vector3 rightTarget)
        {
            _leftTarget = leftTarget;
            _rightTarget = rightTarget;
        }

        private void Awake()
        {
            _body = GetComponent<PlayerBody>();
            // Initialise les cibles sur la pose de repos (évite un saut d'IK au premier frame).
            _leftTarget = transform.TransformPoint(_leftRestOffset);
            _rightTarget = transform.TransformPoint(_rightRestOffset);
        }

        private void FixedUpdate()
        {
            if (_body == null || !_body.IsOwner) return;

            UpdateGait();

            // Bras opposés (déphasage PI) pour un balancement naturel.
            Vector3 leftDesired = RestWithSwing(_leftRestOffset, 0f, _leftUpperArm);
            Vector3 rightDesired = RestWithSwing(_rightRestOffset, Mathf.PI, _rightUpperArm);
            _leftTarget = Vector3.Lerp(_leftTarget, leftDesired, Time.fixedDeltaTime * _targetSmoothing);
            _rightTarget = Vector3.Lerp(_rightTarget, rightDesired, Time.fixedDeltaTime * _targetSmoothing);
        }

        private void LateUpdate()
        {
            // L'IK tourne pour tout le monde, à partir des cibles (locales ou réseau).
            SolveArm(_leftUpperArm, _leftForeArm, _leftHand, _leftTarget, -1f);
            SolveArm(_rightUpperArm, _rightForeArm, _rightHand, _rightTarget, +1f);
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

        /// <summary>
        /// Pose de repos + balancement : la main tourne AUTOUR DE L'ÉPAULE (arc) pour que tout le
        /// bras oscille comme un pendule (et pas seulement l'avant-bras).
        /// </summary>
        private Vector3 RestWithSwing(Vector3 restOffset, float swingPhase, Transform shoulder)
        {
            Vector3 rest = transform.TransformPoint(restOffset);
            if (shoulder == null) return rest;

            float phase = _gaitPhase + swingPhase;
            float angle = Mathf.Sin(phase) * _armSwingAngle * _gaitWeight;
            Vector3 fromShoulder = rest - shoulder.position;
            Vector3 swung = Quaternion.AngleAxis(angle, transform.right) * fromShoulder;
            return shoulder.position + swung;
        }

        private void SolveArm(Transform upper, Transform fore, Transform hand, Vector3 target, float sideSign)
        {
            if (upper == null || fore == null || hand == null) return;

            // Pole : coude vers le bas et vers l'extérieur, pour une pliure naturelle.
            Vector3 pole = upper.position
                           - transform.up * 0.5f
                           + transform.right * (sideSign * 0.4f)
                           + transform.forward * 0.1f;

            TwoBoneIK.Solve(upper, fore, hand, target, pole);
        }
    }
}
