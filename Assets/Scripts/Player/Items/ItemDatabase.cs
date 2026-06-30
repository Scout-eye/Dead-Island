using System.Collections.Generic;
using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Registre DÉTERMINISTE de tous les items (chargés depuis Resources/Items, triés par nom).
    /// Permet de mapper un <see cref="ItemDefinition"/> vers un petit id réseau (et inversement) :
    /// les deux clients chargent les mêmes assets dans le même ordre → mêmes ids.
    ///
    /// Id réseau : 0 = aucun objet ; sinon index+1 (max 254 items).
    /// </summary>
    public static class ItemDatabase
    {
        private static ItemDefinition[] _items;
        private static Dictionary<ItemDefinition, int> _ids;

        private static void EnsureLoaded()
        {
            if (_items != null) return;
            _items = Resources.LoadAll<ItemDefinition>("Items");
            System.Array.Sort(_items, (a, b) => string.CompareOrdinal(a.name, b.name));
            _ids = new Dictionary<ItemDefinition, int>(_items.Length);
            for (int i = 0; i < _items.Length; i++) _ids[_items[i]] = i;
        }

        public static byte GetNetId(ItemDefinition item)
        {
            if (item == null) return 0;
            EnsureLoaded();
            return _ids.TryGetValue(item, out int i) ? (byte)(i + 1) : (byte)0;
        }

        public static ItemDefinition FromNetId(byte id)
        {
            if (id == 0) return null;
            EnsureLoaded();
            int i = id - 1;
            return (i >= 0 && i < _items.Length) ? _items[i] : null;
        }
    }
}
