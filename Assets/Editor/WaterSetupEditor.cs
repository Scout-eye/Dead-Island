using Game.World;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pose une eau stylisée (DealIsland/WaterURP) dans la scène pour tester hors réseau,
/// centrée/dimensionnée sur l'île présente. Réutilise WaterPlane (même code qu'en multijoueur).
/// </summary>
public static class WaterSetupEditor
{
    [MenuItem("Tools/Dead Island/Add Water (URP)")]
    public static void AddWater()
    {
        if (GameObject.Find("Water") != null)
        {
            Debug.Log("[Water] Un objet 'Water' existe déjà. Supprime-le pour le recréer.");
            return;
        }

        float size = 320f;
        Vector3 center = Vector3.zero;
        var island = Object.FindAnyObjectByType<IslandGenerator>();
        if (island != null)
        {
            center = island.transform.position;
            if (island.CurrentSize > 1f) size = island.CurrentSize * 1.6f;
        }

        var go = WaterPlane.Create(center, size);
        Undo.RegisterCreatedObjectUndo(go, "Add Water");
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log($"[Water] Eau ajoutée (plan {size:0}m) à y=0.");
    }
}
