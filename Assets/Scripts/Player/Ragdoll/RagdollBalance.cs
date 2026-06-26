using UnityEngine;

namespace Game.Player.Ragdoll
{
    /// <summary>
    /// Modèle CANONIQUE leader/follower : le bassin physique POURSUIT le bassin ANIMÉ. Deux forces :
    ///   1. Poursuite de position : tire le bassin physique vers la position du bassin animé (qui
    ///      marche devant, posé au sol). Ça déplace ET porte le corps — qui a son vrai poids et se
    ///      tient sur ses jambes (pieds qui portent via la collision -> ne s'enfonce pas).
    ///   2. Couple de posture : aligne l'orientation du bassin physique sur celle du bassin animé.
    /// Trop incliné -> état "tombé" (ragdoll mou), puis relevé via clip d'animation (dos/ventre).
    ///
    /// Expose IsGrounded / IsUpright (lus par la locomotion). Owner uniquement : un remote est kinematic.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RagdollBalance : MonoBehaviour
    {
        [Header("Poursuite de position (le bassin suit le bassin ANIMÉ)")]
        [Tooltip("Raideur de la poursuite : tire le bassin physique vers la position du bassin animé. " +
                 "C'est ce qui déplace ET porte le corps (les pieds prennent le poids via la collision).")]
        [SerializeField] private float _followSpring = 220f;
        [SerializeField] private float _followDamper = 28f;

        [Header("Sonde de sol")]
        [Tooltip("Portée de la sonde sous le bassin pour l'état 'au sol' (m).")]
        [SerializeField] private float _rideHeight = 0.9f;
        [SerializeField] private float _probeExtra = 0.6f;
        [SerializeField] private float _probeRadius = 0.2f;
        [Tooltip("Hauteur de départ de la sonde au-dessus du bassin (évite de sonder depuis l'intérieur du sol quand il est couché).")]
        [SerializeField] private float _probeLift = 0.4f;
        [SerializeField] private LayerMask _groundMask = ~0;

        [Header("Couple de posture (oriente / se redresse)")]
        [SerializeField] private float _uprightSpring = 130f;
        [SerializeField] private float _uprightDamper = 24f;
        [Tooltip("Sous cet angle d'inclinaison, le perso est considéré DEBOUT (peut marcher).")]
        [SerializeField] private float _uprightAngle = 35f;
        [Tooltip("Au-dessus de cet angle, le perso est considéré TOMBÉ : il devient ragdoll puis tente de se relever.")]
        [SerializeField] private float _fallAngle = 65f;
        [Tooltip("Temps (s) où il reste ragdoll au sol avant de tenter de se relever.")]
        [SerializeField] private float _recoverDelay = 1.0f;

        private ActiveRagdoll _ragdoll;
        private AnimatedReference _anim;
        private Rigidbody _pelvis;
        private bool _isOwner = true;
        private bool _active;

        private Quaternion _uprightBind;   // rotation monde du bassin debout au spawn (réf. de tilt / dos-ventre)

        private bool _grounded;
        private float _groundGrace;   // > 0 : sol ignoré (saut en cours) pour vraiment décoller
        private readonly RaycastHit[] _probeHits = new RaycastHit[16];

        private float _tilt;        // angle (deg) entre l'axe du corps et la verticale
        private bool _downed;       // tombé : ragdoll au sol puis relevé
        private float _downedTime;  // instant où il est tombé
        private bool _getupStarted; // le clip de relevé a été déclenché

        public bool IsGrounded => _grounded;
        /// <summary>Vrai quand le corps est debout et stable (sinon il doit d'abord se relever).</summary>
        public bool IsUpright => !_downed && _tilt < _uprightAngle;

        public void SetOwner(bool owner) => _isOwner = owner;

        /// <summary>Ignore le sol pendant un court instant : suspend la poursuite verticale pour que
        /// le saut décolle sans être ré-aspiré vers la position du bassin animé (resté au sol).</summary>
        public void SuspendGround(float seconds)
        {
            _groundGrace = Mathf.Max(_groundGrace, seconds);
            _grounded = false;
        }

        private void Awake()
        {
            _ragdoll = GetComponent<ActiveRagdoll>();
            _anim = GetComponent<AnimatedReference>();
        }

        private void Start()
        {
            _pelvis = _ragdoll != null ? _ragdoll.Pelvis : null;
            if (_pelvis == null) { enabled = false; return; }

            // Mémorise la posture debout au spawn (référence pour l'inclinaison / dos-ventre).
            _uprightBind = _pelvis.rotation;
            _active = true;
        }

        private void FixedUpdate()
        {
            if (!_active || !_isOwner || _pelvis == null) return;

            if (_groundGrace > 0f) _groundGrace -= Time.fixedDeltaTime;
            UpdateTilt();
            ProbeGround();

            // DEBOUT -> TOMBÉ : le corps devient mou (ragdoll) et tombe vraiment.
            if (!_downed && _tilt > _fallAngle)
            {
                _downed = true;
                _downedTime = Time.time;
                _getupStarted = false;
                if (_ragdoll != null) _ragdoll.SetMotorsEnabled(false);
            }
            // TOMBÉ -> RELEVÉ : redevenu assez droit. On rallume les muscles par sécurité.
            else if (_downed && _tilt < _uprightAngle)
            {
                _downed = false;
                if (_ragdoll != null) _ragdoll.SetMotorsEnabled(true);
            }

            // Après le délai au sol, on lance le clip de relevé (dos/ventre) et on rallume les muscles
            // pour que les joints SUIVENT ce clip ; la portance/le couple assistent la remontée.
            bool recovering = _downed && (Time.time - _downedTime) > _recoverDelay;
            if (_downed && recovering && !_getupStarted)
            {
                _getupStarted = true;
                if (_ragdoll != null) _ragdoll.SetMotorsEnabled(true);
                if (_anim != null) _anim.TriggerGetUp(IsOnBack());
            }

            if (!_downed || recovering)
            {
                ApplyPositionFollow();
                ApplyUprightTorque();
            }
        }

        /// <summary>Sur le dos (face vers le ciel) ou sur le ventre ? Choisit le clip de relevé.</summary>
        private bool IsOnBack()
        {
            Vector3 bodyForward = _pelvis.rotation * (Quaternion.Inverse(_uprightBind) * Vector3.forward);
            return bodyForward.y > 0f; // l'avant du corps pointe vers le haut = sur le dos
        }

        /// <summary>Mesure l'inclinaison du corps par rapport à la verticale.</summary>
        private void UpdateTilt()
        {
            // Axe "vertical" du corps au bind, ré-exprimé dans l'orientation courante du bassin.
            Vector3 bodyUp = _pelvis.rotation * (Quaternion.Inverse(_uprightBind) * Vector3.up);
            _tilt = Vector3.Angle(bodyUp, Vector3.up);
        }

        private void ProbeGround()
        {
            if (_groundGrace > 0f)
            {
                _grounded = false;
                return;
            }

            // Origine relevée au-dessus du bassin : la sonde ne part jamais de l'intérieur du sol
            // (sinon, couché, le bassin est trop bas et la détection échoue -> jamais de relevé).
            Vector3 origin = _pelvis.position + Vector3.up * _probeLift;
            float castLen = _rideHeight + _probeExtra + _probeLift;

            // IMPORTANT : la sonde traverse les propres jambes du perso (cuisses juste sous le
            // bassin). On teste TOUS les contacts et on garde le plus proche qui n'est PAS un os
            // du joueur — sinon on croit ne jamais toucher le sol et le corps ne se porte jamais.
            int n = Physics.SphereCastNonAlloc(origin, _probeRadius, Vector3.down, _probeHits,
                                               castLen, _groundMask, QueryTriggerInteraction.Ignore);
            float best = castLen;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = _probeHits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance <= 0f) continue; // chevauchement initial : ignoré
                if (h.distance < best) { best = h.distance; found = true; }
            }

