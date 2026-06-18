# Installer Facepunch.Steamworks (prérequis Étape 2)

1. Télécharger la dernière release :
   https://github.com/Facepunch/Facepunch.Steamworks/releases
   Prendre le zip **64 bits** (ex: `Facepunch.Steamworks.<version>.zip`).

2. Copier dans ce dossier `Assets/Plugins/` :
   - `Facepunch.Steamworks.dll`  (assembly managée)
   - `steam_api64.dll`           (natif Windows 64 bits)
   (le zip de Facepunch contient les deux ; on cible Windows uniquement pour l'instant)

3. Dans Unity, sélectionner `steam_api64.dll` et, dans l'inspector d'import :
   - Cocher **Editor** et **Standalone**, CPU = **x86_64**, OS = **Windows**.
   `Facepunch.Steamworks.dll` peut rester en "Any Platform".

4. `steam_appid.txt` (contenant `480`) est déjà à la racine du projet — ne pas le supprimer.

5. **Steam doit être lancé et connecté** pour que l'init fonctionne.

6. Vérifier que la Console Unity est SANS erreur après import.
   Puis prévenir : j'écris ensuite SteamManager / LobbyManager / NetworkManager / RemotePlayer.

Note : 480 (Spacewar) est partagé par tout le monde → on filtrera les lobbies
par une métadonnée de jeu pour ne pas lister les parties des autres.
