# Controller PEAK-like (active ragdoll) — notes de passation

> Fichier écrit pour reprendre le travail sur le PC principal (où l'on peut **tester et régler en live** dans Unity). Sur le PC actuel on ne pouvait pas itérer la physique, donc le code a été posé "à l'aveugle" : **il a besoin d'une passe de réglage en éditeur**, pas forcément d'être réécrit.

Projet : Dead Island (jeu de survie coop), Unity **6000.5** (Unity 6).
Objectif : un controller **façon PEAK** — le corps est un **active ragdoll** (physique en permanence) qui **suit une animation**. PAS de feature d'escalade. Style de déplacement : **PEAK** (le corps se tourne vers la direction de marche, pas de strafe).

---

## 1. Le modèle retenu (canonique "leader / follower")

Confirmé par la recherche (Sergio Abreu — github.com/sergioabreu-g/active-ragdolls ; doc Unity *Joint and Ragdoll Stability* ; Landfall/PEAK, Gang Beasts) :

- **L'animation MÈNE, la physique SUIT.** Un squelette animé **invisible** (« AnimRig ») joue les clips (idle/walk/run/jump/relevé). Le corps **physique visible** recopie sa pose via les `ConfigurableJoint.targetRotation`, et **poursuit la position du bassin animé**.
- **Le corps a son vrai poids et se tient sur ses jambes** (les pieds portent via la collision). Pas d'anti-gravité, pas de ressort de hauteur magique.
- **Le déplacement vient du root motion** de l'animation (Walk/Run réimportés avec root motion) : l'AnimRig avance, le corps physique le poursuit → les pieds ne glissent pas.

Erreur historique à NE PAS refaire : on a d'abord essayé "100% procédural sans animation" (→ pantin), puis "pousser le bassin en force" (→ glisse, flottement), puis "anti-gravité + ride-spring" (→ rebond/flottement). Tout ça a été **abandonné**. Le modèle canonique ci-dessus est le bon.

---

## 2. Carte des fichiers (`Assets/Scripts/Player/Ragdoll/`)

| Fichier | Rôle |
|---|---|
| `ActiveRagdoll.cs` | Construit AU RUNTIME le ragdoll physique (un Rigidbody + ConfigurableJoint + collider par os) sur le modèle **visible**. Pose les bras vers le bas au build. `DrivePart()` = pilote la rotation cible d'un joint. `SetMotorsEnabled(false)` = corps mou (mort/chute). `_skeletonRoot` = modèle visible (pour ne pas confondre avec l'AnimRig). |
| `ConfigurableJointExtensions.cs` | Helper `SetTargetRotationLocal` (Michael Stevenson) — convertit une rotation locale désirée en `targetRotation` correcte. |
| `AnimatedReference.cs` | L'AnimRig invisible. Mappe les os, expose `LocalRotation(part)`, `Hips`, `Root`, `RootMotionVelocity`, et le pont Animator (`SetSpeed/SetGrounded/TriggerGetUp`). |
| `RootMotionCatcher.cs` | Sur l'AnimRig : capte la vitesse de root motion (`deltaPosition/dt`) **sans** déplacer le transform (on place l'AnimRig nous-mêmes). |
| `RagdollLocomotion.cs` | Pilote l'AnimRig (leader) : oriente vers la direction de marche (style PEAK), choisit l'allure (Speed param), avance l'AnimRig au root motion **tenu en laisse** près du corps + collé au sol. Saut. Contrat réseau. **Ne touche PAS** le corps physique. |
| `RagdollBalance.cs` | Le corps physique **poursuit** le bassin animé : `ApplyPositionFollow` (position) + `ApplyUprightTorque` (orientation). Sonde de sol. Machine à états **tomber → ragdoll → relevé** (clip dos/ventre). Expose `IsGrounded`/`IsUpright`. |
| `RagdollPoseDriver.cs` | Chaque FixedUpdate : `targetRotation` de chaque joint = rotation locale de l'os animé. (Mains exclues : gérées par HandReach.) |
| `HandReach.cs` | Clic G/D → la main se tend (force sur le Rigidbody de la main) vers le point visé (raycast caméra). Pas de grip. |
| `RagdollDebugOverlay.cs` | **Affiche les métriques clés à l'écran (touche F3)**. Lecture seule, déterministe. Sert à diagnostiquer/régler via captures d'écran. Voir §4bis. |

