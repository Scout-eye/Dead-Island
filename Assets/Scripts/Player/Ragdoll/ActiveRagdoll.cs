using System.Collections.Generic;
using UnityEngine;

namespace Game.Player.Ragdoll
{
    /// <summary>Os pilotables du ragdoll, identifiés pour le <see cref="RagdollPoseDriver"/>.</summary>
    public enum RagdollPart
    {
        Hips, Spine, Spine1, Spine2, Neck, Head,
        LeftUpperArm, LeftForeArm, LeftHand,
        RightUpperArm, RightForeArm, RightHand,
        LeftUpLeg, LeftLeg, LeftFoot,
        RightUpLeg, RightLeg, RightFoot
    }

    /// <summary>
    /// Construit (au réveil) et possède le ragdoll PHYSIQUE du joueur : chaque os du squelette
    /// Mixamo reçoit un Rigidbody + un collider + un ConfigurableJoint relié à son parent. Le corps
    /// est physique EN PERMANENCE (contrairement à l'ancien <c>PlayerRagdoll</c> qui ne s'activait
    /// qu'à la mort). C'est l'inversion de design "façon PEAK" : le corps visible EST la simulation.
    ///
    /// Ce composant ne décide RIEN du gameplay : il expose les Rigidbodies/joints et une API pour
    /// piloter la rotation cible de chaque os. La locomotion, l'équilibre, la pose et les mains sont
    /// des composants séparés (responsabilité unique).
    ///
    /// Owner : os dynamiques (simulés). Remote : os kinematic (pilotés réseau — itération future).
    /// Mort : <see cref="SetMotorsEnabled"/>(false) coupe les drives -> le corps s'effondre (passif).
    /// </summary>
    [DefaultExecutionOrder(-100)] // construit avant tous les autres composants du controller
    [DisallowMultipleComponent]
    public sealed class ActiveRagdoll : MonoBehaviour
    {
        [Header("Squelette")]
        [Tooltip("Racine du modèle VISIBLE (où sont les os physiques). Évite de confondre avec l'AnimRig invisible.")]
        [SerializeField] private Transform _skeletonRoot;

        [Header("Drive angulaire des joints (raideur de la 'musculature')")]
        [Tooltip("Raideur du ressort qui tire chaque os vers sa pose cible. Doit tenir le poids du corps " +
                 "sur les jambes : plus haut = plus ferme (s'enfonce moins), trop haut = rigide.")]
        [SerializeField] private float _driveSpring = 1000f;
        [Tooltip("Amortissement du drive (évite l'oscillation). ~10% du spring est un bon départ.")]
        [SerializeField] private float _driveDamper = 100f;
        [Tooltip("Force max appliquée par le drive (borne, évite l'explosion numérique).")]
        [SerializeField] private float _driveMaxForce = 9000f;

        private struct Part
        {
            public Transform T;
            public Rigidbody Body;
            public ConfigurableJoint Joint;       // null pour le bassin (racine physique)
            public Quaternion StartLocalRotation;  // pose de bind, pour SetTargetRotationLocal
        }

        private readonly Dictionary<RagdollPart, Part> _parts = new Dictionary<RagdollPart, Part>();
        private readonly List<Rigidbody> _bodies = new List<Rigidbody>();
        private readonly List<ConfigurableJoint> _joints = new List<ConfigurableJoint>();
        private bool _built;
        private bool _isOwner = true;
        private float _height = 1.8f;

        // --- Accès exposés aux autres composants du controller ---
        public bool IsBuilt => _built;
        public Rigidbody Pelvis => Get(RagdollPart.Hips);
        public Rigidbody LeftHand => Get(RagdollPart.LeftHand);
        public Rigidbody RightHand => Get(RagdollPart.RightHand);

        public Rigidbody Get(RagdollPart part) => _parts.TryGetValue(part, out var p) ? p.Body : null;
        public Transform GetTransform(RagdollPart part) => _parts.TryGetValue(part, out var p) ? p.T : null;

        private void Awake()
        {
            Build();
            ApplyOwnership();
        }

        public void SetOwner(bool owner)
        {
            _isOwner = owner;
            ApplyOwnership();
        }

