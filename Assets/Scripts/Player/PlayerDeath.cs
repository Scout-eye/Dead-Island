using UnityEngine;
using Game.Player.Ragdoll;

namespace Game.Player
{
    /// <summary>
    /// Réaction à la mort (sur le même GameObject que PlayerVitals) : coupe la "musculature" de
    /// l'active ragdoll (il s'effondre en ragdoll passif) et désactive les contrôleurs ; pour le
    /// joueur LOCAL bascule en mode spectateur. Glue minimale — ne référence que des composants
    /// locaux, communique par l'événement PlayerVitals.Died.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDeath : MonoBehaviour
    {
        private PlayerVitals _vitals;
        private ActiveRagdoll _ragdoll;
        private SpectatorController _spectator;

        private void Awake()
        {
            _vitals = GetComponent<PlayerVitals>();
            _ragdoll = GetComponent<ActiveRagdoll>();
            _spectator = GetComponent<SpectatorController>();
        }

        private void OnEnable() { if (_vitals != null) _vitals.Died += HandleDied; }
        private void OnDisable() { if (_vitals != null) _vitals.Died -= HandleDied; }

        private void HandleDied()
        {
            // Le corps est DÉJÀ physique : couper les moteurs suffit à le faire s'effondrer.
            if (_ragdoll != null) _ragdoll.SetMotorsEnabled(false);
            Disable<RagdollLocomotion>();
            Disable<RagdollBalance>();
            Disable<RagdollPoseDriver>();
            Disable<HandReach>();
            Disable<PlayerInputReader>();

            // Seul le joueur local passe en spectateur (suit les autres joueurs vivants).
            if (_vitals != null && _vitals.IsOwner && _spectator != null)
                _spectator.Begin();
        }

        private void Disable<T>() where T : Behaviour
        {
            if (TryGetComponent<T>(out var c)) c.enabled = false;
        }
    }
}