Adaptés (hors ragdoll) : `PlayerCamera` (suit la tête, rotation monde = regard), `PlayerInputReader`, `PlayerVitals`, `PlayerDeath` (mort → `SetMotorsEnabled(false)` + désactive les contrôleurs + spectateur), `SpectatorController`, `RemotePlayer`/`PlayerState`/`NetworkManager` (synchro ragdoll distante **différée** : un remote n'est que kinematic déplacé par son transform racine).

Éditeur (`Assets/Scripts/Editor/`) :
- `PlayerSetupBuilder.cs` — construit le prefab Player (modèle visible + AnimRig invisible + tous les composants + câblage). Menu **Tools ▸ Dead Island ▸ Build Player Prefab (Resources)**.
- `PlayerAnimatorBuilder.cs` — génère l'AnimatorController depuis les clips + configure root motion + loop. Menu **Tools ▸ Dead Island ▸ Build Player Animator**.

---

## 3. Ordre de build (CRITIQUE)

1. **Tools ▸ Dead Island ▸ Build Player Animator** — D'ABORD. Crée `Assets/Animations/Player/PlayerLocomotion.controller` et ré-importe Walk/Run en root motion. Regarde la **Console** : s'il dit "clip introuvable", un état manque.
2. **Tools ▸ Dead Island ▸ Build Player Prefab (Resources)** — ENSUITE. (Si lancé avant l'étape 1, l'AnimRig n'a pas de controller → un **dialog d'erreur** s'affiche maintenant pour te prévenir.)
3. Remplacer le Player de la scène par `Assets/Resources/Player.prefab`, Play.

Animations présentes dans `Assets/Animations/Player/` : `Idle, Walk, Run, Jump, GetUpBack`. **Manquant : `GetUpFront`** (relevé sur le ventre) → le générateur retombe sur `GetUpBack` en attendant. (`Unused_RunningJump` a été sorti dans `Assets/Animations/`.)

---

## 4. PROBLÈMES SUSPECTÉS (à vérifier en priorité sur le PC principal)

### #-1 — RÉSOLU : controller OK + IL TIENT DEBOUT. On est en TUNING FIN.
Au 26/06 : l'Animator joue, le perso **tient debout (Tilt ~3°, Upright oui)** et **avance** (vu ~3.19 m/s). Head-aim ajouté (la tête tourne vers la caméra, puis le corps). Défauts renforcés : Drive Spring 2200 / Drive Max Force 25000 / Upright Spring 320 / Follow Spring 450.

**Symptômes restants (TUNING, à régler en live sur le PC fixe) :**
- **Pieds qui s'enfoncent** → `PhysicsMaterial` "RagdollGrip" (friction exposée `_gripFriction`, combine Maximum) + `maxDepenetrationVelocity` 1→3. Si ça persiste, ajuster le BoxCollider des pieds (`ActiveRagdoll.BuildLimb` → `AddBox` du tip) : EN PLAY, sélectionner LeftFoot et regarder si le box couvre bien la semelle ; corriger taille/centre.
- **Trébuche quand il avance** → corps tiré vers le leader plus vite que les jambes ne suivent. Pistes : scaler le pas du root motion (`RagdollLocomotion.MoveAnimRig`) ET `animator.speed` ensemble (garde les pieds synchro) ; ou monter Drive Spring ; ou baisser `Leash`.
- **Reste COINCÉ en arrière (Tilt ~56°, pas Upright → ne bouge plus)** → la friction forte + jambes rigides peuvent verrouiller une mauvaise pose. Levier : baisser `Active Ragdoll ▸ Grip Friction` (les pieds se replacent) et/ou monter `Upright Spring`.

**FEEL "pas physique comme PEAK" (la plainte récurrente)** : le corps suit l'anim TROP rigidement (drives forts). PEAK = bras/haut du corps LÂCHES qui ballottent, jambes fermes. Ajouté : **`_armDriveFactor`** (0.2 = bras à 20% de raideur → ballottent). Pour pousser le feel : baisser encore `Arm Drive Factor`, et trouver le Drive Spring le PLUS BAS qui tient encore debout (le secret PEAK = aussi lâche que possible sans tomber). **Ça demande de l'itération live.**

Nouveaux curseurs exposés (réglables) : `ActiveRagdoll ▸ Arm Drive Factor` (0.2), `Grip Friction` (0.8). `RagdollPoseDriver ▸ Pitch Sign` (head-aim).

### #0 — BUG CORRIGÉ : `FindClip` ratait les clips embarqués dans les FBX
`PlayerAnimatorBuilder` utilisait `AssetDatabase.FindAssets("t:AnimationClip")`, qui **ne retourne pas** les clips embarqués dans les FBX Mixamo selon la version d'Unity → `idle`/`walk` "introuvables" → le générateur sortait **avant de créer le controller** → AnimRig sans controller → T-pose, ne tient pas, n'avance pas. **Corrigé** : recherche par fichier sur le disque (`Directory.GetFiles`) + chargement du sous-asset. Au prochain `Build Player Animator`, la Console doit afficher `Clips trouvés … idle:OK walk:OK …` puis `Controller généré`.

**2ᵉ bug corrigé (sauvegarde du controller)** : l'AnimRig était un prefab imbriqué → l'assignation du controller en *override* ne se sauvait pas dans `Player.prefab` (Animator "not playing an AnimatorController" en Play). Corrigé en **décompactant l'AnimRig** (`PrefabUtility.UnpackPrefabInstance`) avant de sauver, + `Build Player Prefab` **génère l'Animator automatiquement** s'il manque (plus de problème d'ordre). Au build, la Console confirme : `[PlayerBuilder] ✓ Controller assigné à l'AnimRig : PlayerLocomotion`. **Refaire Build Player Prefab + remplacer l'instance de scène par le prefab frais.**

### #1 — L'AnimRig joue-t-il vraiment l'animation ? (suspect n°1 de "s'enfonce + n'avance pas + tient pas debout")
Si l'`AnimatorController` n'est pas assigné/joué, les os de l'AnimRig restent en **T-pose**, le ragdoll essaie de tenir une T-pose sous gravité → s'effondre, et il n'y a **aucun root motion** → aucun déplacement. **Symptômes identiques à ce qu'on observe.**
- **Vérif** : en Play, sélectionne l'enfant `AnimRig` du Player → son `Animator` doit avoir `PlayerLocomotion` en controller et un state actif. Ou active temporairement son `SkinnedMeshRenderer` (désactivé au build) pour VOIR si le squelette invisible s'anime (idle/marche).
- Si T-pose → relancer Build Player Animator (corriger les clips manquants signalés en Console) PUIS rebuild prefab.

### #2 — Root motion réellement actif sur Walk/Run ?
- **Vérif** : `Walk.fbx` → onglet *Animation* → *Root Transform Position (XZ)* → **"Bake Into Pose" doit être DÉCOCHÉ**. (Build Player Animator le fait via `ModelImporterClipAnimation.lockRootPositionXZ=false`.) Sinon `RootMotionVelocity` = 0 → l'AnimRig n'avance pas → le corps non plus.

### #3 — Tenir debout sur les jambes (le vrai morceau de tuning physique)
C'est ICI que la physique se règle en live (impossible à l'aveugle) :
- `ActiveRagdoll ▸ Drive Spring` (défaut 1000) : raideur des "muscles". **Trop bas → jambes molles → s'enfonce/s'effondre.** Monter (1400, 1800…) jusqu'à ce que les jambes tiennent le poids.
- `RagdollBalance ▸ Follow Spring` (défaut 220) / `Follow Damper` (28) : force qui amène le bassin physique sur le bassin animé. Monter Follow Spring si le corps suit mollement ; monter Follow Damper si ça tremble.
- Vérifier les **colliders des pieds** (BoxCollider sur LeftFoot/RightFoot) : doivent être sous le pied et toucher le sol. Si mal placés → enfoncement.

