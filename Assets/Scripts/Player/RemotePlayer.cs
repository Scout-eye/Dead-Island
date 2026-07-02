using System.Collections.Generic;
using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Représentation d'un joueur distant : reçoit des PlayerState (via NetworkManager), les met dans
    /// un buffer et INTERPOLE position / rotation / vitesse avec un léger retard, puis réinjecte le
    /// résultat dans le controller (transform imposé) et la caméra/animation.
    ///
    /// Aucun input ni physique locale : le CharacterController est désactivé (SetOwner(false)) et le
    /// transform est déplacé par interpolation ; l'avatar s'anime depuis la vitesse réseau.
    /// Composant présent sur le prefab joueur, activé uniquement pour les remotes.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class RemotePlayer : MonoBehaviour
    {
        [Tooltip("Retard d'interpolation (s). ~2-3 paquets à 20 Hz.")]
        [SerializeField] private float _interpolationDelay = 0.12f;
        [SerializeField] private int _maxBuffer = 16;

        private FirstPersonController _body;
        private PlayerCamera _camera;
        private PlayerVitals _vitals;
        private PlayerHeadAim _headAim;
        private PlayerHandItem _handItem;
        private PlayerHoldIK _holdIK;
        private byte _shownHeld = 255; // pour ne réagir qu'au changement d'objet tenu

        private readonly List<Snapshot> _buffer = new List<Snapshot>();

        private void Awake()
        {
            _body = GetComponent<FirstPersonController>();
            _camera = GetComponent<PlayerCamera>();
            _vitals = GetComponent<PlayerVitals>();
            _headAim = GetComponentInChildren<PlayerHeadAim>();
            _handItem = GetComponent<PlayerHandItem>();
            _holdIK = GetComponentInChildren<PlayerHoldIK>();
        }

        /// <summary>Appelé par NetworkManager à chaque state reçu pour ce joueur.</summary>
        public void PushState(PlayerState state)
        {
            _buffer.Add(new Snapshot { Time = Time.time, State = state });
            if (_buffer.Count > _maxBuffer) _buffer.RemoveAt(0);
        }

        private void Update()
        {
            if (_buffer.Count == 0) return;

            float renderTime = Time.time - _interpolationDelay;

            // Cherche les deux snapshots encadrant renderTime.
            Snapshot older = _buffer[0];
            Snapshot newer = _buffer[_buffer.Count - 1];
            bool found = false;
            for (int i = 0; i < _buffer.Count - 1; i++)
            {
                if (_buffer[i].Time <= renderTime && _buffer[i + 1].Time >= renderTime)
                {
                    older = _buffer[i];
                    newer = _buffer[i + 1];
                    found = true;
                    break;
                }
            }

            PlayerState s;
            if (found)
            {
                float span = newer.Time - older.Time;
                float t = span > 1e-4f ? Mathf.Clamp01((renderTime - older.Time) / span) : 0f;
                s = Interpolate(older.State, newer.State, t);
            }
            else
            {
                // Pas encore assez de données ou retard : on prend le plus récent.
                s = newer.State;
            }

            Apply(s);

            // Purge les vieux snapshots devenus inutiles.
            while (_buffer.Count > 2 && _buffer[1].Time < renderTime)
                _buffer.RemoveAt(0);
        }

        private void Apply(PlayerState s)
        {
            if (_body != null)
                _body.ApplyNetworkTransform(s.Position, s.Yaw, s.Velocity, s.Grounded, s.Swimming);
            if (_camera != null)
                _camera.SetNetworkLook(s.LookYaw, s.Pitch);
            if (_headAim != null)
                _headAim.SetLook(s.LookYaw, s.Pitch); // la tête tourne vers le regard du joueur distant
            if (_vitals != null)
                _vitals.SetDeadFromNetwork(s.Dead);

            // Objet tenu : on n'agit qu'au changement (évite de respawn le modèle chaque frame).
            if (s.HeldItem != _shownHeld)
            {
                _shownHeld = s.HeldItem;
                var item = ItemDatabase.FromNetId(s.HeldItem);
                if (_handItem != null) _handItem.SetNetworkItem(item); // sur la main
                if (_holdIK != null) _holdIK.SetHolding(item != null);  // coude plié
            }
        }

        private static PlayerState Interpolate(PlayerState a, PlayerState b, float t)
        {
            return new PlayerState
            {
                Tick = b.Tick,
                Position = Vector3.Lerp(a.Position, b.Position, t),
                Yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, t),
                LookYaw = Mathf.LerpAngle(a.LookYaw, b.LookYaw, t),
                Pitch = Mathf.LerpAngle(a.Pitch, b.Pitch, t),
                Velocity = Vector3.Lerp(a.Velocity, b.Velocity, t),
                Dead = b.Dead,
                Grounded = b.Grounded,
                Swimming = b.Swimming,
                HeldItem = b.HeldItem
            };
        }

        private struct Snapshot
        {
            public float Time;
            public PlayerState State;
        }
    }
}