        private void ApplyOwnership()
        {
            // Remote : kinematic (sera piloté par interpolation réseau dans une itération future).
            foreach (var rb in _bodies)
            {
                if (rb == null) continue;
                rb.isKinematic = !_isOwner;
            }
        }

        /// <summary>
        /// Pilote la rotation LOCALE cible d'un os (appelé chaque FixedUpdate par le PoseDriver / HandReach).
        /// </summary>
        public void DrivePart(RagdollPart part, Quaternion targetLocalRotation)
        {
            if (!_parts.TryGetValue(part, out var p) || p.Joint == null) return;
            p.Joint.SetTargetRotationLocal(targetLocalRotation, p.StartLocalRotation);
        }

        /// <summary>
        /// Active/coupe la "musculature" : à false, les drives tombent à zéro et le corps devient un
        /// ragdoll passif (s'effondre). Appelé à la mort.
        /// </summary>
        public void SetMotorsEnabled(bool enabled)
        {
            float spring = enabled ? _driveSpring : 0f;
            float damper = enabled ? _driveDamper : 0.2f;
            foreach (var j in _joints)
            {
                if (j == null) continue;
                var drive = j.slerpDrive;
                drive.positionSpring = spring;
                drive.positionDamper = damper;
                j.slerpDrive = drive;
            }
        }

        // --- Construction ---------------------------------------------------

        private void Build()
        {
            if (_built) return;

            var hips = Find("Hips");
            if (hips == null)
            {
                Debug.LogWarning("[ActiveRagdoll] Os 'Hips' introuvable — ragdoll non construit.", this);
                return;
            }

            var spine = Find("Spine");
            var spine1 = Find("Spine1");
            var spine2 = Find("Spine2");
            var neck = Find("Neck");
            var head = Find("Head");
            var lArm = Find("LeftArm"); var lFore = Find("LeftForeArm"); var lHand = Find("LeftHand");
            var rArm = Find("RightArm"); var rFore = Find("RightForeArm"); var rHand = Find("RightHand");
            var lUpLeg = Find("LeftUpLeg"); var lLeg = Find("LeftLeg"); var lFoot = Find("LeftFoot");
            var rUpLeg = Find("RightUpLeg"); var rLeg = Find("RightLeg"); var rFoot = Find("RightFoot");

            if (head != null && lFoot != null) _height = Mathf.Max(0.5f, head.position.y - lFoot.position.y);

            // --- Tronc (chaîne portante) ---
            var hipsP = AddRoot(RagdollPart.Hips, hips, 12f);
            AddCapsule(hips, spine, 0.13f);

            var spineP = AddBone(RagdollPart.Spine, spine, hipsP, 10f, twist: 15f, swing: 20f);
            AddCapsule(spine, spine1, 0.13f);

            var spine1P = AddBone(RagdollPart.Spine1, spine1, spineP, 9f, twist: 12f, swing: 18f);
            AddCapsule(spine1, spine2, 0.14f);

            var spine2P = AddBone(RagdollPart.Spine2, spine2, spine1P, 5f, twist: 10f, swing: 15f);
            AddCapsule(spine2, neck, 0.15f);

            var chest = spine2P != null ? spine2P : (spine1P != null ? spine1P : hipsP);

            var neckP = AddBone(RagdollPart.Neck, neck, chest, 3f, twist: 25f, swing: 25f);
            AddCapsule(neck, head, 0.05f);

            AddBone(RagdollPart.Head, head, neckP != null ? neckP : chest, 5f, twist: 30f, swing: 30f);
            AddSphere(head, 0.11f);

            // --- Bras (épaule très souple, coude biaisé charnière) ---
            BuildLimb(RagdollPart.LeftUpperArm, RagdollPart.LeftForeArm, RagdollPart.LeftHand,
                      lArm, lFore, lHand, chest, isLeg: false);
            BuildLimb(RagdollPart.RightUpperArm, RagdollPart.RightForeArm, RagdollPart.RightHand,
                      rArm, rFore, rHand, chest, isLeg: false);

            // --- Jambes (hanche moyenne, genou biaisé charnière) ---
            BuildLimb(RagdollPart.LeftUpLeg, RagdollPart.LeftLeg, RagdollPart.LeftFoot,
                      lUpLeg, lLeg, lFoot, hipsP, isLeg: true);
            BuildLimb(RagdollPart.RightUpLeg, RagdollPart.RightLeg, RagdollPart.RightFoot,
                      rUpLeg, rLeg, rFoot, hipsP, isLeg: true);

            // Les os d'une même chaîne ne se collisionnent pas entre eux (sinon jitter permanent).
            IgnoreSelfCollisions();

            _built = true;
        }

