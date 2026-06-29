using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player
{
    /// <summary>
    /// Centralise toute la lecture d'input (New Input System).
    /// Les actions sont définies en code pour rester 100% C# et sans asset .inputactions.
    ///
    /// N'est actif que pour le joueur local (owner). Un joueur distant (RemotePlayer, étape 2)
    /// n'aura jamais ce composant activé : ses états viennent du réseau.
    ///
    /// Convention : ce reader ne fait QUE lire. Aucune logique de gameplay ici.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        private InputAction _move;
        private InputAction _look;
        private InputAction _jump;
        private InputAction _sprint;
        private InputAction _crouch;
        private InputAction _interact;

        // --- Valeurs lues, exposées aux autres composants ---
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool JumpPressedThisFrame { get; private set; }
        public bool SprintHeld { get; private set; }
        public bool CrouchHeld { get; private set; }
        public bool InteractPressedThisFrame { get; private set; }

        private void Awake()
        {
            _move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _move.AddBinding("<Gamepad>/leftStick");

            _look = new InputAction("Look", InputActionType.Value, expectedControlType: "Vector2");
            _look.AddBinding("<Mouse>/delta");
            _look.AddBinding("<Gamepad>/rightStick").WithProcessor("scaleVector2(x=8,y=8)");

            _jump = new InputAction("Jump", InputActionType.Button);
            _jump.AddBinding("<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");

            _sprint = new InputAction("Sprint", InputActionType.Button);
            _sprint.AddBinding("<Keyboard>/leftShift");
            _sprint.AddBinding("<Gamepad>/leftStickPress");

            _crouch = new InputAction("Crouch", InputActionType.Button);
            _crouch.AddBinding("<Keyboard>/leftCtrl");
            _crouch.AddBinding("<Keyboard>/c");
            _crouch.AddBinding("<Gamepad>/buttonEast");

            _interact = new InputAction("Interact", InputActionType.Button);
            _interact.AddBinding("<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonWest");
        }

        private void OnEnable()
        {
            _move.Enable();
            _look.Enable();
            _jump.Enable();
            _sprint.Enable();
            _crouch.Enable();
            _interact.Enable();
        }

        private void OnDisable()
        {
            _move.Disable();
            _look.Disable();
            _jump.Disable();
            _sprint.Disable();
            _crouch.Disable();
            _interact.Disable();
        }

        private void Update()
        {
            Move = _move.ReadValue<Vector2>();
            Look = _look.ReadValue<Vector2>();
            JumpPressedThisFrame = _jump.WasPressedThisFrame();
            SprintHeld = _sprint.IsPressed();
            CrouchHeld = _crouch.IsPressed();
            InteractPressedThisFrame = _interact.WasPressedThisFrame();
        }

        private void OnDestroy()
        {
            _move?.Dispose();
            _look?.Dispose();
            _jump?.Dispose();
            _sprint?.Dispose();
            _crouch?.Dispose();
            _interact?.Dispose();
        }
    }
}
