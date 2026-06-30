using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Foot IK : chaque pied est repositionné sur la surface réelle sous lui (sol, marche, objet)
    /// au lieu de clipper au travers. Raycast vers le bas depuis chaque pied + SetIKPosition/Rotation,
    /// avec un poids qui relâche l'IK quand le pied se lève (préserve le cycle de marche).
    ///
    /// À placer sur le MÊME GameObject que l'Animator (le modèle). Nécessite "IK Pass" activé sur
    /// le layer de l'AnimatorController (fait par PlayerAnimatorBuilder).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class PlayerFootIK : MonoBehaviour
    {
        [Tooltip("Distance du rayon au-dessus / en-dessous du pied (m).")]
        [SerializeField] private float _rayUp = 0.5f;
        [SerializeField] private float _rayDown = 0.8f;
        [Tooltip("Hauteur cheville↔semelle : le pied se pose à cette hauteur au-dessus de la surface.")]
        [SerializeField] private float _footOffset = 0.1f;
        [Tooltip("Au-delà de cette hauteur le pied est considéré 'levé' → IK relâché.")]
        [SerializeField] private float _liftThreshold = 0.18f;
        [Tooltip("Le pied ne descend pas plus bas que ça sous sa position animée (évite de clipper les rebords).")]
        [SerializeField] private float _maxStepDown = 0.12f;
        [Header("Bassin")]
        [Tooltip("Fraction de l'écart de hauteur entre les pieds dont le bassin descend (→ genoux plient).")]
        [SerializeField] private float _pelvisBend = 0.6f;
        [SerializeField] private float _pelvisSmooth = 10f;
        [SerializeField] private LayerMask _mask = ~0;

        private Animator _anim;
        private float _pelvisDrop;

        private void Awake() => _anim = GetComponent<Animator>();

        private void OnAnimatorIK(int layer)
        {
            if (_anim == null) return;

            // Bassin : si les deux pieds sont à des hauteurs différentes, on abaisse le bassin
            // proportionnellement à l'écart → les jambes plient davantage (au lieu de s'étirer).
            float lY = SurfaceY(AvatarIKGoal.LeftFoot, out bool lHit);
            float rY = SurfaceY(AvatarIKGoal.RightFoot, out bool rHit);
            float target = (lHit && rHit) ? Mathf.Abs(lY - rY) * _pelvisBend : 0f;
            _pelvisDrop = Mathf.Lerp(_pelvisDrop, target, Time.deltaTime * _pelvisSmooth);
            var bp = _anim.bodyPosition;
            bp.y -= _pelvisDrop;
            _anim.bodyPosition = bp;

            Solve(AvatarIKGoal.LeftFoot);
            Solve(AvatarIKGoal.RightFoot);
        }

        private float SurfaceY(AvatarIKGoal goal, out bool hit)
        {
            Vector3 origin = _anim.GetIKPosition(goal) + Vector3.up * _rayUp;
            if (Physics.Raycast(origin, Vector3.down, out var h, _rayUp + _rayDown, _mask, QueryTriggerInteraction.Ignore))
            { hit = true; return h.point.y; }
            hit = false; return 0f;
        }

        private void Solve(AvatarIKGoal goal)
        {
            Vector3 footPos = _anim.GetIKPosition(goal);
            Vector3 origin = footPos + Vector3.up * _rayUp;

            if (Physics.Raycast(origin, Vector3.down, out var hit, _rayUp + _rayDown, _mask, QueryTriggerInteraction.Ignore))
            {
                float above = footPos.y - hit.point.y;
                // Poids : 1 quand le pied touche/clippe la surface, 0 quand il est levé pour le pas.
                float w = above < 0f ? 1f : Mathf.Clamp01(1f - above / Mathf.Max(0.01f, _liftThreshold));

                // Cible : sur la surface, mais jamais loin sous la position animée (anti-clip de rebord).
                float targetY = Mathf.Max(hit.point.y + _footOffset, footPos.y - _maxStepDown);
                _anim.SetIKPositionWeight(goal, w);
                _anim.SetIKPosition(goal, new Vector3(footPos.x, targetY, footPos.z));

                Quaternion rot = Quaternion.FromToRotation(Vector3.up, hit.normal) * _anim.GetIKRotation(goal);
                _anim.SetIKRotationWeight(goal, w);
                _anim.SetIKRotation(goal, rot);
            }
            else
            {
                _anim.SetIKPositionWeight(goal, 0f);
                _anim.SetIKRotationWeight(goal, 0f);
            }
        }
    }
}
