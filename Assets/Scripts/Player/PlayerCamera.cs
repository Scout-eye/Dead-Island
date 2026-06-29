using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Caméra première personne classique :
    ///   - Souris X -> yaw, appliqué au CORPS (le joueur tourne sur lui-même).
    ///   - Souris Y -> pitch, appliqué uniquement au CameraRig (clampé).
    ///   - Head bob léger basé sur la vitesse de déplacement (zéro keyframe).
    ///
    /// Responsabilité unique : regarder/tourner. Le déplacement est géré par
    /// <see cref="FirstPersonController"/>. N'actif que pour le joueur local.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCamera : MonoBehaviour
    {
        [Header("Références")]
        [Tooltip("Pivot caméra (enfant, à hauteur des yeux). Reçoit le pitch + le bob.")]
        [SerializeField] private Transform _cameraRig;

        [Header("Sensibilité")]
        [SerializeField] private float _mouseSensitivity = 0.12f;
        [SerializeField] private float _minPitch = -85f;
        [SerializeField] private float _maxPitch = 85f;

        [Header("Head bob")]
        [SerializeField] private float _bobFrequency = 9f;
        [SerializeField] private float _bobAmplitude = 0.045f;
        [SerializeField] private float _bobSmoothing = 10f;

        [Header("Accroupi")]
        [Tooltip("De combien l'œil descend quand on est accroupi (m).")]
        [SerializeField] private float _crouchEyeDrop = 0.6f;

        private PlayerInputReader _input;
        private FirstPersonController _controller;

        private float _yaw;
        private float _pitch;
        private float _bobTimer;
        private Vector3 _bobOffset;
        private Vector3 _rigBaseLocalPos;

        /// <summary>Orientation horizontale du regard (= du corps en FPS).</summary>
        public float LookYaw => _yaw;
        public float Pitch => _pitch;
        public Transform CameraRig => _cameraRig;

        /// <summary>Remote : impose le regard reçu du réseau (pour l'inclinaison de la tête distante).</summary>
        public void SetNetworkLook(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = pitch;
        }

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _controller = GetComponent<FirstPersonController>();
            _yaw = transform.eulerAngles.y;
            if (_cameraRig != null) _rigBaseLocalPos = _cameraRig.localPosition;
        }

        private void OnEnable()
        {
            if (_controller == null || _controller.IsOwner) LockCursor(true);
        }

        private void OnDisable() => LockCursor(false);

        private void Update()
        {
            if (_controller != null && !_controller.IsOwner) return;
            if (Cursor.lockState != CursorLockMode.Locked) return; // pause : on ne tourne pas

            Vector2 look = _input != null ? _input.Look : Vector2.zero;
            _yaw += look.x * _mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - look.y * _mouseSensitivity, _minPitch, _maxPitch);

            // Le yaw tourne le CORPS (le controller se déplace relatif à transform.forward).
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        private void LateUpdate()
        {
            if (_cameraRig == null) return;
            UpdateHeadBob();
            float crouch = _controller != null ? _controller.CrouchFactor : 0f;
            _cameraRig.localPosition = _rigBaseLocalPos + _bobOffset - Vector3.up * (_crouchEyeDrop * crouch);
            _cameraRig.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void UpdateHeadBob()
        {
            float speed = 0f;
            bool grounded = false;
            if (_controller != null)
            {
                Vector3 v = _controller.CurrentVelocity; v.y = 0f;
                speed = v.magnitude;
                grounded = _controller.IsGrounded;
            }

            Vector3 target = Vector3.zero;
            if (grounded && speed > 0.5f)
            {
                _bobTimer += Time.deltaTime * _bobFrequency * Mathf.Clamp01(speed / 6f);
                target = new Vector3(Mathf.Cos(_bobTimer * 0.5f) * _bobAmplitude * 0.5f,
                                     Mathf.Sin(_bobTimer) * _bobAmplitude, 0f);
            }
            else
            {
                _bobTimer = 0f;
            }
            _bobOffset = Vector3.Lerp(_bobOffset, target, Time.deltaTime * _bobSmoothing);
        }

        private static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
