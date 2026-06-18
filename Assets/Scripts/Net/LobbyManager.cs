using System;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Gère les lobbies Steam : créer, lister (filtrés par clé de jeu), rejoindre, quitter,
    /// et signaler le lancement de la partie. La seed de l'île est stockée dans les métadonnées
    /// pour que tous les clients génèrent la même île (étape 3).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyManager : MonoBehaviour
    {
        // Filtre : on ne liste que NOS lobbies (480/Spacewar est partagé par tout le monde).
        public const string KeyGame = "di_game";
        public const string ValGame = "deadisland";
        public const string KeyName = "di_name";
        public const string KeySeed = "di_seed";
        public const string KeyState = "di_state"; // "waiting" | "playing"

        public static LobbyManager Instance { get; private set; }

        public Lobby? Current { get; private set; }
        public bool InLobby => Current.HasValue;
        public bool IsHost => Current.HasValue && Current.Value.Owner.Id == SteamClient.SteamId;
        public int Seed => Current.HasValue && int.TryParse(Current.Value.GetData(KeySeed), out var s) ? s : 0;
        public int PlayerCount => Current.HasValue ? Mathf.Max(1, Current.Value.MemberCount) : 1;

        /// <summary>Index du joueur local parmi les membres (pour répartir les points de spawn).</summary>
        public int LocalIndex
        {
            get
            {
                if (!Current.HasValue) return 0;
                int i = 0;
                foreach (var m in Current.Value.Members)
                {
                    if (m.Id == SteamClient.SteamId) return i;
                    i++;
                }
                return 0;
            }
        }

        public event Action<Lobby> OnEnteredLobby;
        public event Action OnLeftLobby;
        public event Action OnMembersChanged;
        /// <summary>Lancement de partie : fournit l'identifiant Steam du host à rejoindre.</summary>
        public event Action<SteamId> OnGameStart;

        public static LobbyManager EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[LobbyManager]");
            return go.AddComponent<LobbyManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            SteamMatchmaking.OnLobbyEntered += HandleEntered;
            SteamMatchmaking.OnLobbyMemberJoined += HandleMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave += HandleMemberLeave;
            SteamMatchmaking.OnLobbyGameCreated += HandleGameCreated;
        }

        private void OnDisable()
        {
            SteamMatchmaking.OnLobbyEntered -= HandleEntered;
            SteamMatchmaking.OnLobbyMemberJoined -= HandleMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave -= HandleMemberLeave;
            SteamMatchmaking.OnLobbyGameCreated -= HandleGameCreated;
        }

        public async void CreateLobby(string lobbyName, int maxPlayers = 8)
        {
            try
            {
                var result = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);
                if (!result.HasValue)
                {
                    Debug.LogError("[Lobby] Création échouée.");
                    return;
                }

                var lobby = result.Value;
                lobby.SetPublic();
                lobby.SetJoinable(true);
                lobby.SetData(KeyGame, ValGame);
                lobby.SetData(KeyName, string.IsNullOrWhiteSpace(lobbyName) ? $"Île de {SteamClient.Name}" : lobbyName);
                lobby.SetData(KeySeed, UnityEngine.Random.Range(1, int.MaxValue).ToString());
                lobby.SetData(KeyState, "waiting");
                // OnLobbyEntered se déclenche automatiquement pour le créateur.
            }
            catch (Exception e) { Debug.LogError($"[Lobby] CreateLobby: {e.Message}"); }
        }

        public async Task<Lobby[]> RequestLobbies()
        {
            try
            {
                var list = await SteamMatchmaking.LobbyList
                    .WithKeyValue(KeyGame, ValGame)
                    .WithMaxResults(50)
                    .RequestAsync();
                return list ?? Array.Empty<Lobby>();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Lobby] RequestLobbies: {e.Message}");
                return Array.Empty<Lobby>();
            }
        }

        public async void JoinLobby(Lobby lobby)
        {
            try
            {
                var enter = await lobby.Join();
                if (enter != RoomEnter.Success)
                    Debug.LogError($"[Lobby] Join échoué : {enter}");
                // OnLobbyEntered fera le reste si succès.
            }
            catch (Exception e) { Debug.LogError($"[Lobby] JoinLobby: {e.Message}"); }
        }

        public void LeaveLobby()
        {
            if (!Current.HasValue) return;
            Current.Value.Leave();
            Current = null;
            OnLeftLobby?.Invoke();
        }

        /// <summary>Host uniquement : passe le lobby en "playing" et signale le lancement à tous.</summary>
        public void StartGame()
        {
            if (!IsHost || !Current.HasValue) return;
            Current.Value.SetData(KeyState, "playing");
            // Déclenche OnLobbyGameCreated chez tous les membres, avec l'id du host.
            Current.Value.SetGameServer(SteamClient.SteamId);
        }

        // --- Callbacks Steam ---

        private void HandleEntered(Lobby lobby)
        {
            Current = lobby;
            OnEnteredLobby?.Invoke(lobby);
            OnMembersChanged?.Invoke();
        }

        private void HandleMemberJoined(Lobby lobby, Friend friend) => OnMembersChanged?.Invoke();
        private void HandleMemberLeave(Lobby lobby, Friend friend) => OnMembersChanged?.Invoke();

        private void HandleGameCreated(Lobby lobby, uint ip, ushort port, SteamId serverId)
        {
            OnGameStart?.Invoke(serverId);
        }
    }
}
