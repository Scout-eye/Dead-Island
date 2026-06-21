using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Réaction à la mort (sur le même GameObject que PlayerVitals) : effondre le corps (ragdoll),
    /// et pour le joueur LOCAL bascule en mode spectateur. Glue minimale — ne référence que des
    /// composants locaux, communique par l'événement PlayerVitals.Died.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDeath : MonoBehaviour
    {
        private PlayerVitals _vitals;
        private PlayerRagdoll _ragdoll;
        private SpectatorController _spectator;

        private void Awake()
        {
            _vitals = GetComponent<PlayerVitals>();
            _ragdoll = GetComponent<PlayerRagdoll>();
            _spectator = GetComponent<SpectatorController>();
        }

        private void OnEnable() { if (_vitals != null) _vitals.Died += HandleDied; }
        private void OnDisable() { if (_vitals != null) _vitals.Died -= HandleDied; }

        private void HandleDied()
        {
            if (_ragdoll != null) _ragdoll.Activate();

            // Seul le joueur local passe en spectateur (suit les autres joueurs vivants).
            if (_vitals != null && _vitals.IsOwner && _spectator != null)
                _spectator.Begin();
        }
    }
}
