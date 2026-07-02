using System.Collections.Generic;
using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Ragdoll par os "à la demande" (mort / chute) sur un personnage ANIMÉ.
    /// Construit au réveil un Rigidbody + collider + CharacterJoint sur chaque membre du squelette
    /// Mixamo (Y Bot), avec des limites d'amplitude pour éviter les poses impossibles (tête à
    /// l'envers, coude/genou hyper-étendus).
    ///
    /// Vivant : os kinematic + colliders OFF + Animator pilote la pose. À la mort, <see cref="Activate"/>
    /// coupe l'Animator + les contrôleurs et passe les os en physique : ils s'effondrent et réagissent
    /// au sol et entre eux, en partant de la dernière pose animée (donc transition naturelle).
    ///
    /// Aucune dépendance externe : agit sur sa propre hiérarchie.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRagdoll : MonoBehaviour
    {
        private readonly List<Rigidbody> _bodies = new List<Rigidbody>();
        private bool _built;
        private bool _activated;
        private float _height = 1.8f;

        // NB : on NE construit PAS le ragdoll à l'Awake. Tant que le joueur est vivant, aucun Rigidbody
        // n'est ajouté aux os → l'Animator pose le squelette librement. Le ragdoll est construit
        // uniquement à la mort (Activate), à partir de la dernière pose animée.

        public void Activate()
        {
            if (_activated) return;
            _activated = true;
            BuildRagdoll();

            // Coupe l'animation et tout ce qui pilote/contraint le corps.
            foreach (var a in GetComponentsInChildren<Animator>(true)) a.enabled = false;
            Disable<FirstPersonController>();
            Disable<PlayerInputReader>();
            Disable<PlayerAnimator>();
            Disable<PlayerCamera>();
            Disable<RemotePlayer>();

            // Récupère l'élan, neutralise la capsule (CharacterController) qui maintiendrait debout.
            Vector3 vel = Vector3.zero;
            if (TryGetComponent<CharacterController>(out var cc)) { vel = cc.velocity; cc.enabled = false; }

            SetRagdoll(true, vel);
        }

        // --- Construction ---

        private void BuildRagdoll()
        {
            if (_built) return;
            var hips = Find("Hips");
            if (hips == null) { Debug.LogWarning("[Ragdoll] Bone 'Hips' introuvable — ragdoll inactif.", this); return; }

            var spine = Find("Spine") ?? Find("Spine1");
            var neck = Find("Neck") ?? Find("Head");
            var head = Find("Head");
            var lArm = Find("LeftArm"); var lFore = Find("LeftForeArm"); var lHand = Find("LeftHand");
            var rArm = Find("RightArm"); var rFore = Find("RightForeArm"); var rHand = Find("RightHand");
            var lUpLeg = Find("LeftUpLeg"); var lLeg = Find("LeftLeg"); var lFoot = Find("LeftFoot");
            var rUpLeg = Find("RightUpLeg"); var rLeg = Find("RightLeg"); var rFoot = Find("RightFoot");

            if (head != null && lFoot != null) _height = Mathf.Max(0.5f, head.position.y - lFoot.position.y);

            float hipWidth = (lUpLeg != null && rUpLeg != null) ? Vector3.Distance(lUpLeg.position, rUpLeg.position) : 0.2f * _height;
            float shoulderW = (lArm != null && rArm != null) ? Vector3.Distance(lArm.position, rArm.position) : 0.28f * _height;

            // Tronc
            var hipsRb = AddBody(hips, 11f);
            AddCapsule(hips, spine, hipWidth * 0.55f);

            Rigidbody spineRb = hipsRb;
            if (spine != null)
            {
                spineRb = AddBody(spine, 16f);
                AddCapsule(spine, neck, shoulderW * 0.32f);
                AddJoint(spine, hipsRb, twist: 15f, swing1: 20f, swing2: 20f);
            }

            // Tête
            if (head != null)
            {
                AddBody(head, 5f);
                AddSphere(head, 0.085f * _height, head, neck);
                AddJoint(head, spineRb, twist: 25f, swing1: 25f, swing2: 20f);
            }

            // Bras (épaule souple, coude limité à un plan)
            BuildLimb(lArm, lFore, lHand, spineRb, 2.5f, 1.5f, 0.045f, 0.04f);
            BuildLimb(rArm, rFore, rHand, spineRb, 2.5f, 1.5f, 0.045f, 0.04f);

            // Jambes (hanche moyenne, genou limité à un plan)
            BuildLimb(lUpLeg, lLeg, lFoot, hipsRb, 7f, 3.5f, 0.07f, 0.055f, isLeg: true);
            BuildLimb(rUpLeg, rLeg, rFoot, hipsRb, 7f, 3.5f, 0.07f, 0.055f, isLeg: true);

            _built = true;
        }

        private void BuildLimb(Transform upper, Transform lower, Transform tip, Rigidbody parent,
                               float upperMass, float lowerMass, float upperR, float lowerR, bool isLeg = false)
        {
            if (upper == null || lower == null) return;

            var upperRb = AddBody(upper, upperMass);
            AddCapsule(upper, lower, upperR * _height);
            if (isLeg) AddJoint(upper, parent, twist: 25f, swing1: 45f, swing2: 45f);
            else AddJoint(upper, parent, twist: 40f, swing1: 70f, swing2: 70f);

            AddBody(lower, lowerMass);
            AddCapsule(lower, tip, lowerR * _height);
            AddJoint(lower, upperRb, twist: 10f, swing1: 90f, swing2: 8f); // coude / genou
        }

        // --- Helpers physiques ---

        private Rigidbody AddBody(Transform t, float mass)
        {
            if (!t.TryGetComponent<Rigidbody>(out var rb)) rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.isKinematic = true;
            _bodies.Add(rb);
            return rb;
        }

        private static void AddCapsule(Transform bone, Transform child, float radius)
        {
            var cap = bone.gameObject.AddComponent<CapsuleCollider>();
            cap.radius = Mathf.Max(0.02f, radius);
            if (child != null)
            {
                Vector3 local = bone.InverseTransformPoint(child.position);
                cap.direction = AbsDominantAxis(local);
                cap.height = Mathf.Max(local.magnitude, cap.radius * 2f);
                cap.center = local * 0.5f;
            }
            else cap.height = cap.radius * 3f;
            cap.enabled = false;
        }

        private static void AddSphere(Transform bone, float radius, Transform self, Transform from)
        {
            var s = bone.gameObject.AddComponent<SphereCollider>();
            s.radius = Mathf.Max(0.03f, radius);
            if (from != null) s.center = bone.InverseTransformPoint(self.position + (self.position - from.position).normalized * radius);
            s.enabled = false;
        }

        private static void AddJoint(Transform bone, Rigidbody parent, float twist, float swing1, float swing2)
        {
            var j = bone.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = parent;
            j.enablePreprocessing = false;
            j.enableProjection = true;
            j.lowTwistLimit = new SoftJointLimit { limit = -twist };
            j.highTwistLimit = new SoftJointLimit { limit = twist };
            j.swing1Limit = new SoftJointLimit { limit = swing1 };
            j.swing2Limit = new SoftJointLimit { limit = swing2 };
        }

        private void SetRagdoll(bool on, Vector3 velocity)
        {
            foreach (var rb in _bodies)
            {
                if (rb == null) continue;
                if (rb.TryGetComponent<Collider>(out var col)) col.enabled = on;
                rb.isKinematic = !on;
                rb.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
                if (on)
                {
                    rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                    rb.linearVelocity = velocity;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }

        // --- Utilitaires ---

        private static int AbsDominantAxis(Vector3 v)
        {
            v = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (v.x >= v.y && v.x >= v.z) return 0;
            return v.y >= v.z ? 1 : 2;
        }

        private Transform Find(string bone) => BoneUtils.Find(transform, bone);

        private void Disable<T>() where T : Behaviour
        {
            if (TryGetComponent<T>(out var c)) c.enabled = false;
        }
    }
}