        private void BuildLimb(RagdollPart upperId, RagdollPart lowerId, RagdollPart tipId,
                               Transform upper, Transform lower, Transform tip, Rigidbody parent, bool isLeg)
        {
            if (upper == null || lower == null) return;

            // Bras : le bind Mixamo est en T-pose (bras à l'horizontale). On les pose vers le bas
            // AVANT de créer les joints -> le repos des joints devient cette pose naturelle (aucun
            // pilotage par frame nécessaire pour que les bras pendent le long du corps).
            if (!isLeg)
            {
                float side = upper.position.x < transform.position.x ? -1f : 1f;
                Vector3 downOut = (Vector3.down + transform.forward * 0.12f + transform.right * side * 0.16f).normalized;
                PoseBoneToward(upper, lower, downOut);
                Vector3 downFwd = (Vector3.down + transform.forward * 0.28f + transform.right * side * 0.06f).normalized;
                PoseBoneToward(lower, tip, downFwd);
            }

            float upperMass = isLeg ? 7f : 2.5f;
            float lowerMass = isLeg ? 3.5f : 1.5f;
            float tipMass = isLeg ? 1.8f : 0.8f;
            float upperR = isLeg ? 0.09f : 0.06f;
            float lowerR = isLeg ? 0.07f : 0.045f;

            // Épaule / hanche : amplitude large.
            var upperP = AddBone(upperId, upper, parent, upperMass,
                                 twist: isLeg ? 25f : 40f, swing: isLeg ? 50f : 75f);
            AddCapsule(upper, lower, upperR);

            // Coude / genou : biaisé charnière (gros débattement sur X, faible sur Y/Z).
            var lowerP = AddHinge(lowerId, lower, upperP, lowerMass, hinge: 95f);
            AddCapsule(lower, tip, lowerR);

            if (tip != null)
            {
                AddBone(tipId, tip, lowerP, tipMass, twist: 20f, swing: 25f);
                if (isLeg) AddBox(tip, new Vector3(0.09f, 0.06f, 0.2f), new Vector3(0f, -0.03f, 0.06f));
                else AddSphere(tip, 0.05f);
            }
        }

        // --- Helpers de construction ---------------------------------------

        private Rigidbody AddRoot(RagdollPart id, Transform t, float mass)
        {
            var rb = AddRigidbody(t, mass);
            _parts[id] = new Part { T = t, Body = rb, Joint = null, StartLocalRotation = t.localRotation };
            return rb;
        }

        private Rigidbody AddBone(RagdollPart id, Transform t, Rigidbody parent, float mass,
                                  float twist, float swing)
        {
            if (t == null || parent == null) return null;
            var rb = AddRigidbody(t, mass);
            var joint = AddJoint(t, parent, twist, swing, swing);
            _parts[id] = new Part { T = t, Body = rb, Joint = joint, StartLocalRotation = t.localRotation };
            return rb;
        }

        private Rigidbody AddHinge(RagdollPart id, Transform t, Rigidbody parent, float mass, float hinge)
        {
            if (t == null || parent == null) return null;
            var rb = AddRigidbody(t, mass);
            // Charnière : grand débattement sur l'axe principal, faible sur les deux autres
            // (>=8° conseillé par la doc Unity, sous 5° -> instabilité du solveur).
            var joint = AddJoint(t, parent, twist: hinge, swing1: 8f, swing2: 8f);
            _parts[id] = new Part { T = t, Body = rb, Joint = joint, StartLocalRotation = t.localRotation };
            return rb;
        }

        private Rigidbody AddRigidbody(Transform t, float mass)
        {
            if (!t.TryGetComponent<Rigidbody>(out var rb)) rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.angularDamping = 5f;   // calme les rotations d'os (anti-jitter)
            rb.maxAngularVelocity = 20f;
            // Anti-convulsion : empêche l'éjection explosive quand des colliders se chevauchent
            // (spawn / pénétration sol) et stabilise les joints avec plus d'itérations de solveur.
            rb.maxDepenetrationVelocity = 1f;
            rb.solverIterations = 16;
            rb.solverVelocityIterations = 8;
            _bodies.Add(rb);
            return rb;
        }

