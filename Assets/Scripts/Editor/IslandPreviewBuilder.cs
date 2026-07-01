using Game.World;
using UnityEditor;
using UnityEngine;

namespace Game.World.EditorTools
{
    /// <summary>
    /// Outil éditeur : génère une île + eau dans la scène ouverte pour prévisualiser/itérer
    /// rapidement (nouvelle seed à chaque clic), sans lancer une partie réseau.
    ///
    /// Sélectionne l'île → tu peux régler les paramètres dans l'inspecteur, puis clic droit sur le
    /// composant ▸ "Régénérer (test)" pour rejouer la même seed avec tes réglages.
    ///
    /// Menu : Tools ▸ Dead Island ▸ World.
    /// </summary>
    public static class IslandPreviewBuilder
    {
        private const string PreviewName = "Island Preview";

        [MenuItem("Tools/Dead Island/World/Preview Island (nouvelle seed)")]
        public static void Preview()
        {
            Clear();

            int seed = Random.Range(1, 999_999);
            int players = 4;
            var world = WorldSpawner.Build(seed, players);
            world.Root.name = PreviewName;

            Selection.activeGameObject = world.Island.gameObject;
            EditorGUIUtility.PingObject(world.Island.gameObject);
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

            Debug.Log($"[IslandPreview] seed={seed} · joueurs={players} · taille={world.Size:0} m. " +
                      "Règle les paramètres puis clic-droit sur IslandGenerator ▸ Régénérer (test).", world.Island);
        }

        [MenuItem("Tools/Dead Island/World/Clear Island Preview")]
        public static void Clear()
        {
            var existing = GameObject.Find(PreviewName);
            if (existing != null) Object.DestroyImmediate(existing);
        }
    }
}
