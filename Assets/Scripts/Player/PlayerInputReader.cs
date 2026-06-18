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
        private InputAction _leftGrip;
        private InputAction _rightGrip;

        // --- Valeurs lues, exposées aux autres composants ---
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool JumpPressedThisFrame { get; private set; }
        public bool SprintHeld { get; private set; }
        public bool LeftGripHeld { get; private set; }
        public bool RightGripHeld { get; private set; }

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

            _leftGrip = new InputAction("LeftGrip", InputActionType.Button);
            _leftGrip.AddBinding("<Mouse>/leftButton");
            _leftGrip.AddBinding("<Gamepad>/leftTrigger");

            _rightGrip = new InputAction("RightGrip", InputActionType.Button);
            _rightGrip.AddBinding("<Mouse>/rightButton");
            _rightGrip.AddBinding("<Gamepad>/rightTrigger");
        }

        private void OnEnable()
        {
            _move.Enable();
            _look.Enable();
            _jump.Enable();
            _sprint.Enable();
            _leftGrip.Enable();
            _rightGrip.Enable();
        }

        private void OnDisable()
        {
            _move.Disable();
            _look.Disable();
            _jump.Disable();
            _sprint.Disable();
            _leftGrip.Disable();
            _rightGrip.Disable();
        }

        private void Update()
        {
            Move = _move.ReadValue<Vector2>();
            Look = _look.ReadValue<Vector2>();
            JumpPressedThisFrame = _jump.WasPressedThisFrame();
            SprintHeld = _sprint.IsPressed();
            LeftGripHeld = _leftGrip.IsPressed();
            RightGripHeld = _rightGrip.IsPressed();
        }

        private void OnDestroy()
        {
            _move?.Dispose();
            _look?.Dispose();
            _jump?.Dispose();
            _sprint?.Dispose();
            _leftGrip?.Dispose();
            _rightGrip?.Dispose();
        }
    }
}
