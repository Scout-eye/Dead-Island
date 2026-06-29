using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player.Ragdoll
{
    /// <summary>
    /// Affiche à l'écran (Game view) les métriques clés de l'active ragdoll, pour diagnostiquer et
    /// régler SANS deviner. Pensé pour les captures d'écran : les valeurs critiques sont annotées
    /// quand elles trahissent un problème (controller manquant, root motion à zéro, etc.).
    ///
    /// 100% lecture seule, déterministe — n'influence pas la physique. Toggle : F3.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RagdollDebugOverlay : MonoBehaviour
    {
        [SerializeField] private bool _visible = true;

        private RagdollLocomotion _loco;
        private RagdollBalance _balance;
        private AnimatedReference _anim;
        private ActiveRagdoll _ragdoll;
        private GUIStyle _style;

        private void Awake()
        {
            _loco = GetComponent<RagdollLocomotion>();
            _balance = GetComponent<RagdollBalance>();
            _anim = GetComponent<AnimatedReference>();
            _ragdoll = GetComponent<ActiveRagdoll>();
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame) _visible = !_visible;
        }

        private void OnGUI()
        {
            if (!_visible) return;
            if (_loco != null && !_loco.IsOwner) return; // un seul overlay : le joueur local

            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false
            };

            var sb = new StringBuilder(512);
            sb.AppendLine("<b>RAGDOLL DEBUG</b>  (F3 pour masquer)");

            // --- État physique ---
            if (_balance != null)
            {
                sb.AppendLine($"Grounded : {Flag(_balance.IsGrounded)}    Upright : {Flag(_balance.IsUpright)}    Downed : {(_balance.IsDowned ? "<color=#ff8080>oui</color>" : "non")}");
                sb.AppendLine($"Tilt : {_balance.Tilt:0.}°  (debout si < 35°)");
            }

            // --- Déplacement réel ---
            if (_loco != null)
            {
                Vector3 v = _loco.CurrentVelocity; v.y = 0f;
                sb.AppendLine($"Vitesse corps : {v.magnitude:0.00} m/s    Sprint : {(_loco.IsSprinting ? "oui" : "non")}    Facing : {_loco.BodyYaw:0.}°");
            }

            // --- AnimRig : LE point n°1 à vérifier ---
            var ac = _anim != null ? _anim.Animator : null;
            bool hasCtrl = ac != null && ac.runtimeAnimatorController != null;
            sb.AppendLine($"AnimRig prêt : {Flag(_anim != null && _anim.Ready)}    Controller : {(hasCtrl ? "<color=#80ff80>OK</color>" : "<color=#ff8080>MANQUANT</color>")}");
            if (!hasCtrl)
                sb.AppendLine("  <color=#ff8080>-> sans controller : T-pose, ne tient pas, n'avance pas. Lance Build Player Animator PUIS rebuild.</color>");

            float speedParam = 0f;
            string clip = "(aucun)";
            if (hasCtrl)
            {
                speedParam = ac.GetFloat("Speed");
                var info = ac.GetCurrentAnimatorClipInfo(0);
                if (info != null && info.Length > 0 && info[0].clip != null) clip = info[0].clip.name;
            }
            sb.AppendLine($"Clip joué : {clip}    Speed param : {speedParam:0.0}");

            // --- Root motion : LE point n°2 (cause de "n'avance pas") ---
            float rmSpeed = 0f;
            if (_anim != null) { Vector3 rm = _anim.RootMotionVelocity; rm.y = 0f; rmSpeed = rm.magnitude; }
            sb.Append($"Root motion : {rmSpeed:0.00} m/s");
            if (speedParam > 0.5f && rmSpeed < 0.05f)
                sb.Append("  <color=#ff8080>-> marche demandée mais 0 ! Walk/Run pas en root motion (décoche Bake Into Pose XZ).</color>");
            sb.AppendLine();

            // --- Erreur de poursuite (bassin physique vs bassin animé) ---
            Rigidbody pelvis = _ragdoll != null ? _ragdoll.Pelvis : null;
            Transform hips = _anim != null ? _anim.Hips : null;
            if (pelvis != null && hips != null)
            {
                float err = Vector3.Distance(pelvis.position, hips.position);
                sb.AppendLine($"Erreur de poursuite : {err:0.00} m  (faible = bien collé)");
            }

            GUI.Box(new Rect(8, 8, 560, 184), GUIContent.none);
            GUI.Label(new Rect(16, 12, 552, 180), sb.ToString(), _style);
        }

        private static string Flag(bool ok) => ok ? "<color=#80ff80>oui</color>" : "<color=#ff8080>non</color>";
    }
}
