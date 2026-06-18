using System;
using Steamworks;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Initialise Facepunch.Steamworks et pompe les callbacks. Singleton global, persistant.
    /// AppId 480 = Spacewar (app de test Steam). Steam doit être lancé et connecté.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class SteamManager : MonoBehaviour
    {
        public const uint AppId = 480;

        public static SteamManager Instance { get; private set; }
        public static bool Initialized { get; private set; }

        public SteamId LocalId => SteamClient.SteamId;
        public string LocalName => SteamClient.Name;

        /// <summary>Crée le SteamManager s'il n'existe pas encore (appelé par le bootstrap/menu).</summary>
        public static SteamManager EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[SteamManager]");
            return go.AddComponent<SteamManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (Initialized) return;
            try
            {
                SteamClient.Init(AppId, false); // false => on appelle RunCallbacks nous-mêmes
                Initialized = true;
                // Pré-initialise le réseau relais Steam (connexions P2P plus rapides).
                SteamNetworkingUtils.InitRelayNetworkAccess();
                Debug.Log($"[Steam] Init OK — {SteamClient.Name} ({SteamClient.SteamId})");
            }
            catch (Exception e)
            {
                Initialized = false;
                Debug.LogError($"[Steam] Init échouée : {e.Message}\nSteam est-il lancé et connecté ?");
            }
        }

        private void Update()
        {
            if (Initialized) SteamClient.RunCallbacks();
        }

        private void OnApplicationQuit() => Shutdown();
        private void OnDestroy() { if (Instance == this) Shutdown(); }

        private static void Shutdown()
        {
            if (!Initialized) return;
            Initialized = false;
            try { SteamClient.Shutdown(); } catch { /* ignore */ }
        }
    }
}
