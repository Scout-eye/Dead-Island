using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player
{
    /// <summary>
    /// Mode spectateur : à la mort du joueur local, suit un autre joueur VIVANT (3e personne).
    /// Clic gauche = joueur suivant, clic droit = précédent. Lit le registre <see cref="PlayerVitals.All"/>
    /// (aucune référence directe aux autres joueurs). Activé par PlayerDeath uniquement pour le owner.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpectatorController : MonoBehaviour
    {
        [SerializeField] private Vector3 _offset = new Vector3(0f, 3f, -5f);
        [SerializeField] private float _followSmoothing = 8f;
        [SerializeField] private float _lookAtHeight = 1.5f;

        private Camera _cam;
        private Transform _target;
        private bool _active;

        public void Begin()
        {
            if (_active) return;
            _active = true;

            // Coupe la caméra/écoute du joueur mort, crée une caméra spectateur indépendante.
            foreach (var c in GetComponentsInChildren<Camera>(true)) c.enabled = false;
            foreach (var a in GetComponentsInChildren<AudioListener>(true)) a.enabled = false;

            var go = new GameObject("SpectatorCam");
            _cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();

            PickTarget(0);
        }

        private void OnDestroy()
        {
            if (_cam != null) Destroy(_cam.gameObject);
        }

        private void Update()
        {
            if (!_active) return;
            var m = Mouse.current;
            if (m == null) return;
            if (m.leftButton.wasPressedThisFrame) PickTarget(1);
            else if (m.rightButton.wasPressedThisFrame) PickTarget(-1);
        }

        private void LateUpdate()
        {
            if (!_active || _cam == null) return;
            if (_target == null) { PickTarget(0); if (_target == null) return; }

            Quaternion yaw = Quaternion.Euler(0f, _target.eulerAngles.y, 0f);
            Vector3 desired = _target.position + yaw * _offset;
            _cam.transform.position = Vector3.Lerp(_cam.transform.position, desired, Time.deltaTime * _followSmoothing);
            _cam.transform.LookAt(_target.position + Vector3.up * _lookAtHeight);
        }

        private void PickTarget(int dir)
        {
            var living = Living();
            if (living.Count == 0) { _target = null; return; }
            int cur = _target != null ? living.IndexOf(_target) : -1;
            int idx = cur < 0 ? 0 : ((cur + dir) % living.Count + living.Count) % living.Count;
            _target = living[idx];
        }

        private static List<Transform> Living()
        {
            var list = new List<Transform>();
            foreach (var v in PlayerVitals.All)
                if (v != null && !v.IsDead) list.Add(v.transform);
            return list;
        }
    }
}
