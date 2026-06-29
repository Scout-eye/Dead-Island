using Game.Player;
using UnityEditor;
using UnityEngine;

namespace Game.Player.EditorTools
{
    /// <summary>
    /// Construit le prefab joueur PREMIÈRE PERSONNE à partir du modèle Y Bot (Mixamo) et câble tous
    /// les composants : CharacterController + caméra FPS + avatar animé (idle/marche/course).
    ///
    /// Hiérarchie produite :
    ///   Player (CharacterController + FirstPersonController + PlayerCamera + PlayerAnimator + …)
    ///   ├─ Model (Y Bot + Animator)         -> avatar visible (caché pour le joueur local)
    ///   └─ CameraRig (Camera + AudioListener) -> vue à hauteur des yeux
    ///
    /// Menu : Tools ▸ Dead Island ▸ Build Player Prefab (Resources).
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
                Debug.Log($"[PlayerBuilder] Prefab FPS sauvegardé : {path} (chargé par NetworkManager).", prefab);
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
                EditorUtility.DisplayDialog("Player Builder", $"Modèle introuvable à :\n{YBotPath}", "OK");
                return null;
            }

            // --- Root Player + CharacterController ---
            var player = new GameObject("Player");
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.35f;
            cc.skinWidth = 0.02f;

            // --- Modèle Y Bot (avatar visible) ---
            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(player.transform, false);
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;
            ApplyCharacterMaterial(modelInstance);

            // Animator de l'avatar (blend idle/marche/course, EN PLACE).
            var animator = modelInstance.GetComponent<Animator>();
            if (animator == null) animator = modelInstance.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            AssignLocomotionController(animator);

            // --- CameraRig + Camera (hauteur des yeux) ---
            var rig = new GameObject("CameraRig");
            rig.transform.SetParent(player.transform, false);
            rig.transform.localPosition = new Vector3(0f, 1.65f, 0.1f);
            var cam = rig.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            rig.AddComponent<AudioListener>();

            // --- Composants joueur (FPS) ---
            player.AddComponent<PlayerInputReader>();
            player.AddComponent<FirstPersonController>(); // déplacement
            var camera = player.AddComponent<PlayerCamera>();   // regard
            var anim = player.AddComponent<PlayerAnimator>();    // anim avatar
            player.AddComponent<FirstPersonView>();              // cache la tête en local
            var remote = player.AddComponent<RemotePlayer>();
            remote.enabled = false; // dormant ; activé par le réseau pour les distants

            player.AddComponent<PlayerVitals>();
            player.AddComponent<SpectatorController>();
            player.AddComponent<PlayerDeath>();

            // --- Câblage ---
            var soCam = new SerializedObject(camera);
            SetRef(soCam, "_cameraRig", rig.transform);
            soCam.ApplyModifiedPropertiesWithoutUndo();

            var soAnim = new SerializedObject(anim);
            SetRef(soAnim, "_animator", animator);
            soAnim.ApplyModifiedPropertiesWithoutUndo();

            return player;
        }

        private static void AssignLocomotionController(Animator animator)
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.ControllerPath);
            if (controller == null)
            {
                Debug.Log("[PlayerBuilder] Controller absent -> génération automatique de l'Animator…");
                PlayerAnimatorBuilder.BuildAnimator();
                AssetDatabase.Refresh();
                controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.ControllerPath);
            }

            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
                Debug.Log($"[PlayerBuilder] ✓ Controller assigné à l'avatar : {controller.name}", controller);
            }
            else
            {
                Debug.LogError("[PlayerBuilder] AnimatorController introuvable : l'avatar restera en T-pose. " +
                               "Vérifie les clips dans Assets/Animations/Player/ (idle/walk/run).");
            }
        }

        /// <summary>Applique le matériau perso (écume partagée) en conservant texture/teinte d'origine.</summary>
        private static void ApplyCharacterMaterial(GameObject modelInstance)
        {
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
            if (charMat == null) return;

            if (srcTex != null) charMat.SetTexture("_BaseMap", srcTex);
            charMat.SetColor("_BaseColor", srcTex != null ? Color.white : srcColor);
            EditorUtility.SetDirty(charMat);
            AssetDatabase.SaveAssets();

            foreach (var r in modelInstance.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = charMat;
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
    }
}
