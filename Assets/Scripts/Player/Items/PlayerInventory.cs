using System;
using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Inventaire à 3 slots. Molette = changer de slot sélectionné, clic droit = utiliser l'objet
    /// sélectionné. Input lu uniquement pour le joueur local (owner).
    ///
    /// Faible adhérence : ne touche ni au HUD ni à la main — il expose des événements + un registre
    /// statique (comme PlayerVitals) pour que HUD/main/orchestre s'y branchent.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInventory : MonoBehaviour
    {
        public const int SlotCount = 3;

        [Tooltip("Objets de départ (slot 0..2). Laisser vide = inventaire vide.")]
        [SerializeField] private ItemDefinition[] _startingItems = new ItemDefinition[SlotCount];

        private readonly ItemDefinition[] _slots = new ItemDefinition[SlotCount];
        private int _selected;

        private FirstPersonController _body;
        private PlayerInputReader _input;
        private Camera _cam;

        public int Selected => _selected;
        public ItemDefinition SelectedItem => _slots[_selected];
        public ItemDefinition GetSlot(int i) => (uint)i < SlotCount ? _slots[i] : null;
        public bool IsOwner => _body == null || _body.IsOwner;

        public event Action SlotsChanged;          // contenu d'un slot modifié
        public event Action<int> SelectionChanged;  // slot actif modifié (index)
        public event Action ItemUsed;               // un objet vient d'être utilisé (déclenche le geste)

        // Registre du joueur LOCAL, pour brancher le HUD (cf. PlayerVitals).
        public static event Action<PlayerInventory> OwnerReady;
        public static event Action OwnerGone;

        private void Awake()
        {
            _body = GetComponent<FirstPersonController>();
            _input = GetComponent<PlayerInputReader>();
            for (int i = 0; i < SlotCount; i++)
                _slots[i] = i < _startingItems.Length ? _startingItems[i] : null;
        }

        private void Start()
        {
            SlotsChanged?.Invoke();
            SelectionChanged?.Invoke(_selected);
            if (IsOwner) OwnerReady?.Invoke(this);
        }

        private void OnDisable() { if (IsOwner) OwnerGone?.Invoke(); }

        private void Update()
        {
            if (!IsOwner || _input == null) return;
            if (_body != null && !_body.enabled) return; // mort / ragdoll : pas d'inventaire

            float scroll = _input.ScrollDelta;
            if (scroll > 0.01f) Cycle(+1);
            else if (scroll < -0.01f) Cycle(-1);

            if (_input.UsePrimaryPressed) UseSelected();
            else if (_input.UseSecondaryPressed) UseSelectedSecondary();
            if (_input.DropPressed) DropSelected();
        }

        // --- API (utilisable par pickups / orchestre) ---
        public void Cycle(int dir)
        {
            _selected = ((_selected + dir) % SlotCount + SlotCount) % SlotCount;
            SelectionChanged?.Invoke(_selected);
        }

        public void SetSlot(int index, ItemDefinition item)
        {
            if ((uint)index >= SlotCount) return;
            _slots[index] = item;
            SlotsChanged?.Invoke();
            if (index == _selected) SelectionChanged?.Invoke(_selected);
        }

        /// <summary>Ajoute un objet : 1er slot libre, sinon LÂCHE l'objet en main pour faire de la place.</summary>
        public bool AddItem(ItemDefinition item)
        {
            if (item == null) return false;
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i] == null) { SetSlot(i, item); return true; }

            DropSlot(_selected);          // inventaire plein -> on jette l'objet tenu
            SetSlot(_selected, item);
            return true;
        }

        public void UseSelected()
        {
            var item = _slots[_selected];
            if (item == null) return;

            ItemUsed?.Invoke();          // geste (manger, etc.)
            if (item.Use(gameObject))    // consommé
                ClearSelected();
        }

        public void UseSelectedSecondary()
        {
            var item = _slots[_selected];
            if (item == null) return;
            if (item.UseSecondary(gameObject))
                ClearSelected();
        }

        /// <summary>Lâche l'objet sélectionné dans le monde.</summary>
        public void DropSelected() => DropSlot(_selected);

        private void DropSlot(int index)
        {
            if ((uint)index >= SlotCount || _slots[index] == null) return;

            Vector3 origin, fwd;
            if (TryGetCamera(out var cam)) { origin = cam.transform.position; fwd = cam.transform.forward; }
            else { origin = transform.position + Vector3.up * 1.2f; fwd = transform.forward; }

            WorldItem.Spawn(_slots[index], origin + fwd * 0.6f, fwd * 2.5f + Vector3.up * 0.5f);
            _slots[index] = null;
            SlotsChanged?.Invoke();
            if (index == _selected) SelectionChanged?.Invoke(_selected);
        }

        private void ClearSelected()
        {
            _slots[_selected] = null;
            SlotsChanged?.Invoke();
            SelectionChanged?.Invoke(_selected);
        }

        private bool TryGetCamera(out Camera cam)
        {
            if (_cam == null) _cam = GetComponentInChildren<Camera>(true);
            cam = _cam;
            return cam != null;
        }
    }
}
