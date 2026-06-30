using System.Collections.Generic;
using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Affiche un contour blanc autour de l'objet quand on peut le saisir. Crée (à la demande) des
    /// "coques" enfant qui dupliquent les meshes avec le matériau de contour, et les active/désactive.
    /// Générique : ne dépend que de ses propres MeshRenderers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Highlightable : MonoBehaviour
    {
        [Tooltip("Matériau de contour (DeadIsland/Outline). Si vide : chargé depuis Resources/OutlineMat.")]
        [SerializeField] private Material _outlineMaterial;

        private readonly List<GameObject> _shells = new List<GameObject>();
        private bool _built;
        private bool _on;

        public void SetHighlighted(bool on)
        {
            if (on == _on) return;
            _on = on;
            if (on && !_built) Build();
            foreach (var s in _shells) if (s != null) s.SetActive(on);
        }

        private void Build()
        {
            _built = true;
            var mat = _outlineMaterial != null ? _outlineMaterial : Resources.Load<Material>("OutlineMat");
            if (mat == null) { Debug.LogWarning("[Highlightable] Matériau de contour introuvable (Resources/OutlineMat).", this); return; }

            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
                var shell = new GameObject("Outline");
                shell.transform.SetParent(mf.transform, false);
                shell.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                shell.AddComponent<MeshRenderer>().sharedMaterial = mat;
                shell.SetActive(false);
                _shells.Add(shell);
            }
        }
    }
}