            _grounded = found;
        }

        /// <summary>
        /// Le bassin physique POURSUIT la position du bassin ANIMÉ (modèle canonique). C'est ce qui
        /// déplace le corps (l'AnimRig marche devant) ET le porte. Le corps a son vrai poids : il se
        /// tient sur ses jambes, les pieds prennent le poids via la collision -> il ne s'enfonce pas.
        /// Suspendu pendant le saut (grâce sol) pour laisser l'arc de saut se faire.
        /// </summary>
        private void ApplyPositionFollow()
        {
            if (_groundGrace > 0f) return;                 // saut en cours : on laisse la gravité agir
            if (_anim == null || _anim.Hips == null) return;

            Vector3 err = _anim.Hips.position - _pelvis.position;
            Vector3 force = (err * _followSpring) - (_pelvis.linearVelocity * _followDamper);
            _pelvis.AddForce(force, ForceMode.Acceleration);
        }

        /// <summary>Couple PD qui aligne l'orientation du bassin physique sur celle du bassin ANIMÉ.</summary>
        private void ApplyUprightTorque()
        {
            Quaternion target = (_anim != null && _anim.Hips != null) ? _anim.Hips.rotation : _uprightBind;
            Quaternion delta = target * Quaternion.Inverse(_pelvis.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (Mathf.Abs(angle) < 0.01f || float.IsInfinity(axis.x)) { DampSpin(); return; }

            Vector3 torque = axis.normalized * (angle * Mathf.Deg2Rad * _uprightSpring)
                             - _pelvis.angularVelocity * _uprightDamper;
            _pelvis.AddTorque(torque, ForceMode.Acceleration);
        }

        private void DampSpin()
        {
            _pelvis.AddTorque(-_pelvis.angularVelocity * _uprightDamper, ForceMode.Acceleration);
        }
    }
}
