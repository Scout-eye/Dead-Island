using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Pilote l'Animator de l'avatar visible (blend idle / marche / course) à partir de la vitesse
    /// du <see cref="FirstPersonController"/>. Les clips jouent EN PLACE (le CharacterController
    /// déplace le corps). Marche pour le joueur local ET les distants (vitesse réseau).
    ///
    /// Responsabilité unique : l'animation cosmétique. Aucune autre dépendance.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private Animator _animator;
        [Tooltip("Lissage du paramètre de vitesse (évite les à-coups de blend).")]
        [SerializeField] private float _smoothing = 12f;

        private FirstPersonController _controller;
        private PlayerInputReader _input;
        private float _speed;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int CrouchHash = Animator.StringToHash("Crouch");
        private static readonly int InteractHash = Animator.StringToHash("Interact");

        private void Awake()
        {
            _controller = GetComponent<FirstPersonController>();
            _input = GetComponent<PlayerInputReader>();
        }

        private void Update()
        {
            if (_animator == null || _controller == null) return;

            Vector3 v = _controller.CurrentVelocity; v.y = 0f;
            _speed = Mathf.Lerp(_speed, v.magnitude, Time.deltaTime * _smoothing);
            _animator.SetFloat(SpeedHash, _speed);
            _animator.SetBool(GroundedHash, _controller.IsGrounded);
            _animator.SetBool(CrouchHash, _controller.IsCrouching);

            // Geste d'interaction (le système d'interaction gameplay est séparé).
            if (_controller.IsOwner && _input != null && _input.InteractPressedThisFrame)
                _animator.SetTrigger(InteractHash);
        }
    }
}
