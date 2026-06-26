using UnityEngine;

namespace Game.Player.Ragdoll
{
    /// <summary>
    /// À poser sur l'AnimRig (le GameObject qui porte l'Animator). Capte le ROOT MOTION du clip
    /// (la translation que l'animation de marche/course imprime à la racine) SANS déplacer le
    /// transform — on co-localise l'AnimRig nous-mêmes ailleurs. La vitesse captée sert à propulser
    /// le corps physique exactement à l'allure de l'animation -> les pieds ne glissent pas.
    ///
    /// OnAnimatorMove n'est appelé que sur le GameObject de l'Animator, d'où ce composant dédié.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class RootMotionCatcher : MonoBehaviour
    {
        private Animator _animator;

        /// <summary>Vitesse (monde) imprimée par le root motion à cette frame.</summary>
        public Vector3 Velocity { get; private set; }

        private void Awake() => _animator = GetComponent<Animator>();

        private void OnAnimatorMove()
        {
            float dt = Time.deltaTime;
            Velocity = dt > 1e-5f ? _animator.deltaPosition / dt : Vector3.zero;
            // On n'applique PAS le root motion au transform (pas de ApplyBuiltinRootMotion) :
            // c'est la locomotion qui place l'AnimRig (co-localisé sur le corps physique).
        }
    }
}
