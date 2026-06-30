using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Objet consommable (pomme, gourde…) : restaure faim / soif / vie à l'utilisation, puis disparaît.
    /// Crée un asset via : clic droit ▸ Create ▸ Dead Island ▸ Consumable Item.
    /// </summary>
    [CreateAssetMenu(menuName = "Dead Island/Consumable Item", fileName = "Consumable")]
    public sealed class ConsumableItem : ItemDefinition
    {
        [Header("Restaure à la consommation")]
        [SerializeField] private float _hunger = 30f;
        [SerializeField] private float _thirst = 0f;
        [SerializeField] private float _health = 0f;

        public override bool Use(GameObject user)
        {
            if (user == null || !user.TryGetComponent<PlayerVitals>(out var vitals)) return false;
            if (vitals.IsDead) return false;

            if (_hunger != 0f) vitals.Eat(_hunger);
            if (_thirst != 0f) vitals.Drink(_thirst);
            if (_health != 0f) vitals.Heal(_health);
            return true; // consommé → retiré du slot
        }
    }
}
