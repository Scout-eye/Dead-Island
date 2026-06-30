using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Pour le joueur LOCAL : cache la tête de l'avatar (sinon la caméra à hauteur des yeux voit
    /// l'intérieur du crâne), tout en gardant le corps visible — on voit ses jambes/bras s'animer
    /// quand on regarde vers le bas (façon Content Warning / Lethal Company). Les joueurs DISTANTS
    /// gardent leur avatar entier.
    ///
    /// Responsabilité unique : la visibilité de l'avatar local. Aucune autre dépendance.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FirstPersonView : MonoBehaviour
    {
        [SerializeField] private string _headBoneName = "Head";

        private void Start()
        {
            var controller = GetComponent<FirstPersonController>();
            if (controller != null && !controller.IsOwner) return; // distant : avatar entier visible

            var head = FindBone(transform, _headBoneName);
            if (head != null) head.localScale = Vector3.zero; // cache le crâne en local
        }

        private static Transform FindBone(Transform root, string boneName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == boneName || n == "mixamorig:" + boneName || n.EndsWith(":" + boneName)) return t;
            }
            return null;
        }
    }
}
