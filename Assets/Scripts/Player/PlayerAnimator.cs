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
        private float _moveX, _moveZ;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int MoveXHash = Animator.StringToHash("MoveX"); // strafe local (m/s)
        private static readonly int MoveZHash = Animator.StringToHash("MoveZ"); // avant/arrière local (m/s)
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
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
            // Vitesse en repère LOCAL : X = strafe, Z = avant/arrière (pour le blend tree 2D directionnel).
            Vector3 local = transform.InverseTransformDirection(v);
            float k = Time.deltaTime * _smoothing;
            _speed = Mathf.Lerp(_speed, v.magnitude, k);
            _moveX = Mathf.Lerp(_moveX, local.x, k);
            _moveZ = Mathf.Lerp(_moveZ, local.z, k);

            _animator.SetFloat(SpeedHash, _speed);
            _animator.SetFloat(MoveXHash, _moveX);
            _animator.SetFloat(MoveZHash, _moveZ);
            _animator.SetBool(GroundedHash, _controller.IsGrounded);

            // Saut : déclenché à l'instant T (clip part tout de suite, pas en retard).
            if (_controller.ConsumeJumped()) _animator.SetTrigger(JumpHash);

            // Geste d'interaction (le système d'interaction gameplay est séparé).
            if (_controller.IsOwner && _input != null && _input.InteractPressedThisFrame)
                _animator.SetTrigger(InteractHash);
        }
    }
}
