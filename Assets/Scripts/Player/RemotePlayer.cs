using System.Collections.Generic;
using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Représentation d'un joueur distant : reçoit des PlayerState (via NetworkManager),
    /// les met dans un buffer et INTERPOLE position / rotation / cibles d'IK avec un léger
    /// retard (interpolation buffer), puis réinjecte le résultat dans les composants joueur.
    ///
    /// Aucune physique ni input : le corps est kinematic (SetOwner(false)). L'IK des bras/jambes
    /// est recalculée localement par PlayerHands / PlayerProceduralAnimator à partir des cibles
    /// et de la vitesse réseau (PlayerBody.CurrentVelocity).
    ///
    /// Composant présent sur le prefab joueur, activé uniquement pour les remotes.
    /// </summary>
    [DefaultExecutionOrder(-50)] // avant animator (-10) et hands (0)
    [DisallowMultipleComponent]
    public sealed class RemotePlayer : MonoBehaviour
    {
        [Tooltip("Retard d'interpolation (s). ~2-3 paquets à 20 Hz.")]
        [SerializeField] private float _interpolationDelay = 0.12f;
        [SerializeField] private int _maxBuffer = 16;

        private PlayerBody _body;
        private PlayerCamera _camera;
        private PlayerHands _hands;

        private readonly List<Snapshot> _buffer = new List<Snapshot>();

        private void Awake()
        {
            _body = GetComponent<PlayerBody>();
            _camera = GetComponent<PlayerCamera>();
            _hands = GetComponent<PlayerHands>();
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
                _body.ApplyNetworkTransform(s.Position, s.Yaw, s.Velocity);
            if (_camera != null)
                _camera.SetNetworkLook(s.Yaw, s.Pitch); // pas de head-yaw indépendant synchronisé pour l'instant
            if (_hands != null)
                _hands.SetNetworkHands(s.LeftHandTarget, s.RightHandTarget);
        }

        private static PlayerState Interpolate(PlayerState a, PlayerState b, float t)
        {
            return new PlayerState
            {
                Tick = b.Tick,
                Position = Vector3.Lerp(a.Position, b.Position, t),
                Yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, t),
                Pitch = Mathf.LerpAngle(a.Pitch, b.Pitch, t),
                Velocity = Vector3.Lerp(a.Velocity, b.Velocity, t),
                LeftHandTarget = Vector3.Lerp(a.LeftHandTarget, b.LeftHandTarget, t),
                RightHandTarget = Vector3.Lerp(a.RightHandTarget, b.RightHandTarget, t)
            };
        }

        private struct Snapshot
        {
            public float Time;
            public PlayerState State;
        }
    }
}
