using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Recherche d'os dans une hiérarchie de squelette, tolérante aux préfixes de rig
    /// ("Head", "mixamorig:Head", "monrig:Head"…). Point unique pour tous les scripts joueur.
    /// </summary>
    public static class BoneUtils
    {
        public static Transform Find(Transform root, string bone)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == bone || n.EndsWith(":" + bone)) return t;
            }
            return null;
        }
    }
}
