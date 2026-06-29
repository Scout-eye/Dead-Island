using Game.Player;
using Game.Player.Ragdoll;
using UnityEditor;
using UnityEngine;

namespace Game.Player.EditorTools
{
    /// <summary>
    /// Outil éditeur : construit un GameObject "Player" complet à partir du modèle Y Bot (Mixamo),
    /// câble tous les composants de l'étape 1 et résout automatiquement les bones (préfixe
    /// "mixamorig:" géré). Évite tout drag&drop manuel.
    ///
    /// Menu : Tools ▸ Dead Island ▸ Build Player From Y Bot.
    /// </summary>
    public static class PlayerSetupBuilder
    {
        private const string YBotPath = "Assets/Models/Characters/Y Bot.fbx";

        [MenuItem("Tools/Dead Island/Build Player Prefab (Resources)")]
        public static void BuildPlayerPrefab()
        {
            var player = BuildPlayerObject();
            if (player == null) return;

            const string dir = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Resources");
            const string path = dir + "/Player.prefab";

            var prefab = PrefabUtility.SaveAsPrefabAsset(player, path);
            Object.DestroyImmediate(player);

            if (prefab != null)
            {
                Debug.Log($"[PlayerBuilder] Prefab sauvegardé : {path} (chargé par NetworkManager).", prefab);
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }
            else Debug.LogError("[PlayerBuilder] Échec de la sauvegarde du prefab.");
        }

        private static GameObject BuildPlayerObject()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(YBotPath);
            if (model == null)
            {
                EditorUtility.DisplayDialog("Player Builder",
                    $"Modèle introuvable à :\n{YBotPath}", "OK");
                return null;
            }

            // --- Root Player ---
            // Pas de Rigidbody/collider sur le root : c'est l'active ragdoll (un Rigidbody par os,
            // construit par ActiveRagdoll au runtime) qui porte toute la physique.
            var player = new GameObject("Player");

            // --- Modèle Y Bot en enfant (instance de prefab pour garder le lien au FBX) ---
            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.transform.SetParent(player.transform, false);
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;

