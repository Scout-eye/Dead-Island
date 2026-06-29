using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.Player.EditorTools
{
    /// <summary>
    /// Génère l'AnimatorController du squelette de référence (AnimRig) à partir des clips Mixamo
    /// déposés dans Assets/Animations/Player/. Évite de câbler le state machine à la main.
    ///
    /// Clips attendus (le nom du fichier .fbx doit CONTENIR le mot-clé, casse ignorée) :
    ///   idle, walk, run, jump, getupback, getupfront
    /// (ex: "Idle.fbx", "Walking.fbx", "Running.fbx", "Jump.fbx", "Stand Up.fbx" -> renomme en GetUpBack,
    ///  "Get Up.fbx" -> GetUpFront). Les clips manquants sont simplement ignorés (avec un avertissement).
    ///
    /// Menu : Tools ▸ Dead Island ▸ Build Player Animator.
    /// </summary>
    public static class PlayerAnimatorBuilder
    {
        private const string Dir = "Assets/Animations/Player";
        public const string ControllerPath = Dir + "/PlayerLocomotion.controller";

        [MenuItem("Tools/Dead Island/Build Player Animator")]
        public static void BuildAnimator()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Animations"))
                    AssetDatabase.CreateFolder("Assets", "Animations");
                AssetDatabase.CreateFolder("Assets/Animations", "Player");
                Debug.LogWarning($"[AnimatorBuilder] Dossier {Dir} créé. Dépose-y les clips Mixamo " +
                                 "(idle, walk, run, jump, getupback, getupfront) puis relance.");
                return;
            }

            // Boucle les clips cycliques + active le ROOT MOTION avant/arrière sur marche/course
            // (c'est lui qui donne la vitesse de déplacement au corps physique).
            ConfigureClip("idle", loop: true, keepRootMotionXZ: false);
            ConfigureClip("walk", loop: true, keepRootMotionXZ: true);
            ConfigureClip("run", loop: true, keepRootMotionXZ: true);
            AssetDatabase.Refresh();

            var idle = FindClip("idle");
            var walk = FindClip("walk");
            var run = FindClip("run");
            var jump = FindClip("jump");
            var getUpBack = FindClip("getupback");
            var getUpFront = FindClip("getupfront");

            Debug.Log($"[AnimatorBuilder] Clips trouvés dans {Dir} — idle:{Y(idle)} walk:{Y(walk)} " +
                      $"run:{Y(run)} jump:{Y(jump)} getupback:{Y(getUpBack)} getupfront:{Y(getUpFront)}");

            // Un seul clip de relevé fourni ? On l'utilise pour les deux orientations (dos ET ventre)
            // en attendant un vrai clip "sur le ventre". Le déclencheur reste fonctionnel des deux côtés.
            if (getUpFront == null) getUpFront = getUpBack;
            if (getUpBack == null) getUpBack = getUpFront;

            if (idle == null && walk == null)
            {
                EditorUtility.DisplayDialog("Build Player Animator",
                    $"Aucun clip 'idle'/'walk' trouvé dans {Dir}.\n\n" +
                    "Importe d'abord les animations Mixamo (FBX Y Bot, sans skin).", "OK");
                return;
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("GetUpBack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("GetUpFront", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            // --- Blend tree de locomotion (idle <-> marche <-> course) piloté par Speed ---
            var locoState = controller.CreateBlendTreeInController("Locomotion", out var tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            if (idle != null) tree.AddChild(idle, 0f);
            if (walk != null) tree.AddChild(walk, 1.6f);
            if (run != null) tree.AddChild(run, 4.0f);
            sm.defaultState = locoState;

            // --- Saut (optionnel) : joué quand !Grounded ---
            if (jump != null)
            {
                var jumpState = sm.AddState("Jump");
                jumpState.motion = jump;
                var toJump = sm.AddAnyStateTransition(jumpState);
                toJump.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
                toJump.hasExitTime = false;
                toJump.duration = 0.1f;
                toJump.canTransitionToSelf = false;
                var fromJump = jumpState.AddTransition(locoState);
                fromJump.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
                fromJump.hasExitTime = false;
                fromJump.duration = 0.15f;
            }

            // --- Relevés : déclenchés par trigger, retour à la locomotion à la fin du clip ---
            AddGetUp(sm, locoState, getUpBack, "GetUpBack", "Get Up From Back");
            AddGetUp(sm, locoState, getUpFront, "GetUpFront", "Get Up From Front");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            WarnMissing("idle", idle); WarnMissing("walk", walk); WarnMissing("run", run);
            WarnMissing("jump", jump); WarnMissing("getupback", getUpBack); WarnMissing("getupfront", getUpFront);

            Debug.Log($"[AnimatorBuilder] Controller généré : {ControllerPath}. " +
                      "Reconstruis ensuite le Player (Build Player Prefab).", controller);
            Selection.activeObject = controller;
            EditorGUIUtility.PingObject(controller);
        }

        private static void AddGetUp(AnimatorStateMachine sm, AnimatorState back, AnimationClip clip,
                                     string trigger, string stateName)
        {
            if (clip == null) return;
            var state = sm.AddState(stateName);
            state.motion = clip;

            var toState = sm.AddAnyStateTransition(state);
            toState.AddCondition(AnimatorConditionMode.If, 0, trigger);
            toState.hasExitTime = false;
            toState.duration = 0.08f;
            toState.canTransitionToSelf = false;

            var toLoco = state.AddTransition(back);
            toLoco.hasExitTime = true;
            toLoco.exitTime = 0.92f;   // revient à la locomotion vers la fin du clip
            toLoco.duration = 0.2f;
        }

        /// <summary>Configure boucle + root motion d'un clip (via le ModelImporter), sans clic manuel.</summary>
        private static void ConfigureClip(string keyword, bool loop, bool keepRootMotionXZ)
        {
            var path = FindClipPath(keyword);
            if (path == null) return;
            if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) return;

            var clips = imp.clipAnimations;
            if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;

            foreach (var c in clips)
            {
                c.loopTime = loop;
                c.lockRootRotation = true;                 // on contrôle le facing -> rotation bakée
                c.lockRootHeightY = true;                  // on contrôle la hauteur -> Y baké
                c.lockRootPositionXZ = !keepRootMotionXZ;  // false = GARDE le déplacement avant/arrière
            }

            imp.clipAnimations = clips;   // matérialise les overrides (sinon les defaults ne sont pas sauvés)
            imp.SaveAndReimport();
        }

        /// <summary>
        /// Cherche le FICHIER (.fbx ou .anim) dont le nom contient le mot-clé, en énumérant le dossier
        /// sur le DISQUE — fiable, contrairement à FindAssets("t:AnimationClip") qui rate les clips
        /// embarqués dans les FBX (cas Mixamo).
        /// </summary>
        private static string FindClipPath(string keyword)
        {
            keyword = keyword.ToLowerInvariant();
            if (!Directory.Exists(Dir)) return null;
            foreach (var raw in Directory.GetFiles(Dir))
            {
                var path = raw.Replace('\\', '/');
                if (path.EndsWith(".meta")) continue;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".fbx" && ext != ".anim") continue;
                var file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Replace(" ", "");
                if (file.Contains(keyword)) return path;
            }
            return null;
        }

        /// <summary>Trouve le clip d'animation dont le nom de FICHIER contient le mot-clé.</summary>
        private static AnimationClip FindClip(string keyword)
        {
            var path = FindClipPath(keyword);
            if (path == null) return null;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                if (obj is AnimationClip clip && !clip.name.StartsWith("__"))
                    return clip;
            return null;
        }

        private static void WarnMissing(string keyword, AnimationClip clip)
        {
            if (clip == null)
                Debug.LogWarning($"[AnimatorBuilder] Clip '{keyword}' introuvable dans {Dir} — état ignoré.");
        }

        private static string Y(AnimationClip clip) => clip != null ? "OK" : "ABSENT";
    }
}
