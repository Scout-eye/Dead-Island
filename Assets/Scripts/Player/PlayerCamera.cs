using UnityEngine;
using Game.Player.Ragdoll;

namespace Game.Player
{
    /// <summary>
    /// Caméra FPS procédurale.
    ///   - Souris X  -> yaw du regard (LookYaw), lu par la locomotion pour orienter le corps.
    ///   - Souris Y  -> pitch, appliqué uniquement au CameraRig (clampé).
    ///   - Head bob  -> oscillation procédurale basée sur la vélocité du bassin (zéro keyframe).
    ///   - Lean      -> légère inclinaison (roll) quand on strafe.
    ///
    /// Le CameraRig suit la position de la tête du modèle Mixamo (headBone) si elle est fournie,
    /// pour rester collé au modèle tout en gardant une rotation indépendante.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCamera : MonoBehaviour
    {
        [Header("Références")]
        [Tooltip("Transform pivot de la caméra (enfant du root). Reçoit le pitch + bob + lean.")]
        [SerializeField] private Transform _cameraRig;
        [Tooltip("Os 'Head' du Mixamo. Optionnel : si défini, le rig suit sa position.")]
        [SerializeField] private Transform _headBone;
        [SerializeField] private Vector3 _headLocalOffset = new Vector3(0f, 0.1f, 0.05f);

        [Header("Sensibilité")]
        [SerializeField] private float _mouseSensitivity = 0.12f;
        [SerializeField] private float _minPitch = -85f;
        [SerializeField] private float _maxPitch = 85f;

        [Header("Head bob")]
        [SerializeField] private float _bobFrequency = 9f;
        [SerializeField] private float _bobAmplitude = 0.05f;
        [SerializeField] private float _bobSmoothing = 10f;

        [Header("Lean")]
        [SerializeField] private float _leanAngle = 3f;
        [SerializeField] private float _leanSmoothing = 8f;

        private PlayerInputReader _input;
        private RagdollLocomotion _body;

        private float _lookYaw;
        private float _pitch;
        private float _bobTimer;
        private Vector3 _bobOffset;
        private float _currentLean;
        private Vector3 _baseLocalPos;

        /// <summary>Orientation du regard (degrés monde). Peut diverger du corps (BodyYaw).</summary>
        public float LookYaw => _lookYaw;
        public float Pitch => _pitch;
        public Transform CameraRig => _cameraRig;

        /// <summary>Force le regard depuis le réseau (remote). Owner : non utilisé.</summary>
        public void SetNetworkLook(float lookYaw, float pitch)
        {
            _lookYaw = lookYaw;
            _pitch = pitch;
        }

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _body = GetComponent<RagdollLocomotion>();
            _lookYaw = transform.eulerAngles.y;
            if (_cameraRig != null) _baseLocalPos = _cameraRig.localPosition;
        }

        private void OnEnable()
        {
            if (_body != null && _body.IsOwner)
                LockCursor(true);
        }

        private void OnDisable() => LockCursor(false);

        private void Update()
        {
            if (_body == null || !_body.IsOwner) return;

            // Le curseur est géré par le menu pause (Échap). Si déverrouillé (en pause), on ne tourne pas.
            if (Cursor.lockState != CursorLockMode.Locked) return;

            Vector2 look = _input != null ? _input.Look : Vector2.zero;
            _lookYaw += look.x * _mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - look.y * _mouseSensitivity, _minPitch, _maxPitch);
        }

        private void LateUpdate()
        {
            if (_cameraRig == null) return;

            // 2) Head bob basé sur la vitesse horizontale réelle du Rigidbody.
            UpdateHeadBob();

            // 3) Lean basé sur le strafe.
            UpdateLean();

            // 1) Position : suit la tête du modèle si dispo, sinon reste à sa position de base.
            //    Le bob est ajouté par-dessus (jamais accumulé).
            if (_headBone != null)
                _cameraRig.position = _headBone.position
                                      + _cameraRig.parent.TransformVector(_headLocalOffset)
                                      + _cameraRig.parent.TransformVector(_bobOffset);
            else
                _cameraRig.localPosition = _baseLocalPos + _bobOffset;

            // 4) Le rig suit la tête physique du ragdoll (position) mais garde une rotation
            //    INDÉPENDANTE en monde : le regard ne subit pas le ballottement du corps.
            _cameraRig.rotation = Quaternion.Euler(_pitch, _lookYaw, _currentLean);
        }

        private void UpdateHeadBob()
        {
            float speed = 0f;
            bool grounded = false;
            if (_body != null)
            {
                Vector3 vel = _body.CurrentVelocity;
                vel.y = 0f;
                speed = vel.magnitude;
                grounded = _body.IsGrounded;
            }

            Vector3 target = Vector3.zero;
            if (grounded && speed > 0.5f)
            {
                _bobTimer += Time.deltaTime * _bobFrequency * Mathf.Clamp01(speed / 6f);
                float vertical = Mathf.Sin(_bobTimer) * _bobAmplitude;
                float horizontal = Mathf.Cos(_bobTimer * 0.5f) * _bobAmplitude * 0.5f;
                target = new Vector3(horizontal, vertical, 0f);
            }
            else
            {
                _bobTimer = 0f;
            }

            _bobOffset = Vector3.Lerp(_bobOffset, target, Time.deltaTime * _bobSmoothing);
        }

        private void UpdateLean()
        {
            float strafe = _input != null ? _input.Move.x : 0f;
            float targetLean = -strafe * _leanAngle;
            _currentLean = Mathf.Lerp(_currentLean, targetLean, Time.deltaTime * _leanSmoothing);
        }

        private static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
