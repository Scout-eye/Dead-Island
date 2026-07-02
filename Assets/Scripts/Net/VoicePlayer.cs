using System.IO;
using Steamworks;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Joue la voix d'un joueur DISTANT en 3D "proximité" :
    ///  - AudioSource spatialisé à hauteur de bouche → le volume baisse avec la distance
    ///    (rolloff linéaire, inaudible au-delà de MaxDistance) ;
    ///  - passe-bas progressif avec l'éloignement → voix étouffée/déformée quand elle est lointaine ;
    ///  - volume individuel réglable (slider du menu pause), persisté par SteamId dans PlayerPrefs.
    ///
    /// Reçoit les paquets voix compressés (Steam) via <see cref="Receive"/>, les décompresse en
    /// PCM 16 bits et les pousse dans un tampon circulaire lu par un AudioClip streamé.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoicePlayer : MonoBehaviour
    {
        private const float MinDistance = 2f;    // plein volume en-dessous
        private const float MaxDistance = 30f;   // inaudible au-delà
        private const float MuffleStart = 10f;   // début de l'étouffement
        private const float CutoffNear = 22000f; // passe-bas ouvert (voix claire)
        private const float CutoffFar = 700f;    // voix très lointaine = très sourde

        private AudioSource _source;
        private AudioLowPassFilter _lowPass;
        private float[] _ring;                   // tampon circulaire d'échantillons
        private long _write, _read;
        private readonly object _lock = new object();
        private readonly MemoryStream _pcm = new MemoryStream();
        private ulong _steamId;
        private float _userVolume = 1f;

        public ulong SteamId => _steamId;
        public string DisplayName { get; private set; }

        /// <summary>Volume individuel (slider menu pause), persisté par joueur.</summary>
        public float UserVolume
        {
            get => _userVolume;
            set
            {
                _userVolume = Mathf.Clamp01(value);
                if (_source != null) _source.volume = _userVolume;
                PlayerPrefs.SetFloat("di_voice_" + _steamId, _userVolume);
            }
        }

        /// <summary>Ajoute (ou récupère) le lecteur de voix d'un joueur distant.</summary>
        public static VoicePlayer Attach(GameObject player, ulong steamId)
        {
            var vp = player.GetComponent<VoicePlayer>();
            if (vp == null) vp = player.AddComponent<VoicePlayer>();
            vp.Setup(steamId);
            return vp;
        }

        private void Setup(ulong steamId)
        {
            if (_source != null) return; // déjà initialisé
            _steamId = steamId;
            DisplayName = new Friend(steamId).Name;
            _userVolume = PlayerPrefs.GetFloat("di_voice_" + steamId, 1f);

            int rate = (int)SteamUser.OptimalSampleRate;
            _ring = new float[rate * 2]; // 2 s de tampon

            var go = new GameObject("Voice");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.6f, 0f); // hauteur de bouche

            _source = go.AddComponent<AudioSource>();
            _source.clip = AudioClip.Create("VoiceStream", rate, 1, rate, true, OnAudioRead);
            _source.loop = true;
            _source.spatialBlend = 1f;                       // 3D
            _source.rolloffMode = AudioRolloffMode.Linear;   // baisse régulière, 0 à MaxDistance
            _source.minDistance = MinDistance;
            _source.maxDistance = MaxDistance;
            _source.dopplerLevel = 0f;
            _source.volume = _userVolume;
            _source.Play();

            _lowPass = go.AddComponent<AudioLowPassFilter>();
            _lowPass.cutoffFrequency = CutoffNear;
        }

        private void Update()
        {
            // Étouffement progressif : plus la voix est loin de NOTRE caméra, plus elle est sourde.
            if (_lowPass == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            float d = Vector3.Distance(cam.transform.position, transform.position);
            float t = Mathf.InverseLerp(MuffleStart, MaxDistance, d);
            _lowPass.cutoffFrequency = Mathf.Lerp(CutoffNear, CutoffFar, t * t); // douce puis marquée
        }

        /// <summary>Paquet voix compressé (Steam) reçu du réseau.</summary>
        public void Receive(byte[] packet, int offset, int count)
        {
            if (_source == null || count <= 0) return;

            var compressed = new byte[count];
            System.Buffer.BlockCopy(packet, offset, compressed, 0, count);

            _pcm.Position = 0;
            _pcm.SetLength(0);
            int written = SteamUser.DecompressVoice(compressed, _pcm);
            if (written <= 0) return;

            // PCM 16 bits signé mono → floats dans le tampon circulaire.
            var buf = _pcm.GetBuffer();
            lock (_lock)
            {
                for (int i = 0; i + 1 < written; i += 2)
                {
                    short s = (short)(buf[i] | (buf[i + 1] << 8));
                    _ring[_write % _ring.Length] = s / 32768f;
                    _write++;
                }
                // Anti-dérive : si le tampon déborde (lag), on saute en avant pour rester "live".
                if (_write - _read > _ring.Length) _read = _write - _ring.Length / 2;
            }
        }

        // Callback du clip streamé (thread audio) : lit le tampon, silence si vide.
        private void OnAudioRead(float[] data)
        {
            lock (_lock)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    if (_read < _write)
                    {
                        data[i] = _ring[_read % _ring.Length];
                        _read++;
                    }
                    else data[i] = 0f;
                }
            }
        }
    }
}
