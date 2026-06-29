using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player
{
    /// <summary>
    /// Lecture d'input du joueur local. Les actions sont fournies par <see cref="GameControls"/>
    /// (source partagée + touches réassignables/persistées) ; ce reader ne fait QUE lire et exposer.
    ///
    /// Actif uniquement pour le joueur local (owner) ; un remote a ce composant désactivé.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        private InputAction _move, _look, _jump, _sprint, _interact;

        // --- Valeurs lues, exposées aux autres composants (API inchangée) ---
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool JumpPressedThisFrame { get; private set; }
        public bool SprintHeld { get; private set; }
        public bool InteractPressedThisFrame { get; private set; }

        private void Awake()
        {
            var gc = GameControls.EnsureExists();
            _move = gc.Move; _look = gc.Look; _jump = gc.Jump; _sprint = gc.Sprint; _interact = gc.Interact;
        }

        private void Update()
        {
            if (_move == null) return;
            Move = _move.ReadValue<Vector2>();
            Look = _look.ReadValue<Vector2>();
            JumpPressedThisFrame = _jump.WasPressedThisFrame();
            SprintHeld = _sprint.IsPressed();
            InteractPressedThisFrame = _interact.WasPressedThisFrame();
        }
    }
}
