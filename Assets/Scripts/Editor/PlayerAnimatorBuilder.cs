using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.Player.EditorTools
{
    /// <summary>
    /// Génère l'AnimatorController de l'avatar à partir des clips Mixamo (EN PLACE — le
    /// CharacterController déplace le corps). États : locomotion (idle/marche/course), saut,
    /// accroupi (idle/marche), nage, interaction. Les clips manquants sont ignorés proprement.
    ///
    /// Clips attendus dans Assets/Animations/Player/ (le nom du FICHIER doit contenir le mot-clé) :
    ///   idle, walk, run, jump, crouchidle, crouchwalk, swim, interact
    ///
    /// Seuils du blend tree alignés sur les vitesses du FirstPersonController (1.9 marche, 4.3 course)
    /// pour que les pieds ne glissent pas. Menu : Tools ▸ Dead Island ▸ Build Player Animator.
    /// </summary>
    public static class PlayerAnimatorBuilder
    {
        private const string Dir = "Assets/Animations/Player";
        public const string ControllerPath = Dir + "/PlayerLocomotion.controller";

        private const float WalkThreshold = 1.9f;
        private const float RunThreshold = 4.3f;
        private const float CrouchThreshold = 1.1f;

        [MenuItem("Tools/Dead Island/Build Player Animator")]
        public static void BuildAnimator()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Animations"))
                    AssetDatabase.CreateFolder("Assets", "Animations");
                AssetDatabase.CreateFolder("Assets/Animations", "Player");
                Debug.LogWarning($"[AnimatorBuilder] Dossier {Dir} créé. Dépose-y les clips Mixamo puis relance.");
                return;
            }

            // Clips EN PLACE (le CharacterController déplace) : loop pour les cycliques.
            ConfigureClip("idle", true); ConfigureClip("walk", true); ConfigureClip("run", true);
            ConfigureClip("crouchidle", true); ConfigureClip("crouchwalk", true); ConfigureClip("swim", true);
            ConfigureClip("jump", false); ConfigureClip("interact", false);
            AssetDatabase.Refresh();

            var idle = FindClip("idle");
            var walk = FindClip("walk");
            var run = FindClip("run");
            var jump = FindClip("jump");
            var crouchIdle = FindClip("crouchidle");
            var crouchWalk = FindClip("crouchwalk");
            var swim = FindClip("swim");
            var interact = FindClip("interact");

            Debug.Log($"[AnimatorBuilder] Clips — idle:{Y(idle)} walk:{Y(walk)} run:{Y(run)} jump:{Y(jump)} " +
                      $"crouchidle:{Y(crouchIdle)} crouchwalk:{Y(crouchWalk)} swim:{Y(swim)} interact:{Y(interact)}");

            if (idle == null && walk == null)
            {
                EditorUtility.DisplayDialog("Build Player Animator",
                    $"Aucun clip 'idle'/'walk' dans {Dir}.\nImporte d'abord les animations Mixamo (Y Bot, sans skin).", "OK");
                return;
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Swim", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Interact", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            // --- Locomotion debout (idle <-> marche <-> course) ---
            var loco = LocomotionBlend(controller, "Locomotion", idle, walk, run, WalkThreshold, RunThreshold);
            sm.defaultState = loco;

            // --- Accroupi (idle <-> marche accroupie), bascule par le bool Crouch ---
            if (crouchIdle != null)
            {
                var crouch = LocomotionBlend(controller, "Crouch", crouchIdle, crouchWalk, null, CrouchThreshold, 99f);
                Bidir(loco, crouch, "Crouch");
            }

            // --- Saut : joué tant qu'on n'est pas au sol ---
            if (jump != null)
            {
                var jumpState = sm.AddState("Jump");
                jumpState.motion = jump;
                var toJump = sm.AddAnyStateTransition(jumpState);
                toJump.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
                toJump.hasExitTime = false; toJump.duration = 0.1f; toJump.canTransitionToSelf = false;
                var fromJump = jumpState.AddTransition(loco);
                fromJump.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
                fromJump.hasExitTime = false; fromJump.duration = 0.15f;
            }

            // --- Nage : bascule par le bool Swim ---
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

            // --- Interaction : geste one-shot sur trigger ---
            if (interact != null)
            {
                var interactState = sm.AddState("Interact");
                interactState.motion = interact;
                var toInteract = sm.AddAnyStateTransition(interactState);
                toInteract.AddCondition(AnimatorConditionMode.If, 0, "Interact");
                toInteract.hasExitTime = false; toInteract.duration = 0.05f; toInteract.canTransitionToSelf = false;
                var fromInteract = interactState.AddTransition(loco);
                fromInteract.hasExitTime = true; fromInteract.exitTime = 0.85f; fromInteract.duration = 0.15f;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[AnimatorBuilder] Controller généré : {ControllerPath}. Reconstruis le Player (Build Player Prefab).", controller);
            Selection.activeObject = controller;
            EditorGUIUtility.PingObject(controller);
        }

        /// <summary>Crée un état blend-tree 1D piloté par Speed (idle / marche / [course]).</summary>
        private static AnimatorState LocomotionBlend(AnimatorController controller, string name,
                                                     AnimationClip idle, AnimationClip walk, AnimationClip run,
                                                     float walkThreshold, float runThreshold)
        {
            var state = controller.CreateBlendTreeInController(name, out var tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            if (idle != null) tree.AddChild(idle, 0f);
            if (walk != null) tree.AddChild(walk, walkThreshold);
            if (run != null) tree.AddChild(run, runThreshold);
            return state;
        }

        /// <summary>Transitions a&lt;-&gt;b pilotées par un bool (true = vers b).</summary>
        private static void Bidir(AnimatorState a, AnimatorState b, string boolParam)
        {
            var toB = a.AddTransition(b);
            toB.AddCondition(AnimatorConditionMode.If, 0, boolParam);
            toB.hasExitTime = false; toB.duration = 0.15f;
            var toA = b.AddTransition(a);
            toA.AddCondition(AnimatorConditionMode.IfNot, 0, boolParam);
            toA.hasExitTime = false; toA.duration = 0.15f;
        }

        /// <summary>Configure boucle + EN PLACE (pas de root motion) d'un clip via le ModelImporter.</summary>
        private static void ConfigureClip(string keyword, bool loop)
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
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true; // en place
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
        }

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

        private static AnimationClip FindClip(string keyword)
        {
            var path = FindClipPath(keyword);
            if (path == null) return null;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                if (obj is AnimationClip clip && !clip.name.StartsWith("__"))
                    return clip;
            return null;
        }

        private static string Y(AnimationClip clip) => clip != null ? "OK" : "ABSENT";
    }
}
