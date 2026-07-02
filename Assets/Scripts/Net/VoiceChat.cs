using Steamworks;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Capture micro (voix Steam, micro OUVERT pendant la partie) et envoie les paquets compressés
    /// via <see cref="NetworkManager.SendVoice"/>. Le rendu côté récepteurs est fait par
    /// <see cref="VoicePlayer"/> (proximité + étouffement). Démarré/arrêté par NetworkManager.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceChat : MonoBehaviour
    {
        public static VoiceChat Instance { get; private set; }

        public static void StartRecording()
        {
            if (!SteamClient.IsValid) return;
            if (Instance == null)
            {
                var go = new GameObject("[VoiceChat]");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<VoiceChat>();
            }
            SteamUser.VoiceRecord = true;
        }

        public static void StopRecording()
        {
            if (SteamClient.IsValid) SteamUser.VoiceRecord = false;
        }

        private void Update()
        {
            if (!SteamClient.IsValid || !SteamUser.VoiceRecord) return;
            if (NetworkManager.Instance == null) return;

            // Draine tout ce que Steam a compressé depuis la dernière frame.
            while (SteamUser.HasVoiceData)
            {
                var data = SteamUser.ReadVoiceDataBytes();
                if (data != null && data.Length > 0)
                    NetworkManager.Instance.SendVoice(data);
            }
        }

        private void OnDestroy()
        {
            StopRecording();
            if (Instance == this) Instance = null;
        }
    }
}
