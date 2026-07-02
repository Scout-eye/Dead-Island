using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Détruit le mesh généré au runtime quand son GameObject disparaît (évite d'accumuler des
    /// meshes orphelins quand on régénère le monde — sandbox, retour salle d'attente…).
    /// À poser sur tout GameObject dont le MeshFilter porte un mesh créé par code.
    /// ExecuteAlways : le nettoyage doit aussi tourner en mode édition (sandbox d'île).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class GeneratedMeshCleanup : MonoBehaviour
    {
        private void OnDestroy()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var mesh = mf.sharedMesh;
            mf.sharedMesh = null;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
    }
}
