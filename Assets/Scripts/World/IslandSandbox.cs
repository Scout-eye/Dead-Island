using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Bac à sable d'édition d'île : construit une île + l'eau (via <see cref="WorldSpawner"/>) au
    /// lancement de la scène, et via le menu contextuel en éditeur. Permet de prévisualiser / itérer
    /// sur la génération sans lancer une partie réseau.
    ///
    /// Posé dans la scène "IslandGenerator". En Play → île générée sous cet objet.
    /// En éditeur → clic-droit sur le composant ▸ Régénérer (l'aperçu n'est pas sauvé dans la scène).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IslandSandbox : MonoBehaviour
    {
        [SerializeField] private int _seed = 1337;
        [Range(1, 8)] [SerializeField] private int _players = 4;
        [Tooltip("En Play : tire une nouvelle seed à chaque lancement.")]
        [SerializeField] private bool _randomSeedOnPlay = true;

        private GameObject _worldRoot;

        private void Start() => Rebuild();

        [ContextMenu("Régénérer")]
        public void Rebuild()
        {
            ClearWorld();
            int seed = (_randomSeedOnPlay && Application.isPlaying) ? Random.Range(1, 999_999) : _seed;
            var world = WorldSpawner.Build(seed, _players);
            _worldRoot = world.Root;
            _worldRoot.transform.SetParent(transform, false);
            if (!Application.isPlaying) _worldRoot.hideFlags = HideFlags.DontSave; // aperçu only, pas sauvé
            Debug.Log($"[IslandSandbox] seed={seed} · joueurs={_players} · taille={world.Size:0} m.", this);
        }

        [ContextMenu("Régénérer (nouvelle seed)")]
        public void RebuildRandomSeed()
        {
            _seed = Random.Range(1, 999_999);
            Rebuild();
        }

        private void ClearWorld()
        {
            if (_worldRoot == null) return;
            if (Application.isPlaying) Destroy(_worldRoot);
            else DestroyImmediate(_worldRoot);
            _worldRoot = null;
        }
    }
}
