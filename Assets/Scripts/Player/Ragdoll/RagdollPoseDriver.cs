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

        private void Awake()
        {
            _ragdoll = GetComponent<ActiveRagdoll>();
            _anim = GetComponent<AnimatedReference>();
            _loco = GetComponent<RagdollLocomotion>();
        }

        private void FixedUpdate()
        {
            if (_ragdoll == null || !_ragdoll.IsBuilt || _loco == null || !_loco.IsOwner) return;

            // Recopie la pose animée dans les joints (les paramètres Speed/Grounded de l'Animator sont
            // pilotés par la locomotion). Si pas d'AnimRig : les joints gardent leur pose de bind.
            if (_anim == null || !_anim.Ready) return;
            foreach (var part in Driven)
                _ragdoll.DrivePart(part, _anim.LocalRotation(part));
        }
    }
}
