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
        private SpectatorController _spectator;
        private PlayerRagdoll _ragdoll;

        private void Awake()
        {
            _vitals = GetComponent<PlayerVitals>();
            _spectator = GetComponent<SpectatorController>();
            _ragdoll = GetComponent<PlayerRagdoll>();
        }

        private void OnEnable() { if (_vitals != null) _vitals.Died += HandleDied; }
        private void OnDisable() { if (_vitals != null) _vitals.Died -= HandleDied; }

        private void HandleDied()
        {
            // Ragdoll : il coupe lui-même Animator + controller + caméra + input + CharacterController.
            if (_ragdoll != null) _ragdoll.Activate();
            else { Disable<FirstPersonController>(); Disable<PlayerCamera>(); Disable<PlayerInputReader>(); }

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
