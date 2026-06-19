using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Construit le monde au runtime : île procédurale (depuis la seed) + plan d'eau, et calcule
    /// les points de spawn sur la plage. Déterministe : même (seed, players) => même île sur tous
    /// les clients.
    /// </summary>
    public static class WorldSpawner
    {
        public struct World
        {
            public GameObject Root;   // parent de l'île + l'eau (détruire pour tout nettoyer)
            public IslandGenerator Island;
            public Vector3 Center;
            public float Size;
        }

        public static World Build(int seed, int players)
        {
            var root = new GameObject("World");

            var islandGo = new GameObject("Island");
            islandGo.transform.SetParent(root.transform);
            islandGo.transform.localPosition = Vector3.zero;

            var island = islandGo.AddComponent<IslandGenerator>();
            island.GenerateOnStart = false;       // on pilote la génération nous-mêmes
            island.Generate(seed, players);

            float size = island.CurrentSize;
            var water = WaterPlane.Create(Vector3.zero, size * 1.6f);
            water.transform.SetParent(root.transform);

            Physics.SyncTransforms();             // pour que les raycasts de spawn voient le collider

            return new World { Root = root, Island = island, Center = Vector3.zero, Size = size };
        }

        /// <summary>
        /// Point de spawn sur la plage : on part d'un cercle autour de l'île, à un angle donné, et on
        /// avance vers le centre jusqu'à toucher de la terre au-dessus de l'eau (le rivage).
        /// </summary>
        public static Vector3 FindBeachSpawn(Vector3 center, float islandSize, float angleDeg,
                                             float waterLevel = 0f, LayerMask mask = default)
        {
            int layerMask = mask.value == 0 ? ~0 : mask.value;
            Vector3 dir = Quaternion.Euler(0f, angleDeg, 0f) * Vector3.forward;

            float startDist = islandSize * 0.6f;  // juste à l'extérieur de l'île
            const float step = 0.5f;

            for (float d = startDist; d > 0f; d -= step)
            {
                Vector3 probe = center + dir * d;
                if (Physics.Raycast(probe + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 120f,
                                    layerMask, QueryTriggerInteraction.Ignore))
                {
                    // Première terre au-dessus de l'eau rencontrée en venant de l'extérieur = plage.
                    if (hit.point.y > waterLevel + 0.4f)
                        return hit.point + Vector3.up * 1.2f; // un peu au-dessus pour retomber dessus
                }
            }
            return center + Vector3.up * 3f;       // secours : centre
        }
    }
}
