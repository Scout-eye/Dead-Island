using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Caméra première personne :
    ///   - Souris X -> yaw du REGARD (libre, ne tourne PAS le corps directement).
    ///   - Souris Y -> pitch.
    ///   - Le rig caméra prend la rotation MONDE du regard (découplé du corps) + head bob.
    ///
    /// Le corps suit le regard via <see cref="FirstPersonController"/> (la tête peut tourner d'un
    /// certain angle avant que le corps pivote). N'actif que pour le joueur local.
    /// </summary>
    [DefaultExecutionOrder(-20)] // avant FirstPersonController (qui lit LookYaw)
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

        private PlayerInputReader _input;
        private FirstPersonController _controller;
        private PlayerHeadAim _headAim;

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
            _headAim = GetComponentInChildren<PlayerHeadAim>();
            _yaw = transform.eulerAngles.y;
            if (_cameraRig != null) _rigBaseLocalPos = _cameraRig.localPosition;
        }

        private void OnEnable()
        {
            if (_controller == null || _controller.IsOwner) LockCursor(true);
        }

        // Ne rend le curseur QUE si c'est la caméra du joueur LOCAL qui se désactive (mort, etc.).
        // La désactivation de la caméra d'un joueur DISTANT (ConfigurePlayer) ne doit pas délocker
        // notre souris — c'était la cause de la "caméra figée jusqu'à Échap" en rejoignant.
        private void OnDisable()
        {
            if (_controller == null || _controller.IsOwner) LockCursor(false);
        }

        private void Update()
        {
            if (_controller != null && !_controller.IsOwner) return;
            if (Cursor.lockState != CursorLockMode.Locked) return; // pause : on ne tourne pas

            Vector2 look = _input != null ? _input.Look : Vector2.zero;
            _yaw += look.x * _mouseSensitivity;   // regard libre (le corps ne tourne pas ici)
            _pitch = Mathf.Clamp(_pitch - look.y * _mouseSensitivity, _minPitch, _maxPitch);
        }

        private void LateUpdate()
        {
            if (_cameraRig == null) return;
            UpdateHeadBob();
            // Position suit le corps (hauteur des yeux + bob) ; rotation = regard MONDE (découplé du corps).
            _cameraRig.localPosition = _rigBaseLocalPos + _bobOffset;
            _cameraRig.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            if (_headAim != null) _headAim.SetLook(_yaw, _pitch); // la tête (avatar) suit le regard
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
