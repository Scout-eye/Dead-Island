using System.Threading.Tasks;
using Steamworks.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Color = UnityEngine.Color;
using Image = UnityEngine.UI.Image;

namespace Game.Net
{
    /// <summary>
    /// Menu multijoueur généré 100% par code (uGUI) : Créer / Chercher / Salle d'attente.
    /// À poser sur un GameObject vide d'une scène de menu. Crée aussi les managers (Steam/Lobby/Network)
    /// et l'EventSystem (compatible New Input System).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenu : MonoBehaviour
    {
        private static Font UIFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private GameObject _mainPanel;
        private GameObject _browsePanel;
        private GameObject _roomPanel;
        private InputField _nameField;
        private Transform _lobbyListContent;
        private Transform _memberListContent;
        private Text _seedText;
        private Button _startButton;
        private GameObject _pausePanel;
        private Text _notifLabel;
        private float _notifTimer;
        private Canvas _canvas;
        private bool _inGame;
        private bool _paused;

        private void Start()
        {
            // Managers
            SteamManager.EnsureExists();
            LobbyManager.EnsureExists();
            NetworkManager.EnsureExists();

            EnsureEventSystem();
            BuildUI();

            // (Le HUD de survie s'auto-crée via VitalsHUD.Bootstrap — indépendant du menu/multijoueur.)

            var lm = LobbyManager.Instance;
            lm.OnEnteredLobby += _ => ShowRoom();
            lm.OnLeftLobby += ShowMain;
            lm.OnMembersChanged += RefreshRoom;
            lm.OnGameStart += _ => HideMenu();

            NetworkManager.Instance.Disconnected += HandleDisconnected;
            NetworkManager.Instance.OnReturnedToRoom += HandleReturnedToRoom;

            ShowMain();
        }

        // --- Construction UI ---

        private void BuildUI()
        {
            var canvasGo = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            BuildMainPanel();
            BuildBrowsePanel();
            BuildRoomPanel();
            BuildPausePanel();
            BuildNotification();
        }

        private void BuildNotification()
        {
            var go = new GameObject("Notif", typeof(RectTransform));
            go.transform.SetParent(_canvas.transform, false);
            _notifLabel = go.AddComponent<Text>();
            _notifLabel.font = UIFont;
            _notifLabel.fontSize = 22;
            _notifLabel.color = new Color(1f, 0.6f, 0.5f, 1f);
            _notifLabel.alignment = TextAnchor.UpperCenter;
            _notifLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = _notifLabel.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(700, 40);
            rt.anchoredPosition = new Vector2(0f, -28f);
            go.SetActive(false);
        }

        private void ShowNotification(string message, float duration = 4f)
        {
            if (_notifLabel == null) return;
            _notifLabel.text = message;
            _notifLabel.gameObject.SetActive(true);
            _notifTimer = duration;
        }

        private void HandleDisconnected(string reason)
        {
            // L'hôte est perdu : NetworkManager a déjà nettoyé le jeu, on revient au menu + notif.
            if (LobbyManager.Instance != null) LobbyManager.Instance.LeaveLobby();
            ShowMain();
            ShowNotification(reason);
        }

        private void HandleReturnedToRoom()
        {
            // Tous morts : le jeu est nettoyé mais on reste dans le lobby -> salle d'attente.
            _inGame = false;
            _paused = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            ShowRoom();
            ShowNotification("Tous les joueurs sont morts. Retour à la salle d'attente.");
        }

        private void BuildPausePanel()
        {
            _pausePanel = MakePanel(_canvas.transform, "PausePanel");
            MakeText(_pausePanel.transform, "Pause", 28);
            MakeButton(_pausePanel.transform, "Reprendre", () => SetPaused(false));
            MakeButton(_pausePanel.transform, "Menu principal", ReturnToMenu);
            _pausePanel.SetActive(false);
        }

        private float _roomRefreshTimer;

        private void Update()
        {
            if (_notifTimer > 0f)
            {
                _notifTimer -= Time.unscaledDeltaTime;
                if (_notifTimer <= 0f && _notifLabel != null) _notifLabel.gameObject.SetActive(false);
            }

            // Rafraîchit la liste de la salle d'attente régulièrement (fiabilise si un callback manque).
            if (_roomPanel != null && _roomPanel.activeSelf)
            {
                _roomRefreshTimer -= Time.unscaledDeltaTime;
                if (_roomRefreshTimer <= 0f)
                {
                    _roomRefreshTimer = 1f;
                    RefreshRoom();
                }
            }

            if (!_inGame) return;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                SetPaused(!_paused);
        }

        private void SetPaused(bool paused)
        {
            _paused = paused;
            if (_pausePanel != null) _pausePanel.SetActive(paused);
            Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = paused;
        }

        private void ReturnToMenu()
        {
            _inGame = false;
            _paused = false;
            if (NetworkManager.Instance != null) NetworkManager.Instance.LeaveGame();
            if (LobbyManager.Instance != null) LobbyManager.Instance.LeaveLobby(); // -> OnLeftLobby -> ShowMain
            ShowMain();
        }

        private void BuildMainPanel()
        {
            _mainPanel = MakePanel(_canvas.transform, "MainPanel");
            MakeText(_mainPanel.transform, "DEAD ISLAND", 32);
            MakeText(_mainPanel.transform, "Survie sociale — multijoueur Steam", 14);

            MakeText(_mainPanel.transform, "Nom de la partie :", 14, TextAnchor.MiddleLeft);
            _nameField = MakeInputField(_mainPanel.transform, "Île de ...");

            MakeButton(_mainPanel.transform, "Créer une partie", () =>
                LobbyManager.Instance.CreateLobby(_nameField != null ? _nameField.text : ""));

            MakeButton(_mainPanel.transform, "Chercher une partie", ShowBrowse);
        }

        private void BuildBrowsePanel()
        {
            _browsePanel = MakePanel(_canvas.transform, "BrowsePanel");
            MakeText(_browsePanel.transform, "Parties ouvertes", 26);
            MakeButton(_browsePanel.transform, "Rafraîchir", RefreshBrowse);

            _lobbyListContent = MakeScrollList(_browsePanel.transform);

            MakeButton(_browsePanel.transform, "Retour", ShowMain);
        }

        private void BuildRoomPanel()
        {
            _roomPanel = MakePanel(_canvas.transform, "RoomPanel");
            MakeText(_roomPanel.transform, "Salle d'attente", 26);
            _seedText = MakeText(_roomPanel.transform, "Seed : -", 14);

            _memberListContent = MakeScrollList(_roomPanel.transform);

            _startButton = MakeButton(_roomPanel.transform, "Lancer la partie",
                () => LobbyManager.Instance.StartGame());
            MakeButton(_roomPanel.transform, "Quitter", () => LobbyManager.Instance.LeaveLobby());
        }

        // --- Navigation ---

        private void ShowMain()
        {
            _inGame = false;
            _paused = false;
            _mainPanel.SetActive(true);
            _browsePanel.SetActive(false);
            _roomPanel.SetActive(false);
            if (_pausePanel != null) _pausePanel.SetActive(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ShowBrowse()
        {
            _mainPanel.SetActive(false);
            _browsePanel.SetActive(true);
            _roomPanel.SetActive(false);
            RefreshBrowse();
        }

        private void ShowRoom()
        {
            _mainPanel.SetActive(false);
            _browsePanel.SetActive(false);
            _roomPanel.SetActive(true);
            RefreshRoom();
        }

        private void HideMenu()
        {
            // En jeu : on cache les panneaux (le canvas reste actif pour l'overlay pause Échap).
            _inGame = true;
            _paused = false;
            _mainPanel.SetActive(false);
            _browsePanel.SetActive(false);
            _roomPanel.SetActive(false);
            if (_pausePanel != null) _pausePanel.SetActive(false);
        }

        // --- Données ---

        private async void RefreshBrowse()
        {
            ClearChildren(_lobbyListContent);
            MakeText(_lobbyListContent, "Recherche...", 14);

            Lobby[] lobbies = await LobbyManager.Instance.RequestLobbies();

            ClearChildren(_lobbyListContent);
            if (lobbies.Length == 0)
            {
                MakeText(_lobbyListContent, "Aucune partie trouvée.", 14);
                return;
            }

            foreach (var lobby in lobbies)
            {
                var captured = lobby;
                string lobbyName = lobby.GetData(LobbyManager.KeyName);
                if (string.IsNullOrEmpty(lobbyName)) lobbyName = "Partie";
                string label = $"{lobbyName}  ({lobby.MemberCount}/{lobby.MaxMembers})";
                MakeButton(_lobbyListContent, label, () => LobbyManager.Instance.JoinLobby(captured));
            }
        }

        private void RefreshRoom()
        {
            if (!_roomPanel.activeSelf) return;
            var lm = LobbyManager.Instance;
            if (!lm.InLobby) return;

            _seedText.text = $"Seed : {lm.Seed}";
            _startButton.gameObject.SetActive(lm.IsHost);

            var lobby = lm.Current.Value;
            ClearChildren(_memberListContent);
            MakeText(_memberListContent, $"Joueurs ({lobby.MemberCount}/{lobby.MaxMembers})", 14, TextAnchor.MiddleLeft);
            foreach (var member in lobby.Members)
            {
                string name = string.IsNullOrEmpty(member.Name) ? $"Joueur {member.Id}" : member.Name;
                string tag = member.Id == lobby.Owner.Id ? " (hôte)" : "";
                MakeText(_memberListContent, "• " + name + tag, 16, TextAnchor.MiddleLeft);
            }
        }

        // --- Helpers UI ---

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(go);
        }

        private static GameObject MakePanel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(440, 560);
            rt.anchoredPosition = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

            var v = go.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(18, 18, 18, 18);
            v.spacing = 8;
            v.childControlWidth = true;
            v.childControlHeight = false;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childAlignment = TextAnchor.UpperCenter;
            return go;
        }

        private static Text MakeText(Transform parent, string content, int size, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = UIFont;
            t.text = content;
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = size + 8;
            return t;
        }

        private static Button MakeButton(Transform parent, string label, UnityAction action)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.18f, 0.32f, 0.45f, 1f);
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(action);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 42;

