using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// IK analytique deux os (two-bone), 100% C#, sans package Animation Rigging.
    /// Résout une chaîne upper -> lower -> end pour atteindre une cible, avec un "pole"
    /// (vecteur indice) qui contrôle la direction de pliure du coude.
    ///
    /// Positionnement uniquement : l'orientation finale de la main est laissée à l'appelant.
    /// À appeler en LateUpdate, APRÈS que le modèle ait suivi le Rigidbody.
    /// </summary>
    public static class TwoBoneIK
    {
        /// <param name="upper">Os racine (ex: bras / upper arm).</param>
        /// <param name="lower">Os intermédiaire (ex: avant-bras), enfant de upper.</param>
        /// <param name="end">Effecteur (ex: main), enfant de lower.</param>
        /// <param name="targetPos">Position monde visée par l'effecteur.</param>
        /// <param name="poleHint">Position monde indiquant le côté vers lequel le coude plie.</param>
        /// <param name="reachClamp">Fraction max de l'allonge totale (évite le bras tendu/instable).</param>
        public static void Solve(Transform upper, Transform lower, Transform end,
                                 Vector3 targetPos, Vector3 poleHint, float reachClamp = 0.999f)
        {
            if (upper == null || lower == null || end == null) return;

            Vector3 upperPos = upper.position;
            float upperLen = Vector3.Distance(upperPos, lower.position);
            float lowerLen = Vector3.Distance(lower.position, end.position);
            float chainLen = upperLen + lowerLen;
            if (chainLen < 1e-5f) return;

            Vector3 toTarget = targetPos - upperPos;
            float targetDist = toTarget.magnitude;
            if (targetDist < 1e-5f) return;

            // On borne la distance dans l'intervalle atteignable [|a-b|, a+b].
            targetDist = Mathf.Clamp(targetDist,
                Mathf.Abs(upperLen - lowerLen) + 1e-3f,
                chainLen * reachClamp);

            Vector3 targetDir = toTarget / toTarget.magnitude;

            // Angle au niveau de l'épaule (loi des cosinus).
            float cosUpper = (upperLen * upperLen + targetDist * targetDist - lowerLen * lowerLen)
                             / (2f * upperLen * targetDist);
            cosUpper = Mathf.Clamp(cosUpper, -1f, 1f);
            float upperAngle = Mathf.Acos(cosUpper) * Mathf.Rad2Deg;

            // Axe de pliure : plan défini par la direction cible et le pole.
            Vector3 poleDir = poleHint - upperPos;
            Vector3 bendAxis = Vector3.Cross(targetDir, poleDir);
            if (bendAxis.sqrMagnitude < 1e-6f)
            {
                bendAxis = Vector3.Cross(targetDir, Vector3.up);
                if (bendAxis.sqrMagnitude < 1e-6f)
                    bendAxis = Vector3.Cross(targetDir, Vector3.right);
            }
            bendAxis.Normalize();

            // Direction de l'os supérieur = cible inclinée vers le pole de upperAngle degrés.
            Vector3 upperDir = Quaternion.AngleAxis(upperAngle, bendAxis) * targetDir;
            Vector3 elbowPos = upperPos + upperDir * upperLen;

            // Aligne d'abord l'os supérieur (déplace le coude et la main),
            // puis l'os inférieur (déplace la main sur la cible).
            AlignBone(upper, lower, elbowPos);
            AlignBone(lower, end, targetPos);
        }

        /// <summary>
        /// Fait pointer un os vers une cible en réorientant son axe "vers l'enfant",
        /// quel que soit l'axe local du squelette (générique, marche avec Mixamo).
        /// </summary>
        private static void AlignBone(Transform bone, Transform child, Vector3 childTargetPos)
        {
            Vector3 currentDir = child.position - bone.position;
            Vector3 desiredDir = childTargetPos - bone.position;
            if (currentDir.sqrMagnitude < 1e-8f || desiredDir.sqrMagnitude < 1e-8f) return;
            Quaternion delta = Quaternion.FromToRotation(currentDir, desiredDir);
            bone.rotation = delta * bone.rotation;
        }
    }
}
