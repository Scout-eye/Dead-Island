using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Définition d'un objet (ScriptableObject = data pure, réutilisable). Le MODÈLE affiché en main
    /// est un simple champ <see cref="ViewPrefab"/> : on le remplace par n'importe quel prefab (sphère
    /// de test puis vrai modèle) SANS toucher au code.
    ///
    /// Le comportement d'utilisation est défini par les sous-classes (ex. <see cref="ConsumableItem"/>).
    /// Faible adhérence : ne connaît ni l'inventaire, ni le HUD, ni la main.
    /// </summary>
    public abstract class ItemDefinition : ScriptableObject
    {
        [SerializeField] private string _displayName = "Objet";
        [SerializeField] private Sprite _icon;
        [Tooltip("Modèle affiché dans la main droite quand l'objet est sélectionné (swappable sans code).")]
        [SerializeField] private GameObject _viewPrefab;

        public string DisplayName => _displayName;
        public Sprite Icon => _icon;
        public GameObject ViewPrefab => _viewPrefab;

        /// <summary>Usage PRINCIPAL (clic gauche), ex. manger. Retourne true si consommé (retiré du slot).</summary>
        public abstract bool Use(GameObject user);

        /// <summary>Usage SPÉCIAL (clic droit), propre à l'objet (ex. viser, lancer…). Rien par défaut.</summary>
        public virtual bool UseSecondary(GameObject user) => false;
    }
}
