using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.Player.EditorTools
{
    /// <summary>
    /// Génère l'AnimatorController de l'avatar depuis les clips Mixamo (EN PLACE — le
    /// CharacterController déplace le corps).
    ///
    /// Structure (façon controller pro, cf. blend tree 2D directionnel) :
    ///  - Locomotion = blend tree 2D (MoveX = strafe, MoveZ = avant/arrière) : idle, marche/course
    ///    avant + arrière + strafe G/D. Seuils en m/s alignés sur FirstPersonController (1.9 / 4.3).
    ///  - Saut = mini state machine : Jump (départ) → Fall (chute en boucle) → Land (réception).
    ///  - Crouch (blend idle/marche accroupie, bool Crouch), Swim (bool), Interact (trigger).
    ///
    /// Clips manquants ignorés proprement. Le matching privilégie le NOM DE FICHIER EXACT
    /// (ex. "WalkForward.fbx") puis un repli "contient". Menu : Tools ▸ Dead Island ▸ Build Player Animator.
    /// </summary>
    public static class PlayerAnimatorBuilder
    {
        private const string Dir = "Assets/Animations/Player";
        public const string ControllerPath = Dir + "/PlayerLocomotion.controller";

        private const float Walk = 1.9f;  // m/s — doit matcher FirstPersonController
        private const float Run = 4.3f;

        [MenuItem("Tools/Dead Island/Build Player Animator")]
        public static void BuildAnimator()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
                AssetDatabase.CreateFolder("Assets/Animations", "Player");
                Debug.LogWarning($"[AnimatorBuilder] Dossier {Dir} créé. Dépose-y les clips Mixamo puis relance.");
                return;
            }

            ConfigureAllClips(); // boucle + en place sur tous les FBX/anim du dossier

            // Locomotion 2D directionnelle
            var idle = FindClip("idle");
            var fwdW = FindClip("walkforward", "walk");
            var fwdR = FindClip("runforward", "run");
            var backW = FindClip("walkback", "walkbackward", "walkingbackward");
            var backR = FindClip("runback", "runbackward", "runningbackward");
            var leftW = FindClip("walkstrafeleft", "leftstrafewalking");
            var leftR = FindClip("runstrafeleft", "leftstrafe");
            var rightW = FindClip("walkstraferight", "rightstrafewalking");
            var rightR = FindClip("runstraferight", "rightstrafe");
            // Saut
            var jump = FindClip("jump");
            var fall = FindClip("fall", "fallingidle", "falling");
            // Autres
            var swim = FindClip("swim");
            var interact = FindClip("interact");

            Debug.Log($"[AnimatorBuilder] Locomotion — idle:{Y(idle)} fwd(W/R):{Y(fwdW)}/{Y(fwdR)} " +
                      $"back(W/R):{Y(backW)}/{Y(backR)} L(W/R):{Y(leftW)}/{Y(leftR)} R(W/R):{Y(rightW)}/{Y(rightR)}");
            Debug.Log($"[AnimatorBuilder] Saut — jump:{Y(jump)} fall:{Y(fall)} | swim:{Y(swim)} interact:{Y(interact)}");

            if (idle == null && fwdW == null)
            {
                EditorUtility.DisplayDialog("Build Player Animator",
                    $"Aucun clip 'idle'/'walk' dans {Dir}.\nImporte d'abord les animations Mixamo (Y Bot, sans skin).", "OK");
                return;
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Swim", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Interact", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            // --- Locomotion 2D directionnelle (MoveX = strafe, MoveZ = avant/arrière) ---
            var loco = controller.CreateBlendTreeInController("Locomotion", out var tree);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveZ";
            AddDir(tree, idle, 0f, 0f);
            AddDir(tree, fwdW, 0f, Walk); AddDir(tree, fwdR, 0f, Run);
            AddDir(tree, backW, 0f, -Walk); AddDir(tree, backR, 0f, -Run);
            AddDir(tree, leftW, -Walk, 0f); AddDir(tree, leftR, -Run, 0f);
            AddDir(tree, rightW, Walk, 0f); AddDir(tree, rightR, Run, 0f);
            sm.defaultState = loco;

            // --- Saut / chute ---
            // Jump (clip de saut) déclenché par le TRIGGER Jump (part à l'instant du saut).
            // Fall (chute en boucle) pour la phase aérienne + la marche dans le vide.
            // Retour fluide à la locomotion dès qu'on touche le sol (pas d'atterrissage violent).
            AnimatorState airState = null;
            if (fall != null)
            {
                var fallState = sm.AddState("Fall");
                fallState.motion = fall;
                airState = fallState;
                // On entre en chute quand on quitte le sol sans avoir sauté (rebord, descente).
                var locoToFall = loco.AddTransition(fallState);
                locoToFall.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
                locoToFall.hasExitTime = false; locoToFall.duration = 0.2f;
                // Réception : retour à la locomotion en douceur.
                var fallToLoco = fallState.AddTransition(loco);
                fallToLoco.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
                fallToLoco.hasExitTime = false; fallToLoco.duration = 0.18f;
            }

            if (jump != null)
            {
                var jumpState = sm.AddState("Jump");
                jumpState.motion = jump;
                var toJump = sm.AddAnyStateTransition(jumpState);
                toJump.AddCondition(AnimatorConditionMode.If, 0, "Jump"); // trigger
                toJump.hasExitTime = false; toJump.duration = 0.1f; toJump.canTransitionToSelf = false;

                // Après le clip de saut : enchaîne sur la chute (ou retour loco si pas de clip Fall).
                var afterJump = airState != null ? airState : loco;
                var jumpToAir = jumpState.AddTransition(afterJump);
                jumpToAir.hasExitTime = true; jumpToAir.exitTime = 0.5f; jumpToAir.duration = 0.2f;
                // Sécurité : si on retouche le sol pendant le clip de saut, retour loco.
                var jumpToLoco = jumpState.AddTransition(loco);
                jumpToLoco.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
                jumpToLoco.hasExitTime = true; jumpToLoco.exitTime = 0.4f; jumpToLoco.duration = 0.18f;
            }

            // --- Nage (bool Swim) ---
            if (swim != null)
            {
                var swimState = sm.AddState("Swim");
                swimState.motion = swim;
                var toSwim = sm.AddAnyStateTransition(swimState);
                toSwim.AddCondition(AnimatorConditionMode.If, 0, "Swim");
                toSwim.hasExitTime = false; toSwim.duration = 0.2f; toSwim.canTransitionToSelf = false;
                var fromSwim = swimState.AddTransition(loco);
                fromSwim.AddCondition(AnimatorConditionMode.IfNot, 0, "Swim");
                fromSwim.hasExitTime = false; fromSwim.duration = 0.2f;
            }

            // IK Pass (requis pour le Foot IK qui pose les pieds sur le sol/objets).
            var layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;

            // --- Geste de ramassage : layer HAUT-DU-CORPS masqué -> blende avec la locomotion ---
            if (interact != null) BuildUpperBodyGesture(controller, interact);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[AnimatorBuilder] Controller généré : {ControllerPath}. Reconstruis le Player (Build Player Prefab).", controller);
            Selection.activeObject = controller;
            EditorGUIUtility.PingObject(controller);
        }

        private static void AddDir(BlendTree tree, AnimationClip clip, float x, float z)
        {
            if (clip != null) tree.AddChild(clip, new Vector2(x, z));
        }

        // Layer additionnel masqué (buste + bras + mains) pour jouer un geste par-dessus la locomotion.
        // Poids piloté par PlayerAnimator (0 au repos, 1 pendant le geste) -> les jambes gardent la marche.
        private static void BuildUpperBodyGesture(AnimatorController controller, AnimationClip interact)
        {
            const string maskPath = Dir + "/UpperBody.mask";
            if (AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath) != null) AssetDatabase.DeleteAsset(maskPath);

            var mask = new AvatarMask();
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            AssetDatabase.CreateAsset(mask, maskPath);

            controller.AddLayer("UpperBody");
            var layers = controller.layers;
            int idx = layers.Length - 1;
            layers[idx].avatarMask = mask;
            layers[idx].defaultWeight = 0f; // PlayerAnimator monte le poids pendant le geste
            layers[idx].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            var sm = controller.layers[idx].stateMachine;
            var empty = sm.AddState("Empty");
            sm.defaultState = empty;
            var gesture = sm.AddState("Interact");
            gesture.motion = interact;

            var toGesture = sm.AddAnyStateTransition(gesture);
            toGesture.AddCondition(AnimatorConditionMode.If, 0, "Interact");
            toGesture.hasExitTime = false; toGesture.duration = 0.1f; toGesture.canTransitionToSelf = false;
            var back = gesture.AddTransition(empty);
            back.hasExitTime = true; back.exitTime = 0.85f; back.duration = 0.2f;
        }

        // --- Import : tous les clips EN PLACE ; boucle sauf les one-shots ---
        private static void ConfigureAllClips()
        {
            if (!Directory.Exists(Dir)) return;
            foreach (var raw in Directory.GetFiles(Dir))
            {
                var path = raw.Replace('\\', '/');
                if (path.EndsWith(".meta")) continue;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".fbx") continue;
                if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;

                string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Replace(" ", "");
                bool oneShot = Contains(file, "jump", "land", "interact", "getup", "pickup", "press");

                // Rig Humanoid (sinon les clips Mixamo ne retargettent pas sur l'avatar).
                bool rigChanged = false;
                if (imp.animationType != ModelImporterAnimationType.Human)
                {
                    imp.animationType = ModelImporterAnimationType.Human;
                    imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    rigChanged = true;
                }

                var clips = imp.clipAnimations;
                if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
                if (clips == null || clips.Length == 0) { if (rigChanged) imp.SaveAndReimport(); continue; }

                // Le clip de saut Mixamo a ~14 frames de préparation : on coupe le début pour que
                // le décollage parte tout de suite (sinon le saut "commence en retard").
                bool isJump = file.Contains("jump");
                float jumpStart = 14f;

                bool changed = rigChanged;
                foreach (var c in clips)
                {
                    bool jumpOk = !isJump || c.firstFrame >= jumpStart - 0.5f;
                    bool ok = c.loopTime == !oneShot
                        && c.lockRootRotation && !c.keepOriginalOrientation
                        && c.lockRootHeightY && c.heightFromFeet
                        && c.lockRootPositionXZ && !c.keepOriginalPositionXZ
                        && jumpOk;
                    if (ok) continue;

                    c.loopTime = !oneShot;
                    // EN PLACE + PIEDS AU SOL. (Bake Into Pose ON : config qui JOUE. Si un clip dérive,
                    // c'est qu'il a une translation à la source -> le re-télécharger avec "In Place".)
                    c.lockRootRotation = true;   c.keepOriginalOrientation = false;
                    c.lockRootHeightY = true;    c.heightFromFeet = true;
                    c.lockRootPositionXZ = true;  c.keepOriginalPositionXZ = false;
                    if (isJump) c.firstFrame = jumpStart;
                    changed = true;
                }
                if (changed) { imp.clipAnimations = clips; imp.SaveAndReimport(); }
            }
            AssetDatabase.Refresh();
        }

        private static bool Contains(string s, params string[] words)
        {
            foreach (var w in words) if (s.Contains(w)) return true;
            return false;
        }

        // Cherche un clip : nom de fichier EXACT d'abord (lève l'ambiguïté "left strafe" vs
        // "left strafe walking"), puis repli "contient".
        private static AnimationClip FindClip(params string[] keywords)
        {
            if (!Directory.Exists(Dir)) return null;
            var files = new List<string>();
            foreach (var raw in Directory.GetFiles(Dir))
            {
                var path = raw.Replace('\\', '/');
                if (path.EndsWith(".meta")) continue;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".fbx" || ext == ".anim") files.Add(path);
            }

            foreach (var kw in keywords)
            {
                var k = kw.ToLowerInvariant();
                foreach (var p in files)
                    if (Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Replace(" ", "") == k)
                        return Load(p);
            }
            foreach (var kw in keywords)
            {
                var k = kw.ToLowerInvariant();
                foreach (var p in files)
                    if (Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Replace(" ", "").Contains(k))
                        return Load(p);
            }
            return null;
        }

        private static AnimationClip Load(string path)
        {
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                if (obj is AnimationClip clip && !clip.name.StartsWith("__"))
                    return clip;
            return null;
        }

        private static string Y(AnimationClip clip) => clip != null ? "OK" : "—";
    }
}