### #4 — Head-aim (la tête ne suit pas la caméra) — NON IMPLÉMENTÉ
Volontairement laissé de côté. La tête suit juste l'animation idle. À ajouter (déterministe, pas de tuning) : dans `RagdollPoseDriver`, après la copie de pose, surcharger la cible du joint `Neck`/`Head` pour pointer vers le `Pitch`/`Yaw` de `PlayerCamera`. Voir la méthode `DriveDirection` géométrique utilisée dans une version précédente (git log) comme base.

### #5 — Avertissement "2 audio listeners"
La scène de test a un AudioListener sur la Main Camera ET sur le CameraRig du Player. Désactiver celui de la Main Camera pour le test.

---

## 4bis. Debug overlay (touche F3) — À UTILISER pour diagnostiquer

Le prefab embarque `RagdollDebugOverlay`. En Play, il affiche en haut à gauche (F3 pour masquer) :

| Ligne | Ce que ça dit | Valeur saine |
|---|---|---|
| `Grounded / Upright / Downed` | état physique | Grounded **oui**, Upright **oui**, Downed **non** |
| `Tilt` | inclinaison du corps vs verticale | **< 35°** (sinon il tombe / ne marche pas) |
| `Vitesse corps` | vitesse réelle de déplacement | ~1.4 en marche, ~0 à l'arrêt |
| `AnimRig prêt / Controller` | **diagnostic n°1** | Controller **OK** (si MANQUANT → T-pose, cf §4 #1) |
| `Clip joué / Speed param` | l'Animator tourne-t-il ? | un clip (Idle/Walk…) ≠ "(aucun)" |
| `Root motion` | **diagnostic n°2** | ~1.4 en marche (si 0 en marchant → §4 #2) |
| `Erreur de poursuite` | distance bassin physique ↔ animé | **faible** (< ~0.15 m) |

L'overlay **annote lui-même** les anomalies en rouge (controller manquant, root motion à zéro en marchant). **→ Pour les captures d'écran à m'envoyer : appuie une fois sur les touches de déplacement et fais le screenshot avec l'overlay visible.** Les chiffres me disent immédiatement où ça coince.

## 5. Guide de réglage rapide (symptôme → curseur, tout en LIVE / Play mode)

| Symptôme | Composant ▸ curseur | Sens |
|---|---|---|
| Jambes molles / s'enfonce / s'effondre | `Active Ragdoll ▸ Drive Spring` | ↑ |
| Tremble / vibre | `Active Ragdoll ▸ Drive Spring` | ↓ (ou ↑ Drive Damper) |
| Suit mollement / lent à se déplacer | `Ragdoll Balance ▸ Follow Spring` | ↑ |
| Tremble en suivant | `Ragdoll Balance ▸ Follow Damper` | ↑ |
| Penche / ne se redresse pas | `Ragdoll Balance ▸ Upright Spring` | ↑ |
| Patine au virage / "élastique" | `Locomotion ▸ Leash` | ↓ |
| Tourne trop lentement vers la direction | `Locomotion ▸ Turn Speed` | ↑ |
| Marche trop lente/rapide | c'est la vitesse du **clip** (root motion). Régler dans Mixamo ou via `animator.speed`. |

> ⚠️ Les réglages en Play mode sont **perdus à l'arrêt**. Une fois les bonnes valeurs trouvées, les reporter sur le prefab (hors Play) ou les figer comme défauts dans les `[SerializeField]` puis rebuild.

---

## 6. Checklist de démarrage sur le PC principal

1. Ouvrir le projet, laisser compiler (vérifier 0 erreur Console).
2. **Build Player Animator** → vérifier en Console qu'il trouve Idle/Walk/Run/Jump/GetUpBack (pas de "clip introuvable").
3. **Build Player Prefab** (pas de dialog d'erreur "controller introuvable").
4. Mettre le prefab en scène, Play. **Regarder l'overlay (F3)** — il dit tout (§4bis).
5. **Controller = MANQUANT** dans l'overlay ? → revenir à l'étape 2 (Build Player Animator d'abord), rebuild.
6. **Controller OK mais le corps s'effondre** (Upright = non, Tilt > 35°) → monter `Active Ragdoll ▸ Drive Spring` jusqu'à ce qu'il tienne.
7. **Debout mais Root motion = 0 en marchant** → root motion pas actif (§4 #2). Sinon, si Vitesse corps reste 0 → monter `Follow Spring`.
8. Itérer avec le tableau §5. **M'envoyer une capture avec l'overlay visible** = je sais où ça coince.

---

## 7. Pas encore fait (features prévues par le joueur, après que la base tienne)
- ✅ **Head-aim FAIT** : la tête tourne vers la caméra jusqu'à `_maxHeadYaw` (70°), réparti sur Spine2/Neck/Head (RagdollPoseDriver.ApplyLook, delta appliqué en MONDE donc convention-indépendant) ; au-delà le corps pivote (RagdollLocomotion.UpdateFacing, à l'arrêt). Si le pitch (haut/bas) est inversé → `PoseDriver ▸ Pitch Sign = -1`. (Menu "Build Player From Y Bot" retiré : ne reste que "Build Player Prefab".)
- ⚠️ **Tuning en cours** : il TIENT DEBOUT (Tilt ~3°) mais **tombe quand il se déplace** (le mouvement déstabilise). Pistes : baisser la vitesse du clip / monter Follow+Upright pendant le déplacement / amortir l'accélération.
- **Relevé sur le ventre** : récupérer un clip Mixamo "Getting Up (from front)", le nommer `GetUpFront.fbx` dans `Assets/Animations/Player/`, relancer Build Player Animator.
- **Ramasser un objet** : la base HandReach existe (la main se tend). Ajouter l'accroche + parentage de l'objet.
- **Frapper / frapper avec un objet** : ajouter des clips Punch/Swing dans l'Animator (états déclenchés par trigger), le ragdoll les suivra.
- **Synchro multijoueur du ragdoll** : actuellement différée (remote = transform racine interpolé). À traiter quand le solo est solide (sync pelvis + 2 pieds + 2 mains).

---

## 8. Si malgré tout l'active ragdoll continu reste trop instable
Plan B discuté avec le joueur (non retenu pour l'instant, il veut d'abord tester/régler) : base **animée classique fiable** (Animator pilote directement le modèle) + **ragdoll à la demande** (sur coup / chute / mort) qui se relève. Moins "wobble permanent" mais robuste. À ne proposer que si le tuning du modèle canonique ne donne rien après une vraie session de réglage en éditeur.
