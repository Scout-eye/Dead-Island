# Archive — Controller PHYSIQUE (active ragdoll façon PEAK)

Travail conservé ici, **inactif** (hors `Assets/`, donc Unity ne le compile pas). C'est un controller où le personnage est un **active ragdoll** : le corps physique suit une animation et réagit à la physique, façon PEAK / Gang Beasts.

> Détail complet du raisonnement, des bugs résolus et du tuning : voir `RAGDOLL_SESSION_NOTES.md` (copié ici).

## Ce que c'est (en 30 s)
- Un squelette **animé invisible** (AnimRig) joue les clips Mixamo (idle/walk/run/jump/relevé).
- Le **corps physique visible** (un Rigidbody + ConfigurableJoint + collider par os, construits au runtime) **recopie la pose animée** (joints pilotés en `targetRotation`) et **poursuit la position** du bassin animé.
- Le corps a son vrai poids, se tient sur ses jambes (collision), réagit aux chocs. Bras volontairement lâches (feel physique). Tombe/se relève via clips dos/ventre.

## Fichiers (dans `Ragdoll/` et `Editor/`)
| Fichier | Rôle |
|---|---|
| `ActiveRagdoll.cs` | Construit le ragdoll physique au runtime (rigidbodies + ConfigurableJoints + colliders + friction), pilote les joints, bras lâches. |
| `ConfigurableJointExtensions.cs` | Helper `SetTargetRotationLocal` (Michael Stevenson). |
| `AnimatedReference.cs` | L'AnimRig invisible : mappe les os, expose pose locale/monde + root motion + params Animator. |
| `RootMotionCatcher.cs` | Capte la vitesse de root motion sans déplacer l'AnimRig. |
| `RagdollLocomotion.cs` | Pilote l'AnimRig "leader" (facing, allure, root motion en laisse). Contrat réseau. |
| `RagdollBalance.cs` | Le bassin poursuit le bassin animé (position + posture) + machine tomber/relever. |
| `RagdollPoseDriver.cs` | Recopie la pose animée dans les joints + head-aim (tête vers caméra). |
| `HandReach.cs` | Clic → la main se tend vers le viseur. |
| `RagdollDebugOverlay.cs` | Overlay F3 (métriques temps réel pour régler). |
| `Editor/PlayerSetupBuilder.cs` | Construit le prefab Player physique. |
| `Editor/PlayerAnimatorBuilder.cs` | Génère l'AnimatorController depuis les clips Mixamo (+ root motion). |

## État au moment de l'archivage
**Fonctionne** : tient debout, s'anime, avance, head-aim, se relève. **Reste du tuning** (le "feel physique" parfait façon PEAK demande de l'itération live) : trouver le Drive Spring le plus bas qui tient encore + bras très lâches ; régler friction des pieds ; lisser le déplacement (trébuche). Tous les curseurs sont exposés et documentés dans `RAGDOLL_SESSION_NOTES.md`.

## Pour le RÉACTIVER plus tard
1. Recopier `Ragdoll/*.cs` dans `Assets/Scripts/Player/Ragdoll/` et `Editor/*.cs` dans `Assets/Scripts/Editor/`.
2. Retirer le controller classique (ou gérer le conflit de noms de composants).
3. Les animations Mixamo sont dans `Assets/Animations/Player/` (partagées — restent dans le projet).
4. `Tools ▸ Dead Island ▸ Build Player Animator` PUIS `Build Player Prefab`.

Les dépendances réseau/vitals/mort avaient été adaptées à ce controller — il faudra re-câbler selon l'état du projet au moment de la réactivation.