        private ConfigurableJoint AddJoint(Transform bone, Rigidbody parent, float twist, float swing1, float swing2)
        {
            var j = bone.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = parent;
            j.autoConfigureConnectedAnchor = true;
            j.anchor = Vector3.zero;                 // pivot = origine du bone (= articulation)
            j.enablePreprocessing = false;
            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.1f;
            j.projectionAngle = 20f;

            // Linéaire verrouillé : les os restent solidaires (pas de translation au joint).
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;

            // Angulaire limité (évite les poses impossibles) + drive Slerp vers la pose cible.
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
            j.lowAngularXLimit = new SoftJointLimit { limit = -twist };
            j.highAngularXLimit = new SoftJointLimit { limit = twist };
            j.angularYLimit = new SoftJointLimit { limit = swing1 };
            j.angularZLimit = new SoftJointLimit { limit = swing2 };

            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.slerpDrive = new JointDrive
            {
                positionSpring = _driveSpring,
                positionDamper = _driveDamper,
                maximumForce = _driveMaxForce
            };
            j.targetRotation = Quaternion.identity;

            _joints.Add(j);
            return j;
        }

        private static void AddCapsule(Transform bone, Transform child, float radius)
        {
            if (bone == null) return;
            var cap = bone.gameObject.AddComponent<CapsuleCollider>();
            cap.radius = Mathf.Max(0.02f, radius);
            if (child != null)
            {
                Vector3 local = bone.InverseTransformPoint(child.position);
                float len = local.magnitude;
                cap.direction = AbsDominantAxis(local);
                cap.height = Mathf.Max(len, cap.radius * 2f);
                cap.center = local * 0.5f;
            }
            else
            {
                cap.height = cap.radius * 3f;
            }
        }

        private static void AddSphere(Transform bone, float radius)
        {
            if (bone == null) return;
            var s = bone.gameObject.AddComponent<SphereCollider>();
            s.radius = Mathf.Max(0.03f, radius);
            s.center = new Vector3(0f, radius * 0.5f, 0f);
        }

        private static void AddBox(Transform bone, Vector3 size, Vector3 center)
        {
            if (bone == null) return;
            var b = bone.gameObject.AddComponent<BoxCollider>();
            b.size = size;
            b.center = center;
        }

        /// <summary>Empêche les colliders d'os reliés de se repousser (sinon jitter permanent).</summary>
        private void IgnoreSelfCollisions()
        {
            var cols = new List<Collider>();
            foreach (var rb in _bodies)
                if (rb != null && rb.TryGetComponent<Collider>(out var c)) cols.Add(c);

            for (int i = 0; i < cols.Count; i++)
                for (int k = i + 1; k < cols.Count; k++)
                    Physics.IgnoreCollision(cols[i], cols[k], true);
        }

        /// <summary>
        /// Oriente un os (au build, espace transform) pour que la direction os->enfant pointe vers
        /// <paramref name="worldDir"/>. Indépendant de la convention d'axes Mixamo.
        /// </summary>
        private static void PoseBoneToward(Transform bone, Transform child, Vector3 worldDir)
        {
            if (bone == null || child == null) return;
            Vector3 cur = child.position - bone.position;
            if (cur.sqrMagnitude < 1e-8f || worldDir.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(cur, worldDir) * bone.rotation;
        }

        // --- Utilitaires ---

        private static int AbsDominantAxis(Vector3 v)
        {
            v = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (v.x >= v.y && v.x >= v.z) return 0;
            return v.y >= v.z ? 1 : 2;
        }

        private Transform Find(string bone)
        {
            // Cherche UNIQUEMENT dans le modèle visible (sinon on trouverait aussi les os de l'AnimRig).
            Transform searchRoot = _skeletonRoot != null ? _skeletonRoot : transform;
            foreach (var t in searchRoot.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == bone || n == "mixamorig:" + bone || n.EndsWith(":" + bone)) return t;
            }
            return null;
        }
    }
}
