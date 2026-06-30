using Game.Player;
using UnityEditor;
using UnityEngine;

namespace Game.Player.EditorTools
{
    /// <summary>
    /// Crée un item de démo "Pomme" : modèle placeholder (sphère rouge) + asset ConsumableItem +
    /// matériau de contour blanc, et POSE une pomme ramassable dans la scène courante.
    /// Pour un vrai modèle : remplacer le champ "View Prefab" de Apple.asset — AUCUN code à changer.
    ///
    /// Menu : Tools ▸ Dead Island ▸ Create Sample Apple.
    /// </summary>
    public static class ItemSetupBuilder
    {
        private const string Dir = "Assets/Resources/Items";
        private const string ViewPath = Dir + "/AppleView.prefab";
        private const string MatPath = Dir + "/AppleMat.mat";
        private const string ItemPath = Dir + "/Apple.asset";
        private const string OutlinePath = "Assets/Resources/OutlineMat.mat"; // chargé par Highlightable

        [MenuItem("Tools/Dead Island/Create Sample Apple")]
        public static void CreateApple()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Resources", "Items");

            EnsureOutlineMaterial();
            var view = CreateSphereView();
            var item = CreateItemAsset(view);
            SpawnWorldApple(item);

            Debug.Log("[ItemSetup] Pomme posée dans la scène (sphère placeholder). Vise-la (contour blanc) + " +
                      "touche Interagir (E) pour la ramasser. Vrai modèle : remplace 'View Prefab' de Apple.asset.", item);
            Selection.activeObject = item;
            EditorGUIUtility.PingObject(item);
        }

        private static void EnsureOutlineMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(OutlinePath) != null) return;
            var shader = Shader.Find("DeadIsland/Outline");
            if (shader == null) { Debug.LogWarning("[ItemSetup] Shader DeadIsland/Outline introuvable."); return; }
            var mat = new Material(shader);
            if (mat.HasProperty("_OutlineColor")) mat.SetColor("_OutlineColor", Color.white);
            if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0.02f);
            AssetDatabase.CreateAsset(mat, OutlinePath);
        }

        private static GameObject CreateSphereView()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.8f, 0.1f, 0.1f));
                else mat.color = new Color(0.8f, 0.1f, 0.1f);
                AssetDatabase.CreateAsset(mat, MatPath);
            }

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "AppleView";
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.transform.localScale = Vector3.one * 0.12f;
            sphere.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var prefab = PrefabUtility.SaveAsPrefabAsset(sphere, ViewPath);
            Object.DestroyImmediate(sphere);
            return prefab;
        }

        private static ConsumableItem CreateItemAsset(GameObject view)
        {
            var item = AssetDatabase.LoadAssetAtPath<ConsumableItem>(ItemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ConsumableItem>();
                AssetDatabase.CreateAsset(item, ItemPath);
            }

            var so = new SerializedObject(item);
            so.FindProperty("_displayName").stringValue = "Pomme";
            so.FindProperty("_viewPrefab").objectReferenceValue = view;
            so.FindProperty("_hunger").floatValue = 35f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
            AssetDatabase.SaveAssets();
            return item;
        }

        private static void SpawnWorldApple(ItemDefinition item)
        {
            var go = new GameObject("Apple (World)");
            go.transform.position = new Vector3(1.5f, 0.15f, 1.5f);

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.07f; // proche du visuel (sphère 0.12 ⌀) pour qu'elle repose au sol sans flotter

            var world = go.AddComponent<WorldItem>();
            var so = new SerializedObject(world);
            so.FindProperty("_item").objectReferenceValue = item;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(go);
            Selection.activeGameObject = go;
        }
    }
}
