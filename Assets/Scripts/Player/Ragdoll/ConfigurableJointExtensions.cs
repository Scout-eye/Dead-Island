using UnityEngine;

namespace Game.Player.Ragdoll
{
    /// <summary>
    /// Helpers pour piloter un <see cref="ConfigurableJoint"/> en rotation cible.
    ///
    /// Le piège de Unity : <c>ConfigurableJoint.targetRotation</c> est exprimée dans l'espace
    /// du joint à l'instant de sa création (rotation initiale du bone), et "à l'envers" (une
    /// targetRotation = identité ramène le bone à sa pose de bind). Ce helper convertit une
    /// rotation LOCALE désirée (celle qu'on voudrait voir sur le transform) en la targetRotation
    /// correcte, en tenant compte de la pose de bind mémorisée à la construction.
    ///
    /// Source de la formule : doc/communauté Unity (SetTargetRotationLocal), adaptée et commentée.
    /// </summary>
    public static class ConfigurableJointExtensions
    {
        /// <summary>
        /// Définit la rotation cible du drive angulaire pour viser une rotation LOCALE donnée.
        /// </summary>
        /// <param name="joint">Le joint à piloter.</param>
        /// <param name="targetLocalRotation">Rotation locale désirée du bone (repère du parent).</param>
        /// <param name="startLocalRotation">Rotation locale du bone au moment de la construction (bind).</param>
        public static void SetTargetRotationLocal(this ConfigurableJoint joint,
                                                  Quaternion targetLocalRotation,
                                                  Quaternion startLocalRotation)
        {
            // Axes principal/secondaire du joint -> base orthonormée du repère du joint.
            Vector3 right = joint.axis;
            Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            Quaternion worldToJointSpace = Quaternion.LookRotation(forward, up);

            // delta = écart entre la pose de bind et la pose désirée, ramené dans l'espace du joint,
            // et inversé (convention Unity : targetRotation tire le bone vers la cible).
            Quaternion resultRotation = Quaternion.Inverse(worldToJointSpace)
                                        * Quaternion.Inverse(targetLocalRotation)
                                        * startLocalRotation
                                        * worldToJointSpace;

            joint.targetRotation = resultRotation;
        }
    }
}
