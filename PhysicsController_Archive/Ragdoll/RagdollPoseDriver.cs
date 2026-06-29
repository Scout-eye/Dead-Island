using UnityEngine;

namespace Game.Player.Ragdoll
{
    /// <summary>
    /// Recopie la pose du squelette animé (<see cref="AnimatedReference"/>) dans le ragdoll physique :
    /// chaque FixedUpdate, la targetRotation de chaque joint = rotation locale de l'os animé équivalent.
    /// Le mouvement final émerge de la physique (chocs, terrain, traîne) mais la CIBLE est l'animation
    /// — c'est la technique PEAK / active ragdoll "qui suit une animation".
    ///
    /// Pousse aussi les paramètres Speed / Grounded vers l'Animator (idle <-> marche <-> course).
    /// Les MAINS sont volontairement exclues : elles sont pilotées en force par <see cref="HandReach"/>.
    /// </summary>
    [DefaultExecutionOrder(-15)]
    [DisallowMultipleComponent]
    public sealed class RagdollPoseDriver : MonoBehaviour
    {
        [Header("Regard (la tête tourne vers la caméra)")]
        [Tooltip("Angle max de rotation de la tête avant que le corps ne suive. Doit matcher le Max Head Yaw de la locomotion.")]
        [SerializeField] private float _maxHeadYaw = 70f;
        [SerializeField] private float _maxLookPitch = 45f;
        [Tooltip("Si la tête s'incline en haut/bas À L'ENVERS, mets -1.")]
        [SerializeField] private float _pitchSign = 1f;

        // Tous les os pilotables sauf le bassin (racine sans joint) et les mains (gérées par HandReach).
        private static readonly RagdollPart[] Driven =
        {
            RagdollPart.Spine, RagdollPart.Spine1, RagdollPart.Spine2, RagdollPart.Neck, RagdollPart.Head,
            RagdollPart.LeftUpperArm, RagdollPart.LeftForeArm,
            RagdollPart.RightUpperArm, RagdollPart.RightForeArm,
            RagdollPart.LeftUpLeg, RagdollPart.LeftLeg, RagdollPart.LeftFoot,
            RagdollPart.RightUpLeg, RagdollPart.RightLeg, RagdollPart.RightFoot,
        };

        private ActiveRagdoll _ragdoll;
        private AnimatedReference _anim;
        private RagdollLocomotion _loco;
        private PlayerCamera _camera;

        private void Awake()
        {
            _ragdoll = GetComponent<ActiveRagdoll>();
            _anim = GetComponent<AnimatedReference>();
            _loco = GetComponent<RagdollLocomotion>();
            _camera = GetComponent<PlayerCamera>();
        }

        private void FixedUpdate()
        {
            if (_ragdoll == null || !_ragdoll.IsBuilt || _loco == null || !_loco.IsOwner) return;

            // Recopie la pose animée dans les joints (les paramètres Speed/Grounded de l'Animator sont
            // pilotés par la locomotion). Si pas d'AnimRig : les joints gardent leur pose de bind.
            if (_anim == null || !_anim.Ready) return;
            foreach (var part in Driven)
                _ragdoll.DrivePart(part, _anim.LocalRotation(part));

            ApplyLook();
        }

        /// <summary>
        /// Tourne la tête (réparti sur haut-de-colonne / nuque / tête) vers le regard caméra, borné à
        /// _maxHeadYaw. Au-delà, c'est la locomotion qui fait pivoter le corps. Delta appliqué en
        /// MONDE (yaw autour de la verticale, pitch autour du côté du corps) -> indépendant des axes Mixamo.
        /// </summary>
        private void ApplyLook()
        {
            if (_camera == null) return;
            float yaw = Mathf.Clamp(Mathf.DeltaAngle(_loco.BodyYaw, _camera.LookYaw), -_maxHeadYaw, _maxHeadYaw);
            float pitch = Mathf.Clamp(_camera.Pitch * _pitchSign, -_maxLookPitch, _maxLookPitch);
            if (Mathf.Abs(yaw) < 0.5f && Mathf.Abs(pitch) < 0.5f) return;

            Vector3 right = Quaternion.Euler(0f, _loco.BodyYaw, 0f) * Vector3.right;
            LookBone(RagdollPart.Spine2, yaw * 0.25f, pitch * 0.25f, right);
            LookBone(RagdollPart.Neck, yaw * 0.40f, pitch * 0.40f, right);
            LookBone(RagdollPart.Head, yaw * 0.35f, pitch * 0.35f, right);
        }

        private void LookBone(RagdollPart part, float yaw, float pitch, Vector3 right)
        {
            Transform bone = _ragdoll.GetTransform(part);
            if (bone == null || bone.parent == null) return;
            Quaternion delta = Quaternion.AngleAxis(pitch, right) * Quaternion.Euler(0f, yaw, 0f);
            Quaternion desiredWorld = delta * _anim.WorldRotation(part);
            Quaternion desiredLocal = Quaternion.Inverse(bone.parent.rotation) * desiredWorld;
            _ragdoll.DrivePart(part, desiredLocal);
        }
    }
}
