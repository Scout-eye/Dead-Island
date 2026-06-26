using System.Collections.Generic;
using UnityEngine;

namespace Game.Player.Ragdoll
{
    /// <summary>
    /// Squelette de RÉFÉRENCE animé (invisible) que le ragdoll physique recopie. C'est la pièce qui
    /// manquait : sans pose animée à viser, les joints n'avaient aucune cible cohérente -> pantin.
    ///
    /// Un Animator joue les clips (idle / marche / course / relevés…) sur une copie du même rig Y Bot
    /// dont les renderers sont coupés. Chaque FixedUpdate, <see cref="RagdollPoseDriver"/> lit la
    /// rotation LOCALE de chaque os animé et la pousse dans la targetRotation du joint correspondant.
    /// Comme c'est le MÊME rig, les rotations locales se correspondent un à un.
    ///
    /// Expose aussi un pont vers les paramètres de l'Animator (Speed / Grounded / déclencheurs de relevé).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnimatedReference : MonoBehaviour
    {
        [Tooltip("Animator du squelette de référence (sur l'AnimRig invisible).")]
        [SerializeField] private Animator _animator;
        [Tooltip("Racine de l'armature animée (on y résout les os par nom). C'est l'objet qu'on co-localise.")]
        [SerializeField] private Transform _animRoot;
        [Tooltip("Capteur de root motion (sur l'AnimRig).")]
        [SerializeField] private RootMotionCatcher _catcher;

        private readonly Dictionary<RagdollPart, Transform> _bones = new Dictionary<RagdollPart, Transform>();

        // Os physique -> nom du bone Mixamo (mêmes noms que ActiveRagdoll).
        private static readonly (RagdollPart part, string bone)[] Map =
        {
            (RagdollPart.Hips, "Hips"), (RagdollPart.Spine, "Spine"), (RagdollPart.Spine1, "Spine1"),
            (RagdollPart.Spine2, "Spine2"), (RagdollPart.Neck, "Neck"), (RagdollPart.Head, "Head"),
            (RagdollPart.LeftUpperArm, "LeftArm"), (RagdollPart.LeftForeArm, "LeftForeArm"), (RagdollPart.LeftHand, "LeftHand"),
            (RagdollPart.RightUpperArm, "RightArm"), (RagdollPart.RightForeArm, "RightForeArm"), (RagdollPart.RightHand, "RightHand"),
            (RagdollPart.LeftUpLeg, "LeftUpLeg"), (RagdollPart.LeftLeg, "LeftLeg"), (RagdollPart.LeftFoot, "LeftFoot"),
            (RagdollPart.RightUpLeg, "RightUpLeg"), (RagdollPart.RightLeg, "RightLeg"), (RagdollPart.RightFoot, "RightFoot"),
        };

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int GetUpBackHash = Animator.StringToHash("GetUpBack");
        private static readonly int GetUpFrontHash = Animator.StringToHash("GetUpFront");

        /// <summary>Vrai si le squelette de référence est utilisable (au moins le bassin résolu).</summary>
        public bool Ready => _bones.Count > 0;

        /// <summary>Transform racine de l'AnimRig (qu'on co-localise sur le corps physique).</summary>
        public Transform Root => _animRoot;
        /// <summary>Os "Hips" animé (sa position/rotation MONDE = cible du bassin physique).</summary>
        public Transform Hips => _bones.TryGetValue(RagdollPart.Hips, out var t) ? t : null;
        /// <summary>Vitesse imprimée par le root motion de l'animation (marche/course).</summary>
        public Vector3 RootMotionVelocity => _catcher != null ? _catcher.Velocity : Vector3.zero;

        private void Awake()
        {
            if (_animator != null)
            {
                // Toujours animer (même hors écran) et en phase avec la physique.
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _animator.updateMode = AnimatorUpdateMode.Fixed;
            }
            BuildMap();
        }

        private void BuildMap()
        {
            if (_animRoot == null) return;
            foreach (var (part, bone) in Map)
            {
                var t = Find(bone);
                if (t != null) _bones[part] = t;
            }
        }

        /// <summary>Rotation locale de l'os animé (cible à recopier dans le joint physique).</summary>
        public Quaternion LocalRotation(RagdollPart part)
            => _bones.TryGetValue(part, out var t) ? t.localRotation : Quaternion.identity;

        public bool Has(RagdollPart part) => _bones.ContainsKey(part);

        // --- Pont vers l'Animator ---
        public void SetSpeed(float speed) { if (_animator != null) _animator.SetFloat(SpeedHash, speed); }
        public void SetGrounded(bool grounded) { if (_animator != null) _animator.SetBool(GroundedHash, grounded); }
        public void TriggerGetUp(bool onBack)
        {
            if (_animator == null) return;
            _animator.SetTrigger(onBack ? GetUpBackHash : GetUpFrontHash);
        }

        private Transform Find(string bone)
        {
            foreach (var t in _animRoot.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == bone || n == "mixamorig:" + bone || n.EndsWith(":" + bone)) return t;
            }
            return null;
        }
    }
}
