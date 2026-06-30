using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Plie le bras droit pour "tenir" un objet devant le buste, via l'IK main de l'Animator.
    /// Utilisé pour les joueurs DISTANTS (l'objet est sur leur main) → les autres voient qu'ils tiennent
    /// quelque chose. Inactif pour le joueur local (son objet est un viewmodel sur la caméra).
    ///
    /// À placer sur le GameObject de l'Animator (le modèle). Piloté par RemotePlayer via SetHolding.
    /// Nécessite "IK Pass" sur le layer.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class PlayerHoldIK : MonoBehaviour
    {
        [Range(0f, 1f)] [SerializeField] private float _weight = 0.75f;
        [Tooltip("Cible de la main (repère local du modèle) : devant le buste.")]
        [SerializeField] private Vector3 _holdOffset = new Vector3(0.16f, 1.3f, 0.38f);
        [SerializeField] private float _smoothing = 6f;

        private Animator _anim;
        private bool _holding;
        private float _w;

        private void Awake() => _anim = GetComponent<Animator>();

        public void SetHolding(bool holding) => _holding = holding;

        private void OnAnimatorIK(int layer)
        {
            if (_anim == null) return;
            _w = Mathf.MoveTowards(_w, _holding ? _weight : 0f, Time.deltaTime * _smoothing);
            if (_w <= 0.001f) return;

            Vector3 target = transform.TransformPoint(_holdOffset); // devant le buste, en monde
            _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, _w);
            _anim.SetIKPosition(AvatarIKGoal.RightHand, target);
        }
    }
}
