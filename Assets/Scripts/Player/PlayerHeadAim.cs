using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Oriente la tête (et un peu le haut du corps) vers la direction REGARDÉE, via le Look-At IK de
    /// l'Animator. Marche pour le joueur local ET les distants → la tête tourne visiblement chez les
    /// autres joueurs avant que le corps pivote.
    ///
    /// À placer sur le GameObject de l'Animator (le modèle). La direction est poussée par
    /// PlayerCamera (owner) ou RemotePlayer (distant) via <see cref="SetLook"/>. Nécessite "IK Pass".
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class PlayerHeadAim : MonoBehaviour
    {
        [Tooltip("Poids global du look-at (0 = rien, 1 = total).")]
        [SerializeField] private float _weight = 0.8f;
        [Tooltip("Part du corps qui participe (0 = tête seule).")]
        [SerializeField] private float _bodyWeight = 0.25f;
        [SerializeField] private float _headWeight = 1f;
        [SerializeField] private float _smoothing = 12f;

        private Animator _anim;
        private float _yaw, _pitch;
        private float _curYaw, _curPitch;
        private bool _hasLook;

        private void Awake()
        {
            _anim = GetComponent<Animator>();
            _curYaw = transform.eulerAngles.y;
        }

        /// <summary>Pousse la direction regardée (degrés monde). Appelé par owner/remote.</summary>
        public void SetLook(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = pitch;
            _hasLook = true;
        }

        private void OnAnimatorIK(int layer)
        {
            if (_anim == null || !_hasLook) return;

            // Lissage (évite les à-coups quand la cible saute, ex. interpolation réseau).
            float k = 1f - Mathf.Exp(-_smoothing * Time.deltaTime);
            _curYaw = Mathf.LerpAngle(_curYaw, _yaw, k);
            _curPitch = Mathf.LerpAngle(_curPitch, _pitch, k);

            var head = _anim.GetBoneTransform(HumanBodyBones.Head);
            Vector3 eye = head != null ? head.position : transform.position + Vector3.up * 1.6f;
            Vector3 dir = Quaternion.Euler(_curPitch, _curYaw, 0f) * Vector3.forward;

            _anim.SetLookAtWeight(_weight, _bodyWeight, _headWeight);
            _anim.SetLookAtPosition(eye + dir * 3f);
        }
    }
}
