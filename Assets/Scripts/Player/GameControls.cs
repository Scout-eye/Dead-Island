using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player
{
    /// <summary>
    /// Source UNIQUE et partagée des actions d'input (New Input System), pour que le joueur
    /// (PlayerInputReader) ET le menu de configuration utilisent exactement les mêmes touches.
    /// Persiste les réassignations (binding overrides) dans PlayerPrefs.
    ///
    /// Singleton DontDestroyOnLoad. Faible adhérence : ne connaît rien du gameplay, expose juste
    /// les actions + la liste réassignable + save/load/reset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameControls : MonoBehaviour
    {
        private const string PrefsKey = "di_bindings";

        public static GameControls Instance { get; private set; }

        public InputAction Move { get; private set; }
        public InputAction Look { get; private set; }
        public InputAction Jump { get; private set; }
        public InputAction Sprint { get; private set; }
        public InputAction Interact { get; private set; }
        public InputAction Scroll { get; private set; }       // molette : changer de slot
        public InputAction UsePrimary { get; private set; }   // clic gauche : utiliser l'objet (manger…)
        public InputAction UseSecondary { get; private set; } // clic droit : usage spécial de l'objet
        public InputAction Drop { get; private set; }         // lâcher l'objet en main

        /// <summary>Une touche réassignable : libellé + action + index du binding concerné.</summary>
        public sealed class Entry
        {
            public string Label;
            public InputAction Action;
            public int BindingIndex;
        }

        public IReadOnlyList<Entry> Rebindable => _rebindable;
        private readonly List<Entry> _rebindable = new List<Entry>();

        private InputActionMap _map;

        public static GameControls EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("GameControls");
            return go.AddComponent<GameControls>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            BuildActions();
            LoadOverrides();
            _map.Enable();
        }

        private void BuildActions()
        {
            _map = new InputActionMap("Player");

            Move = _map.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector")      // index 0 = composite, 1..4 = directions
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            Move.AddBinding("<Gamepad>/leftStick");

            Look = _map.AddAction("Look", InputActionType.Value);
            Look.AddBinding("<Mouse>/delta");
            Look.AddBinding("<Gamepad>/rightStick").WithProcessor("scaleVector2(x=8,y=8)");

            Jump = _map.AddAction("Jump", InputActionType.Button);
            Jump.AddBinding("<Keyboard>/space");      // index 0
            Jump.AddBinding("<Gamepad>/buttonSouth");

            Sprint = _map.AddAction("Sprint", InputActionType.Button);
            Sprint.AddBinding("<Keyboard>/leftShift"); // index 0
            Sprint.AddBinding("<Gamepad>/leftStickPress");

            Interact = _map.AddAction("Interact", InputActionType.Button);
            Interact.AddBinding("<Keyboard>/e");       // index 0
            Interact.AddBinding("<Gamepad>/buttonWest");

            Scroll = _map.AddAction("Scroll", InputActionType.Value);
            Scroll.AddBinding("<Mouse>/scroll/y");

            UsePrimary = _map.AddAction("UsePrimary", InputActionType.Button);
            UsePrimary.AddBinding("<Mouse>/leftButton");
            UsePrimary.AddBinding("<Gamepad>/rightTrigger");

            UseSecondary = _map.AddAction("UseSecondary", InputActionType.Button);
            UseSecondary.AddBinding("<Mouse>/rightButton");
            UseSecondary.AddBinding("<Gamepad>/leftTrigger");

            Drop = _map.AddAction("Drop", InputActionType.Button);
            Drop.AddBinding("<Keyboard>/g");
            Drop.AddBinding("<Gamepad>/dpad/down");

            // Touches réassignables (clavier) exposées au menu.
            _rebindable.Add(new Entry { Label = "Avancer", Action = Move, BindingIndex = 1 });
            _rebindable.Add(new Entry { Label = "Reculer", Action = Move, BindingIndex = 2 });
            _rebindable.Add(new Entry { Label = "Gauche", Action = Move, BindingIndex = 3 });
            _rebindable.Add(new Entry { Label = "Droite", Action = Move, BindingIndex = 4 });
            _rebindable.Add(new Entry { Label = "Sauter", Action = Jump, BindingIndex = 0 });
            _rebindable.Add(new Entry { Label = "Courir", Action = Sprint, BindingIndex = 0 });
            _rebindable.Add(new Entry { Label = "Interagir", Action = Interact, BindingIndex = 0 });
            _rebindable.Add(new Entry { Label = "Lâcher", Action = Drop, BindingIndex = 0 });
        }

        /// <summary>Libellé lisible de la touche actuelle (ex. "Z", "Espace").</summary>
        public static string DisplayKey(Entry e) =>
            e.Action.GetBindingDisplayString(e.BindingIndex, InputBinding.DisplayStringOptions.DontUseShortDisplayNames);

        public void SaveOverrides() => PlayerPrefs.SetString(PrefsKey, _map.SaveBindingOverridesAsJson());

        private void LoadOverrides()
        {
            var json = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json)) _map.LoadBindingOverridesFromJson(json);
        }

        public void ResetToDefaults()
        {
            _map.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }
    }
}
