using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Animation procédurale du corps (zéro clip, zéro Animator) :
    ///   - Look tête/buste : la colonne (spine -> head) s'oriente vers le regard, clampée.
    ///   - Jambes : système de PAS avec IK et pieds plantés au sol (pas de glissement). Chaque
    ///     pied reste fixe au sol tant qu'il n'a pas pris de retard, puis fait un pas en arc vers
    ///     l'avant. La longueur/hauteur/fréquence des pas suit la vitesse réelle (marche -> course).
    ///   - Bob du bassin synchronisé, absorbé par la flexion des genoux (IK).
    ///   - Expose GaitPhase / GaitWeight pour le balancement des bras (PlayerHands).
    ///
    /// S'exécute AVANT PlayerHands (ordre -10) : il oriente la colonne (parent des bras) et bouge
    /// le bassin, les bras sont resolvés en IK par-dessus.
    /// </summary>
    // rev: orientation pieds à plat + repositionnement sur dérive (virages)
    [DefaultExecutionOrder(-10)]
    [DisallowMultipleComponent]
    public sealed class PlayerProceduralAnimator : MonoBehaviour
    {
        [Header("Colonne (du bas vers la tête)")]
        [SerializeField] private Transform _spine;
        [SerializeField] private Transform _spine1;
        [SerializeField] private Transform _spine2;
        [SerializeField] private Transform _neck;
        [SerializeField] private Transform _head;

        [Header("Look")]
        [SerializeField] private float _maxLookYaw = 70f;
        [SerializeField] private float _maxLookPitch = 45f;
        [SerializeField] private float _lookSmoothing = 12f;
        [SerializeField] private float _pitchSign = 1f;

        [Header("Jambes")]
        [SerializeField] private Transform _hips;
        [SerializeField] private Transform _leftUpLeg;
        [SerializeField] private Transform _leftLeg;
        [SerializeField] private Transform _leftFoot;
        [SerializeField] private Transform _rightUpLeg;
        [SerializeField] private Transform _rightLeg;
        [SerializeField] private Transform _rightFoot;

        [Header("Pas")]
        [SerializeField] private LayerMask _groundMask = ~0;
        [Tooltip("Écartement latéral des pieds (m).")]
        [SerializeField] private float _footSpacing = 0.12f;
        [Tooltip("Hauteur de la cheville au-dessus du sol (m).")]
        [SerializeField] private float _ankleHeight = 0.1f;
        [Tooltip("La foulée = vitesse × ce temps (s). Plus grand = grandes enjambées, cadence plus lente.")]
        [SerializeField] private float _stepTime = 0.35f;
        [Tooltip("Foulée maximale (m), borne la longueur des pas en course.")]
        [SerializeField] private float _maxStride = 0.9f;
        [Tooltip("Sécurité : distance d'atterrissage max d'un pied devant la hanche (m). Large maintenant que le bassin est abaissé.")]
        [SerializeField] private float _maxFootReach = 1.0f;
        [Tooltip("Hauteur de lever du pied à l'arrêt/petit pas (m).")]
        [SerializeField] private float _stepHeight = 0.08f;
        [Tooltip("Hauteur de lever ajoutée par mètre de foulée (lève les genoux en marche/course).")]
        [SerializeField] private float _stepHeightPerStride = 0.28f;
        [SerializeField] private float _maxStepHeight = 0.45f;
        [Tooltip("Durée d'un pas en l'air (s).")]
        [SerializeField] private float _swingDuration = 0.28f;
        [Tooltip("Distance (m) à laquelle un pied dérive de sous la hanche avant de se replacer (virages).")]
        [SerializeField] private float _maxFootDrift = 0.26f;

        [Header("Bassin")]
        [SerializeField] private float _hipBob = 0.04f;
        [Tooltip("Abaissement constant du bassin (m) : genoux fléchis pour permettre l'enjambée.")]
        [SerializeField] private float _hipDrop = 0.12f;

        [Header("Jambes en l'air (saut / chute)")]
        [Tooltip("Temps (s) de transition sol <-> air pour les jambes.")]
        [SerializeField] private float _airBlendTime = 0.12f;
        [Tooltip("Vitesse de chute (m/s) à laquelle les jambes sont pleinement tendues vers le sol.")]
        [SerializeField] private float _fallExtendSpeed = 6f;

        private PlayerBody _body;
        private PlayerCamera _camera;

        private Transform[] _lookChain;
        private Quaternion[] _lookBind;
        private Vector3 _hipsBindLocalPos;

        private float _smoothedYaw;
        private float _smoothedPitch;
        private float _gaitPhase;   // pour bras + bob
        private float _gaitWeight;
        private bool _initialized;

        private FootStep _left;
        private FootStep _right;
        private readonly RaycastHit[] _rayBuffer = new RaycastHit[8];
        private float _airBlend;       // 0 sol, 1 en l'air
        private float _legLength = 0.85f;

        /// <summary>Phase de marche (radians), pour synchroniser le balancement des bras.</summary>
        public float GaitPhase => _gaitPhase;
        /// <summary>Intensité de marche 0..1 (0 à l'arrêt).</summary>
        public float GaitWeight => _gaitWeight;

        private void Awake()
        {
            _body = GetComponent<PlayerBody>();
            _camera = GetComponent<PlayerCamera>();

            BuildLookChain();
            if (_hips != null) _hipsBindLocalPos = _hips.localPosition;

            _left = new FootStep(_leftUpLeg, _leftLeg, _leftFoot, -1f);
            _right = new FootStep(_rightUpLeg, _rightLeg, _rightFoot, +1f);

            // Mémorise l'orientation des pieds en bind (T-pose : à plat, face avant), relative au corps.
            if (_leftFoot != null) _left.FootBind = Quaternion.Inverse(transform.rotation) * _leftFoot.rotation;
            if (_rightFoot != null) _right.FootBind = Quaternion.Inverse(transform.rotation) * _rightFoot.rotation;

            if (_leftUpLeg != null && _leftLeg != null && _leftFoot != null)
                _legLength = Vector3.Distance(_leftUpLeg.position, _leftLeg.position)
                           + Vector3.Distance(_leftLeg.position, _leftFoot.position);
        }

        private void BuildLookChain()
        {
            var bones = new[] { _spine, _spine1, _spine2, _neck, _head };
            int count = 0;
            foreach (var b in bones) if (b != null) count++;
            _lookChain = new Transform[count];
            _lookBind = new Quaternion[count];
            int i = 0;
            foreach (var b in bones)
                if (b != null) { _lookChain[i] = b; _lookBind[i] = b.localRotation; i++; }
        }

        private void LateUpdate()
        {
            ComputeGait();
            ApplyHipBob();
            ApplyLook();
            UpdateLegs();
        }

        // --- Cadence -------------------------------------------------------

        private void ComputeGait()
        {
            float speed = HorizontalSpeed(out bool grounded);
            float walkRef = _body != null ? _body.WalkSpeed : 4.5f;
            // Basé sur la vitesse (pas sur IsGrounded, peu fiable) : les bras balancent dès qu'on bouge.
            float targetWeight = speed > 0.3f ? Mathf.Clamp01(speed / Mathf.Max(walkRef, 0.1f)) : 0f;
            _gaitWeight = Mathf.Lerp(_gaitWeight, targetWeight, Time.deltaTime * 10f);

            // Cadence = vitesse / foulée (constante en marche, plus rapide en course).
            float stride = Mathf.Clamp(speed * _stepTime, 0.1f, _maxStride);
            float stepsPerSecond = Mathf.Clamp(speed / stride, 0f, 5f);
            _gaitPhase += Time.deltaTime * stepsPerSecond * Mathf.PI;
        }

        private void ApplyHipBob()
        {
            if (_hips == null) return;
            float bob = Mathf.Sin(_gaitPhase * 2f) * _hipBob * _gaitWeight;
            // Abaissement du bassin SEULEMENT en mouvement (genoux fléchis pour enjamber) ;
            // à l'arrêt le perso se redresse (sinon posture "C-3PO"). + bob synchronisé.
            // Calculé dans le repère du parent (robuste à une armature pivotée).
            float offset = bob - _hipDrop * _gaitWeight;
            Vector3 localUp = _hips.parent != null
                ? _hips.parent.InverseTransformDirection(Vector3.up)
                : Vector3.up;
            _hips.localPosition = _hipsBindLocalPos + localUp * offset;
        }

        // --- Look ----------------------------------------------------------

        private void ApplyLook()
        {
            if (_lookChain == null || _lookChain.Length == 0) return;

            for (int i = 0; i < _lookChain.Length; i++)
                _lookChain[i].localRotation = _lookBind[i];

            float bodyYaw = _body != null ? _body.BodyYaw : transform.eulerAngles.y;
            float lookYaw = _camera != null ? _camera.LookYaw : bodyYaw;
            float lookPitch = _camera != null ? _camera.Pitch : 0f;

            float targetYaw = Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, lookYaw), -_maxLookYaw, _maxLookYaw);
            float targetPitch = Mathf.Clamp(lookPitch * _pitchSign, -_maxLookPitch, _maxLookPitch);

            _smoothedYaw = Mathf.Lerp(_smoothedYaw, targetYaw, Time.deltaTime * _lookSmoothing);
            _smoothedPitch = Mathf.Lerp(_smoothedPitch, targetPitch, Time.deltaTime * _lookSmoothing);

            float yawPer = _smoothedYaw / _lookChain.Length;
            float pitchPer = _smoothedPitch / _lookChain.Length;
            Vector3 right = transform.right;

            // Cumulatif root -> tête : la tête atteint le total, courbure progressive en chemin.
            foreach (var bone in _lookChain)
                bone.rotation = Quaternion.AngleAxis(yawPer, Vector3.up)
                              * Quaternion.AngleAxis(pitchPer, right)
                              * bone.rotation;
        }

        // --- Jambes (IK + pas plantés) -------------------------------------

        private void UpdateLegs()
        {
            float speed = HorizontalSpeed(out bool grounded);
            Vector3 vel = _body != null ? _body.CurrentVelocity : Vector3.zero;
            float vy = vel.y;
            Vector3 flat = vel; flat.y = 0f;
            Vector3 moveDir = flat.sqrMagnitude > 0.04f ? flat.normalized : transform.forward;

            // Mélange sol <-> air (0 au sol, 1 en l'air) et facteur d'extension en chute.
            _airBlend = Mathf.MoveTowards(_airBlend, grounded ? 0f : 1f, Time.deltaTime / Mathf.Max(_airBlendTime, 0.01f));
            float extend01 = Mathf.Clamp01(-vy / Mathf.Max(_fallExtendSpeed, 0.1f)); // 0 montée/apex, 1 chute

            float stride = Mathf.Clamp(speed * _stepTime, 0f, _maxStride);
            float stepHeight = Mathf.Min(_stepHeight + stride * _stepHeightPerStride, _maxStepHeight);
            bool moving = grounded && speed > 0.3f;

            EnsureInit(moveDir);

            UpdateFoot(ref _left, moveDir, speed, stride, stepHeight, _swingDuration, moving, _right.Stepping, grounded);
            UpdateFoot(ref _right, moveDir, speed, stride, stepHeight, _swingDuration, moving, _left.Stepping, grounded);

            SolveFoot(_left, extend01);
            SolveFoot(_right, extend01);
        }

        private void EnsureInit(Vector3 moveDir)
        {
            if (_initialized) return;
            _left.CurrentPos = GroundedHome(_left, moveDir, 0f);
            _right.CurrentPos = GroundedHome(_right, moveDir, 0f);
            _initialized = true;
        }

        private void UpdateFoot(ref FootStep foot, Vector3 moveDir, float speed, float stride,
                                float stepHeight, float stepDuration, bool moving, bool otherStepping, bool grounded)
        {
            // "home" = position au sol directement sous la hanche du pied.
            Vector3 home = GroundedHome(foot, moveDir, 0f);

            // En l'air : pas de pas planté ; le pied reste sous la hanche pour un atterrissage propre.
            if (!grounded)
            {
                foot.CurrentPos = home;
                foot.StepT = -1f;
                return;
            }

            if (foot.Stepping)
            {
                foot.StepT += Time.deltaTime / Mathf.Max(stepDuration, 0.01f);
                float t = Mathf.Clamp01(foot.StepT);
                // Horizontal adouci (ease in/out) pour éviter le "snap", lift en arc.
                Vector3 flat = Vector3.Lerp(foot.StepStart, foot.StepTarget, Mathf.SmoothStep(0f, 1f, t));
                float lift = Mathf.Sin(t * Mathf.PI) * stepHeight;
                foot.CurrentPos = flat + Vector3.up * lift;
                if (foot.StepT >= 1f) { foot.StepT = -1f; foot.CurrentPos = foot.StepTarget; }
            }
            else
            {
                // Deux raisons de faire un pas :
                //  - en marche : le pied est une demi-foulée DERRIÈRE la hanche ;
                //  - virage / sur place : le pied a trop dérivé de sous la hanche (sinon les jambes vrillent).
                float behind = Vector3.Dot(home - foot.CurrentPos, moveDir);
                float drift = Vector3.Distance(foot.CurrentPos, home);
                float half = Mathf.Max(stride * 0.5f, 0.12f);
                bool needStep = (moving && behind > half) || drift > _maxFootDrift;
                if (needStep && !otherStepping)
                {
                    // En marche : on vise devant la hanche, mais JAMAIS au-delà de l'allonge de la
                    // jambe (sinon elle se tend à fond). À l'arrêt / virage : on se replace sous la hanche.
                    float plantAhead = moving ? Mathf.Min(half + speed * stepDuration, _maxFootReach) : 0f;
                    foot.StepStart = foot.CurrentPos;
                    foot.StepTarget = home + moveDir * plantAhead;
                    foot.StepT = 0f;
                }
            }
        }

        /// <summary>Position au sol sous la hanche du pied, avancée de "lead" mètres.</summary>
        private Vector3 GroundedHome(FootStep foot, Vector3 moveDir, float lead)
        {
            Vector3 home = transform.TransformPoint(new Vector3(foot.Side * _footSpacing, 0f, 0f))
                           + moveDir * lead;
            Vector3 origin = home + Vector3.up * 0.6f;
            if (GroundRay(origin, out Vector3 point))
                return point;
            home.y = transform.position.y;
            return home;
        }

        /// <summary>Raycast sol vers le bas en ignorant les colliders du joueur lui-même.</summary>
        private bool GroundRay(Vector3 origin, out Vector3 point)
        {
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, _rayBuffer, 1.5f,
                                            _groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            point = origin;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = _rayBuffer[i];
                if (h.collider.transform.IsChildOf(transform)) continue; // ignore self (capsule)
                if (h.distance < best) { best = h.distance; point = h.point; found = true; }
            }
            return found;
        }

        private void SolveFoot(FootStep foot, float extend01)
        {
            if (foot.UpLeg == null || foot.Leg == null || foot.Foot == null) return;

            // Cible plantée (sol) <-> cible aérienne (saut/chute), mélangées par _airBlend.
            Vector3 plantedTarget = foot.CurrentPos + Vector3.up * _ankleHeight;
            Vector3 airTarget = AirborneTarget(foot, extend01);
            Vector3 target = Vector3.Lerp(plantedTarget, airTarget, _airBlend);

            Vector3 pole = foot.UpLeg.position + transform.forward * 0.6f - transform.up * 0.1f;
            // reachClamp < 1 => le genou garde toujours une légère flexion.
            TwoBoneIK.Solve(foot.UpLeg, foot.Leg, foot.Foot, target, pole, 0.95f);
            foot.Foot.rotation = transform.rotation * foot.FootBind;
        }

        /// <summary>
        /// Pose des jambes en l'air : repliées/resserrées en montée & apex, tendues et écartées vers
        /// le sol en chute (préparation de l'atterrissage). Léger décalage gauche/droite (anti-jumeau).
        /// </summary>
        private Vector3 AirborneTarget(FootStep foot, float extend01)
        {
            Vector3 hip = foot.UpLeg != null ? foot.UpLeg.position : transform.position;
            float reach = Mathf.Lerp(0.5f, 0.92f, extend01) * _legLength;
            Vector3 dir = -transform.up
                          + transform.forward * Mathf.Lerp(-0.35f, 0.15f, extend01)              // replié arrière -> tendu avant
                          + transform.right * (foot.Side * Mathf.Lerp(0.12f, 0.28f, extend01));   // resserré -> écarté
            dir = dir.sqrMagnitude > 1e-5f ? dir.normalized : -transform.up;
            float asym = foot.Side * 0.05f * (1f - extend01);
            return hip + dir * (reach + asym);
        }

        private float HorizontalSpeed(out bool grounded)
        {
            grounded = _body != null && _body.IsGrounded;
            if (_body == null) return 0f;
            Vector3 v = _body.CurrentVelocity;
            v.y = 0f;
            return v.magnitude;
        }

        private struct FootStep
        {
            public readonly Transform UpLeg, Leg, Foot;
            public readonly float Side;     // -1 gauche, +1 droite
            public Vector3 CurrentPos;      // monde, planté ou en cours de pas
            public Vector3 StepStart, StepTarget;
            public float StepT;             // -1 = planté ; 0..1 = pas en cours
            public Quaternion FootBind;     // orientation du pied en bind, relative au corps

            public FootStep(Transform upLeg, Transform leg, Transform foot, float side)
            {
                UpLeg = upLeg; Leg = leg; Foot = foot; Side = side;
                CurrentPos = StepStart = StepTarget = Vector3.zero;
                StepT = -1f;
                FootBind = Quaternion.identity;
            }

            public bool Stepping => StepT >= 0f;
        }
    }
}
