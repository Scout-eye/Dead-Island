using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Game.Player;
using Game.World;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Transport P2P via SteamNetworkingSockets (relais Steam, connection-based — pas le vieux
    /// SendP2PPacket déprécié). Le host ouvre une socket relais ; les clients s'y connectent.
    ///
    /// Format de message : [1 octet type][8 octets SteamId expéditeur][payload].
    ///   type 0 = PlayerState (payload = PlayerState sérialisé).
    /// Le host relaie les states reçus aux autres clients (topologie étoile). Envoi à 20 Hz.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkManager : MonoBehaviour
    {
        private const int SendRate = 30;
        private const string PlayerResource = "Player"; // Assets/Resources/Player.prefab
        // NoNagle = envoi immédiat (pas de regroupement) => latence plus basse.
        private const SendType StateSendType = SendType.Unreliable | SendType.NoNagle;

        private enum MsgType : byte { PlayerState = 0, GameEvent = 1 }

        public static NetworkManager Instance { get; private set; }

        /// <summary>Déconnexion subie (ex: hôte perdu). Fournit un message à afficher.</summary>
        public event System.Action<string> Disconnected;
        /// <summary>Retour à la salle d'attente (tous morts) — la connexion reste ouverte.</summary>
        public event System.Action OnReturnedToRoom;

        private const byte EvtReturnToRoom = 1;

        private GameSocket _socket;          // host
        private GameConnection _connection;  // client
        private bool _isHost;
        private bool _running;

        private GameObject _localPlayer;
        private FirstPersonController _localBody;
        private PlayerCamera _localCam;
        private PlayerVitals _localVitals;
        private PlayerInventory _localInventory;

        private bool _worldBuilt;
        private Vector3 _worldCenter;
        private float _worldSize;
        private GameObject _worldRoot;
        private bool _hostLost;

        private readonly Dictionary<ulong, RemotePlayer> _remotes = new Dictionary<ulong, RemotePlayer>();
        private readonly Dictionary<uint, ulong> _connToSteam = new Dictionary<uint, ulong>(); // host: connId -> steamId

        private float _sendTimer;
        private uint _tick;
        private readonly byte[] _sendBuffer = new byte[1 + 8 + PlayerState.Size];

        public static NetworkManager EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[NetworkManager]");
            return go.AddComponent<NetworkManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            var lm = LobbyManager.EnsureExists();
            lm.OnEnteredLobby += HandleEnteredLobby;
            lm.OnGameStart += HandleGameStart;
            lm.OnLeftLobby += LeaveGame;   // quitter le lobby ferme la socket + nettoie
            PlayerVitals.AllDead += HandleAllDead;
        }

        private void OnDestroy()
        {
            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnEnteredLobby -= HandleEnteredLobby;
                LobbyManager.Instance.OnGameStart -= HandleGameStart;
                LobbyManager.Instance.OnLeftLobby -= LeaveGame;
            }
            PlayerVitals.AllDead -= HandleAllDead;
            if (Instance == this) ShutdownTransport();
        }

        // Tous les joueurs sont morts : l'HÔTE fait autorité — il prévient tout le monde et
        // chacun revient en salle d'attente (la connexion reste ouverte pour rejouer).
        private void HandleAllDead()
        {
            if (!_running) return;
            if (LobbyManager.Instance == null || !LobbyManager.Instance.IsHost) return;
            LobbyManager.Instance.SetWaiting();
            BroadcastGameEvent(EvtReturnToRoom);
            ReturnToRoom();
        }

        /// <summary>Retour salle d'attente : détruit monde + joueurs mais GARDE la connexion ouverte.</summary>
        public void ReturnToRoom()
        {
            if (_localPlayer != null) Destroy(_localPlayer);
            _localPlayer = null; _localBody = null; _localCam = null; _localVitals = null; _localInventory = null;

            foreach (var kv in _remotes)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _remotes.Clear();
            _connToSteam.Clear();

            if (_worldRoot != null) Destroy(_worldRoot);
            _worldRoot = null;
            _worldBuilt = false;

            OnReturnedToRoom?.Invoke();
        }

        // Dès qu'on entre dans son propre lobby (hôte), on ouvre la socket d'écoute : elle est ainsi
        // prête bien avant que les clients tentent de se connecter (évite la course au démarrage).
        private void HandleEnteredLobby(Steamworks.Data.Lobby lobby)
        {
            if (LobbyManager.Instance != null && LobbyManager.Instance.IsHost) OpenHostSocket();
        }

        private void HandleGameStart(SteamId hostId)
        {
            // Garde : on ne démarre que si on est bien dans un lobby (évite une partie vide sur signal parasite).
            if (LobbyManager.Instance == null || !LobbyManager.Instance.InLobby) return;

            if (LobbyManager.Instance.IsHost) StartHost();
            else StartClient(hostId);
        }

        /// <summary>Quitte la partie : coupe le réseau, détruit le monde et tous les joueurs.</summary>
        public void LeaveGame()
        {
            ShutdownTransport();

            if (_localPlayer != null) Destroy(_localPlayer);
            _localPlayer = null;
            _localBody = null;
            _localCam = null;
            _localVitals = null;
            _localInventory = null;

            foreach (var kv in _remotes)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _remotes.Clear();
            _connToSteam.Clear();

            if (_worldRoot != null) Destroy(_worldRoot);
            _worldRoot = null;
            _worldBuilt = false;
            _isHost = false;
        }

        private void OpenHostSocket()
        {
            if (_socket != null) return;
            _isHost = true;
            _socket = SteamNetworkingSockets.CreateRelaySocket<GameSocket>();
            _socket.Net = this;
            _running = true;
            Debug.Log("[Net] Socket hôte ouverte (relais) — prête à accepter.");
        }

        public void StartHost()
        {
            OpenHostSocket();      // déjà ouverte à l'entrée du lobby en principe
            SpawnLocalPlayer();    // idempotent
        }

        public void StartClient(SteamId hostId)
        {
            if (_connection == null)
            {
                _isHost = false;
                _connection = SteamNetworkingSockets.ConnectRelay<GameConnection>(hostId);
                _connection.Net = this;
                _running = true;
                Debug.Log($"[Net] Connexion au host {hostId}...");
            }
            SpawnLocalPlayer();    // idempotent (if (_localPlayer != null) return)
        }

        private void Update()
        {
            if (!_running) return;

            _socket?.Receive();
            _connection?.Receive();

            // Déconnexion subie détectée pendant Receive : on nettoie HORS du callback (sûr).
            if (_hostLost)
            {
                _hostLost = false;
                LeaveGame();
                Disconnected?.Invoke("Connexion à l'hôte perdue.");
                return;
            }

            if (_localBody == null) return;
            _sendTimer += Time.deltaTime;
            if (_sendTimer >= 1f / SendRate)
            {
                _sendTimer = 0f;
                SendLocalState();
            }
        }

        // --- Envoi ---

        private void SendLocalState()
        {
            var state = GatherLocalState();
            int o = 0;
            _sendBuffer[o++] = (byte)MsgType.PlayerState;
            BitConverter.TryWriteBytes(new Span<byte>(_sendBuffer, o, 8), SteamClient.SteamId.Value);
            o += 8;
            state.WriteTo(_sendBuffer, o);

            if (_isHost) BroadcastToClients(_sendBuffer, null);
            else _connection?.Connection.SendMessage(_sendBuffer, StateSendType);
        }

        private PlayerState GatherLocalState()
        {
            return new PlayerState
            {
                Tick = _tick++,
                Position = _localBody.NetworkPosition,
                Yaw = _localBody.BodyYaw,
                LookYaw = _localCam != null ? _localCam.LookYaw : _localBody.BodyYaw,
                Pitch = _localCam != null ? _localCam.Pitch : 0f,
                Velocity = _localBody.NetworkVelocity,
                Dead = _localVitals != null && _localVitals.IsDead,
                Grounded = _localBody.IsGrounded,
                Swimming = _localBody.IsSwimming,
                HeldItem = ItemDatabase.GetNetId(_localInventory != null ? _localInventory.SelectedItem : null)
            };
        }

        /// <summary>Envoie un événement de jeu fiable (host -> clients, ou client -> host).</summary>
        private void BroadcastGameEvent(byte eventId)
        {
            var buf = new byte[1 + 8 + 1];
            buf[0] = (byte)MsgType.GameEvent;
            BitConverter.TryWriteBytes(new Span<byte>(buf, 1, 8), SteamClient.SteamId.Value);
            buf[9] = eventId;
            if (_isHost) BroadcastToClients(buf, null);
            else _connection?.Connection.SendMessage(buf, SendType.Reliable);
        }

        /// <summary>Host : envoie à tous les clients sauf éventuellement l'expéditeur d'origine.</summary>
        private void BroadcastToClients(byte[] data, Connection? except)
        {
            if (_socket == null) return;
            foreach (var c in _socket.Connected)
            {
                if (except.HasValue && c.Id == except.Value.Id) continue;
                c.SendMessage(data, StateSendType);
            }
        }

        // --- Réception (appelée par GameSocket / GameConnection) ---

        public void OnData(Connection from, byte[] data, bool onHost)
        {
            if (data == null || data.Length < 9) return;
            var type = (MsgType)data[0];
            ulong sender = BitConverter.ToUInt64(data, 1);
            if (sender == SteamClient.SteamId.Value) return; // jamais soi-même

            if (onHost) _connToSteam[from.Id] = sender;

            if (type == MsgType.PlayerState)
            {
                var state = PlayerState.Deserialize(data, 9);
                GetOrCreateRemote(sender).PushState(state);

                // Host : relaie aux autres clients (étoile).
                if (_isHost) BroadcastToClients(data, from);
            }
            else if (type == MsgType.GameEvent && data.Length >= 10)
            {
                if (data[9] == EvtReturnToRoom) ReturnToRoom();
            }
        }

        public void OnConnectionClosed(Connection connection)
        {
            if (_connToSteam.TryGetValue(connection.Id, out ulong steamId))
            {
                _connToSteam.Remove(connection.Id);
                DestroyRemote(steamId);
            }
        }

        public void OnHostLost()
        {
            // Appelé depuis un callback de connexion : on diffère le nettoyage au prochain Update.
            _hostLost = true;
        }

        // --- Spawn ---

        private void SpawnLocalPlayer()
        {
            if (_localPlayer != null) return;
            BuildWorld();
            var prefab = Resources.Load<GameObject>(PlayerResource);
            if (prefab == null)
            {
                Debug.LogError($"[Net] Prefab introuvable : Resources/{PlayerResource}.prefab " +
                               "(utilise Tools ▸ Dead Island ▸ Build Player Prefab).");
                return;
            }

            int index = LobbyManager.Instance != null ? LobbyManager.Instance.LocalIndex : 0;
            _localPlayer = Instantiate(prefab, ComputeSpawn(index), Quaternion.identity);
            _localPlayer.name = "Player (Local)";
            ConfigurePlayer(_localPlayer, owner: true);
            _localBody = _localPlayer.GetComponent<FirstPersonController>();
            _localCam = _localPlayer.GetComponent<PlayerCamera>();
            _localVitals = _localPlayer.GetComponent<PlayerVitals>();
            _localInventory = _localPlayer.GetComponent<PlayerInventory>();
        }

        private RemotePlayer GetOrCreateRemote(ulong steamId)
        {
            if (_remotes.TryGetValue(steamId, out var existing) && existing != null) return existing;

            var prefab = Resources.Load<GameObject>(PlayerResource);
            // Position initiale sur la plage ; la vraie position viendra du réseau (interpolation).
            var go = Instantiate(prefab, ComputeSpawn(_remotes.Count + 1), Quaternion.identity);
            go.name = $"Player (Remote {steamId})";
            ConfigurePlayer(go, owner: false);
            var rp = go.GetComponent<RemotePlayer>();
            _remotes[steamId] = rp;
            Debug.Log($"[Net] Joueur distant créé : {steamId} ({(_isHost ? "host reçoit client" : "client reçoit relai")}).");
            return rp;
        }

        private void DestroyRemote(ulong steamId)
        {
            if (_remotes.TryGetValue(steamId, out var rp))
            {
                if (rp != null) Destroy(rp.gameObject);
                _remotes.Remove(steamId);
            }
        }

        private static void ConfigurePlayer(GameObject go, bool owner)
        {
            var body = go.GetComponent<FirstPersonController>();
            if (body != null) body.SetOwner(owner);

            SetEnabled(go.GetComponent<PlayerInputReader>(), owner);
            SetEnabled(go.GetComponent<PlayerCamera>(), owner);
            SetEnabled(go.GetComponent<RemotePlayer>(), !owner);

            // La vraie caméra/écoute audio : uniquement le joueur local.
            var cam = go.GetComponentInChildren<Camera>(true);
            if (cam != null) cam.enabled = owner;
            var listener = go.GetComponentInChildren<AudioListener>(true);
            if (listener != null) listener.enabled = owner;
            // L'avatar local reste visible (on voit son corps) ; FirstPersonView cache juste la tête.
        }

        private static void SetEnabled(Behaviour b, bool enabled)
        {
            if (b != null) b.enabled = enabled;
        }

        /// <summary>Génère l'île procédurale (depuis la seed du lobby) + l'eau. Une seule fois par client.</summary>
        private void BuildWorld()
        {
            if (_worldBuilt) return;
            int seed = LobbyManager.Instance != null ? LobbyManager.Instance.Seed : 12345;
            int players = LobbyManager.Instance != null ? LobbyManager.Instance.PlayerCount : 1;
            if (seed == 0) seed = 12345;

            var world = WorldSpawner.Build(seed, players);
            _worldCenter = world.Center;
            _worldSize = world.Size;
            _worldRoot = world.Root;
            _worldBuilt = true;
        }

        /// <summary>Point de spawn sur la plage : angle d'or pour répartir les joueurs autour de l'île.</summary>
        private Vector3 ComputeSpawn(int index)
        {
            float angle = index * 137.508f;
            return WorldSpawner.FindBeachSpawn(_worldCenter, _worldSize, angle);
        }

        private void ShutdownTransport()
        {
            _running = false;
            try { _socket?.Close(); } catch { /* ignore */ }
            try { _connection?.Close(); } catch { /* ignore */ }
            _socket = null;
            _connection = null;
        }

        public static byte[] ToBytes(IntPtr data, int size)
        {
            var arr = new byte[size];
            Marshal.Copy(data, arr, 0, size);
            return arr;
        }

        // --- Interfaces socket Facepunch ---

        private class GameSocket : SocketManager
        {
            public NetworkManager Net;

            public override void OnConnecting(Connection connection, ConnectionInfo info)
            {
                connection.Accept(); // accepte toutes les connexions du lobby
            }

            public override void OnConnected(Connection connection, ConnectionInfo info)
            {
                Debug.Log($"[Net] Client connecté : {info.Identity}");
            }

            public override void OnDisconnected(Connection connection, ConnectionInfo info)
            {
                Net.OnConnectionClosed(connection);
            }

            public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data,
                                           int size, long messageNum, long recvTime, int channel)
            {
                Net.OnData(connection, ToBytes(data, size), onHost: true);
            }
        }

        private class GameConnection : ConnectionManager
        {
            public NetworkManager Net;

            public override void OnConnected(ConnectionInfo info)
            {
                Debug.Log("[Net] Connecté au host.");
            }

            public override void OnDisconnected(ConnectionInfo info)
            {
                Net.OnHostLost();
            }

            public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
            {
                Net.OnData(default, ToBytes(data, size), onHost: false);
            }
        }
    }
}