            // Matériau perso avec écume partagée (même système que le rivage).
            // IMPORTANT : matériau-ASSET (pas new Material runtime), sinon le prefab perd la
            // référence à l'enregistrement -> personnage magenta.
            // On récupère texture + couleur d'origine du modèle pour conserver son apparence.
            Texture srcTex = null;
            Color srcColor = Color.white;
            foreach (var r in modelInstance.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null) continue;
                if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) srcTex = m.GetTexture("_BaseMap");
                else if (m.mainTexture != null) srcTex = m.mainTexture;
                if (m.HasProperty("_BaseColor")) srcColor = m.GetColor("_BaseColor");
                else if (m.HasProperty("_Color")) srcColor = m.GetColor("_Color");
                if (srcTex != null) break;
            }

            var charMat = GetOrCreateCharacterMaterial();
            if (charMat != null)
            {
                if (srcTex != null) charMat.SetTexture("_BaseMap", srcTex);
                // Teinte : couleur d'origine (ou blanc si une texture porte déjà la couleur).
                charMat.SetColor("_BaseColor", srcTex != null ? Color.white : srcColor);
                EditorUtility.SetDirty(charMat);
                AssetDatabase.SaveAssets();

                foreach (var r in modelInstance.GetComponentsInChildren<Renderer>(true))
                    r.sharedMaterial = charMat;
            }

            // --- Squelette ANIMÉ invisible (référence que le ragdoll physique va suivre) ---
            var animRig = (GameObject)PrefabUtility.InstantiatePrefab(model);
            animRig.name = "AnimRig";
            animRig.transform.SetParent(player.transform, false);
            animRig.transform.localPosition = Vector3.zero;
            animRig.transform.localRotation = Quaternion.identity;
            // Décompacte : l'AnimRig devient de simples GameObjects (plus un prefab imbriqué). Sinon
            // l'assignation du controller en OVERRIDE peut ne pas être sauvée dans le prefab Player.
            PrefabUtility.UnpackPrefabInstance(animRig, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var r in animRig.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

            var animator = animRig.GetComponent<Animator>();
            if (animator == null) animator = animRig.AddComponent<Animator>();
            animator.applyRootMotion = true;   // le root motion de marche/course pilote la vitesse
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Fixed;
            var catcher = animRig.AddComponent<RootMotionCatcher>();
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.ControllerPath);
            if (controller == null)
            {
                // Pas de controller -> on le génère AUTOMATIQUEMENT ici (ordre garanti, un seul clic),
                // puis on recharge. Sans lui l'AnimRig reste en T-pose (ne tient pas, n'avance pas).
                Debug.Log("[PlayerBuilder] Controller absent -> génération automatique de l'Animator…");
                PlayerAnimatorBuilder.BuildAnimator();
                AssetDatabase.Refresh();
                controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.ControllerPath);
            }

            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
                Debug.Log($"[PlayerBuilder] ✓ Controller assigné à l'AnimRig : {controller.name}", controller);
            }
            else
            {
                Debug.LogError("[PlayerBuilder] AnimatorController toujours introuvable APRÈS génération auto. " +
                               "Regarde les logs [AnimatorBuilder] : un clip idle/walk est probablement absent ou " +
                               "le dossier Assets/Animations/Player/ est vide.");
            }

            // --- CameraRig + Camera ---
            var rig = new GameObject("CameraRig");
            rig.transform.SetParent(player.transform, false);
            rig.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            var cam = rig.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            rig.AddComponent<AudioListener>();

            // --- Bones utiles au câblage (le ragdoll résout tous ses os lui-même au runtime) ---
            Transform root = modelInstance.transform;
            Transform head = FindBone(root, "Head");
            Transform hips = FindBone(root, "Hips");

            if (head != null)
                rig.transform.position = head.position;

            // --- Composants joueur (active ragdoll façon PEAK) ---
            player.AddComponent<PlayerInputReader>();
            var ragdoll = player.AddComponent<ActiveRagdoll>();   // construit le ragdoll physique au réveil
            player.AddComponent<AnimatedReference>(); // squelette animé que les joints recopient
            player.AddComponent<RagdollBalance>();   // tient debout + relevé
            player.AddComponent<RagdollLocomotion>(); // driver input -> propulsion + saut
            player.AddComponent<RagdollPoseDriver>(); // recopie la pose animée dans les joints
            player.AddComponent<HandReach>();        // clic -> main tendue
            player.AddComponent<RagdollDebugOverlay>(); // métriques à l'écran (F3) pour régler
            var camera = player.AddComponent<PlayerCamera>();
            var remote = player.AddComponent<RemotePlayer>();
            remote.enabled = false; // dormant ; activé par le réseau uniquement pour les distants

            // Survie + mort (faible adhérence : communiquent par événements / registre)
            player.AddComponent<PlayerVitals>();
            player.AddComponent<SpectatorController>(); // dormant tant que vivant (gardé par _active)
            player.AddComponent<PlayerDeath>();

            // --- Câblage caméra (seuls champs à brancher manuellement) ---
            var soCam = new SerializedObject(camera);
            SetRef(soCam, "_cameraRig", rig.transform);
            SetRef(soCam, "_headBone", head);
            soCam.ApplyModifiedPropertiesWithoutUndo();

            // ActiveRagdoll cherche ses os UNIQUEMENT dans le modèle visible (pas l'AnimRig).
            var soRag = new SerializedObject(ragdoll);
            SetRef(soRag, "_skeletonRoot", modelInstance.transform);
            soRag.ApplyModifiedPropertiesWithoutUndo();

            // AnimatedReference pointe sur l'AnimRig invisible.
            var soRef = new SerializedObject(player.GetComponent<AnimatedReference>());
            SetRef(soRef, "_animator", animator);
            SetRef(soRef, "_animRoot", animRig.transform);
            SetRef(soRef, "_catcher", catcher);
            soRef.ApplyModifiedPropertiesWithoutUndo();

            if (hips == null)
                Debug.LogWarning("[PlayerBuilder] Os 'Hips' introuvable — l'active ragdoll ne pourra pas se construire.");

            return player;
        }

        private static Material GetOrCreateCharacterMaterial()
        {
            const string dir = "Assets/Materials";
            const string path = dir + "/CharacterFoam.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            var shader = Shader.Find("DeadIsland/CharacterFoam");
            if (shader == null)
            {
                Debug.LogWarning("[PlayerBuilder] Shader DeadIsland/CharacterFoam introuvable.");
                return null;
            }
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Materials");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static void SetRef(SerializedObject so, string property, Object value)
        {
            var prop = so.FindProperty(property);
            if (prop != null) prop.objectReferenceValue = value;
            else Debug.LogWarning($"[PlayerBuilder] Champ sérialisé introuvable : {property}");
        }

        /// <summary>
        /// Recherche récursive d'un bone par nom, en ignorant un éventuel préfixe "namespace:".
        /// </summary>
        private static Transform FindBone(Transform root, string boneName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                int colon = n.IndexOf(':');
                if (colon >= 0) n = n.Substring(colon + 1);
                if (string.Equals(n, boneName, System.StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }
    }
}
