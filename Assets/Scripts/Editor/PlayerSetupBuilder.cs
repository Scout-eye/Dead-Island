using Game.Player;
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

        [MenuItem("Tools/Dead Island/Build Player From Y Bot")]
        public static void BuildPlayer()
        {
            var player = BuildPlayerObject();
            if (player == null) return;
            Selection.activeGameObject = player;
            EditorGUIUtility.PingObject(player);
            Debug.Log("[PlayerBuilder] Player construit dans la scène. Règle les LayerMask (GroundMask/GripMask).", player);
        }

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
            var player = new GameObject("Player");

            var rb = player.AddComponent<Rigidbody>();
            rb.mass = 70f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezeRotation;

            var capsule = player.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.3f;
            capsule.center = new Vector3(0f, 0.9f, 0f);

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

            // --- CameraRig + Camera ---
            var rig = new GameObject("CameraRig");
            rig.transform.SetParent(player.transform, false);
            rig.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            var cam = rig.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            rig.AddComponent<AudioListener>();

            // --- Bones ---
            Transform root = modelInstance.transform;
            Transform head = FindBone(root, "Head");
            Transform lUpper = FindBone(root, "LeftArm");
            Transform lFore = FindBone(root, "LeftForeArm");
            Transform lHand = FindBone(root, "LeftHand");
            Transform rUpper = FindBone(root, "RightArm");
            Transform rFore = FindBone(root, "RightForeArm");
            Transform rHand = FindBone(root, "RightHand");

            Transform hips = FindBone(root, "Hips");
            Transform spine = FindBone(root, "Spine");
            Transform spine1 = FindBone(root, "Spine1");
            Transform spine2 = FindBone(root, "Spine2");
            Transform neck = FindBone(root, "Neck");
            Transform lUpLeg = FindBone(root, "LeftUpLeg");
            Transform lLeg = FindBone(root, "LeftLeg");
            Transform lFoot = FindBone(root, "LeftFoot");
            Transform rUpLeg = FindBone(root, "RightUpLeg");
            Transform rLeg = FindBone(root, "RightLeg");
            Transform rFoot = FindBone(root, "RightFoot");

            if (head != null)
                rig.transform.position = head.position;

            // --- Composants joueur ---
            player.AddComponent<PlayerInputReader>();
            var body = player.AddComponent<PlayerBody>();
            var camera = player.AddComponent<PlayerCamera>();
            var animator = player.AddComponent<PlayerProceduralAnimator>();
            var hands = player.AddComponent<PlayerHands>();
            player.AddComponent<RemotePlayer>(); // activé seulement pour les joueurs distants

            // --- Câblage via SerializedObject (champs privés [SerializeField]) ---
            var soCam = new SerializedObject(camera);
            SetRef(soCam, "_cameraRig", rig.transform);
            SetRef(soCam, "_headBone", head);
            soCam.ApplyModifiedPropertiesWithoutUndo();

            var soHands = new SerializedObject(hands);
            SetRef(soHands, "_leftUpperArm", lUpper);
            SetRef(soHands, "_leftForeArm", lFore);
            SetRef(soHands, "_leftHand", lHand);
            SetRef(soHands, "_rightUpperArm", rUpper);
            SetRef(soHands, "_rightForeArm", rFore);
            SetRef(soHands, "_rightHand", rHand);
            soHands.ApplyModifiedPropertiesWithoutUndo();

            var soAnim = new SerializedObject(animator);
            SetRef(soAnim, "_spine", spine);
            SetRef(soAnim, "_spine1", spine1);
            SetRef(soAnim, "_spine2", spine2);
            SetRef(soAnim, "_neck", neck);
            SetRef(soAnim, "_head", head);
            SetRef(soAnim, "_hips", hips);
            SetRef(soAnim, "_leftUpLeg", lUpLeg);
            SetRef(soAnim, "_leftLeg", lLeg);
            SetRef(soAnim, "_leftFoot", lFoot);
            SetRef(soAnim, "_rightUpLeg", rUpLeg);
            SetRef(soAnim, "_rightLeg", rLeg);
            SetRef(soAnim, "_rightFoot", rFoot);
            soAnim.ApplyModifiedPropertiesWithoutUndo();

            // Avertit si des bones manquent (noms inattendus).
            WarnMissing(head, lUpper, lFore, lHand, rUpper, rFore, rHand);

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

        private static void WarnMissing(params Transform[] bones)
        {
            string[] names = { "Head", "LeftArm", "LeftForeArm", "LeftHand", "RightArm", "RightForeArm", "RightHand" };
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null)
                    Debug.LogWarning($"[PlayerBuilder] Bone non résolu : {names[i]} — à assigner à la main.");
            }
        }
    }
}
