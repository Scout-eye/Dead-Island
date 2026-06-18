# Dead Island — jeu de survie sociale (prototype)

Jeu de survie sociale multijoueur **FPS physics-based** sur une île procédurale (4–8 joueurs),
dans l'esprit de *Peak* (grimpe physique) × *Galerapagos* (tension sociale). Prototype.

## Stack
- **Unity 6** (URP)
- **C# uniquement**, aucun asset store de gameplay
- **Facepunch.Steamworks** (P2P Steam, `SteamNetworkingSockets`)
- Modèle **Mixamo Y Bot** comme placeholder
- **Aucun Animator / clip** : tout est piloté par la physique et de l'**IK procédural** en C#

## Architecture (assembly definitions)
| Assembly | Dossier | Rôle |
|---|---|---|
| `Game.Player` | `Assets/Scripts/Player/` | Contrôleur joueur (corps, caméra, mains, animation procédurale), état réseau, IK, RemotePlayer |
| `Game.Net` | `Assets/Scripts/Net/` | Steam, lobbies, transport P2P, menu uGUI |
| `Game.World` | `Assets/Scripts/World/` | Génération d'île procédurale, eau, spawn du monde |
| `Game.Player.Editor` | `Assets/Scripts/Editor/` | Outil de build du Player |
| (Assembly-CSharp-Editor) | `Assets/Editor/` | Outil d'ajout d'eau |

**Scripts clés**
- Joueur : `PlayerBody` (Rigidbody en forces, sol/pentes/saut), `PlayerCamera` (yaw corps / pitch tête découplés, head-bob, lean), `PlayerHands` (IK 2 bras vers la pose de repos + balancement de marche), `PlayerProceduralAnimator` (jambes IK à pas plantés, pose aérienne, look buste, balancement des bras), `PlayerInputReader` (New Input System en code), `PlayerState` (snapshot réseau sérialisable), `RemotePlayer` (interpolation), `TwoBoneIK`.
- Réseau : `SteamManager`, `LobbyManager` (seed/joueurs dans les métadonnées), `NetworkManager` (sockets relais, 20 Hz, relais en étoile, spawn), `MainMenu` (Créer / Chercher / Salle d'attente, généré par code).
- Monde : `IslandGenerator` (île en 3 étapes : forme & côte par distance radiale bruitée, altitude pondérée par la distance à la côte), `WaterPlane`, `WorldSpawner` (île + eau + spawn sur la plage).

**Shaders** (`Assets/Shaders/`) : `VertexColorLit` (terrain), `WaterURP` (eau stylisée : vagues + moutons), `CharacterFoam` (perso + écume), `DIWater.hlsl` (fonctions de vague/écume partagées).

## Mise en route
1. **Steam doit tourner** et être connecté. `steam_appid.txt` (= `480`, Spacewar) est à la racine.
2. Installer Facepunch.Steamworks : `Assets/Plugins/Facepunch.Steamworks.Win64.dll` + `steam_api64.dll` (déjà présents).
3. **Construire le prefab joueur** : menu **Tools ▸ Dead Island ▸ Build Player Prefab (Resources)** → crée `Assets/Resources/Player.prefab` (utilisé pour spawner local et distants).
4. **Menu** : dans une scène, ajouter un GameObject vide + le composant `MainMenu`. Lancer → Créer une partie → Salle d'attente → Lancer.
5. Au lancement, le monde (île depuis la seed + eau) est généré et les joueurs spawnent sur la plage.

### Outils éditeur (menu Tools ▸ Dead Island)
- **Build Player From Y Bot** : monte un Player jouable dans la scène (test solo).
- **Build Player Prefab (Resources)** : sauvegarde le Player en prefab pour le réseau.
- **Add Water (URP)** : pose un plan d'eau stylisé, dimensionné sur l'île présente (test).
- `IslandGenerator` (composant) : clic droit ▸ **Régénérer (test)** pour visualiser une île.

### Contrôles
ZQSD/WASD déplacement · Souris regarder · **Shift** courir · **Espace** sauter.

### Tester le multijoueur
Le P2P passe par le client Steam. Pour jouer à deux : **2 machines, 2 comptes Steam différents**
(l'app 480 ne peut pas tourner deux fois sous le même compte). En solo on valide la création de
partie, la génération d'île et le spawn.

## Avancement
- ✅ **Étape 1** — Contrôleur FPS physique + animation procédurale (corps, caméra, mains, jambes, saut)
- ✅ **Étape 2** — Multijoueur P2P Steam (lobbies, transport, menu, interpolation)
- 🟡 **Étape 3** — Île procédurale + eau stylisée (forme/côte/relief OK ; à venir : `IslandPopulator` — feu de camp, mare, pêche, épave)

## TODO connus
- Écume autour du joueur dans l'eau (à régler) + traînée de sillage (`WaterWake`).
- `IslandPopulator` (éléments de gameplay placés via la seed).
- Synchro réseau de l'écume/effets pour tous les joueurs (actuellement local).

---
Prototype — placeholders (primitives, Y Bot). Pas de NavMesh, pas de serveur dédié (P2P pur).