            var t = MakeText(go.transform, label, 18);
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return btn;
        }

        private static InputField MakeInputField(Transform parent, string placeholder)
        {
            var go = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Color.white;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 36;

            var input = go.GetComponent<InputField>();
            var ph = MakeFillText(go.transform, placeholder, new Color(0.45f, 0.45f, 0.45f, 1f));
            var txt = MakeFillText(go.transform, "", Color.black);
            txt.supportRichText = false;
            input.textComponent = txt;
            input.placeholder = ph;
            return input;
        }

        private static Text MakeFillText(Transform parent, string content, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = UIFont;
            t.text = content;
            t.fontSize = 16;
            t.color = color;
            t.alignment = TextAnchor.MiddleLeft;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8, 2);
            rt.offsetMax = new Vector2(-8, -2);
            return t;
        }

        /// <summary>Crée une zone scrollable verticale et retourne le content où ajouter les éléments.</summary>
        private static Transform MakeScrollList(Transform parent)
        {
            var viewport = new GameObject("List", typeof(RectTransform), typeof(Image),
                                          typeof(ScrollRect), typeof(Mask));
            viewport.transform.SetParent(parent, false);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
            viewport.GetComponent<Mask>().showMaskGraphic = true;
            var le = viewport.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 300;

            var content = new GameObject("Content", typeof(RectTransform),
                                         typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.anchoredPosition = Vector2.zero;

            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.GetComponent<ScrollRect>();
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.vertical = true;
            return content.transform;
        }

        private static void ClearChildren(Transform t)
        {
            if (t == null) return;
            for (int i = t.childCount - 1; i >= 0; i--)
                Destroy(t.GetChild(i).gameObject);
        }
    }
}
