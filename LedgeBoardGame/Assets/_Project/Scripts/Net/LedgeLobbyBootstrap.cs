using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Magi.LedgeBoardGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using MagiGameServer.Contracts.Core;

namespace Magi.LedgeBoardGame.Net
{
    /// Minimal in-scene lobby for the Monday 6-player demo. Replaces
    /// LedgeShadowBootstrap's auto-spawn in builds: shows a kit-styled
    /// Canvas overlay at scene start, defers GameController.Start until the
    /// user picks Host or Join, then wires up a single-seat
    /// LedgeBoardSessionDriver via ForHost / ForJoin and attaches it as the
    /// controller's shadow sink so the Network-mode entry points submit
    /// through the real WebSocket transport.
    ///
    /// Execution order -100 runs Awake before GameController's own Awake
    /// (default 0), which lets us disable the controller component before
    /// Unity schedules its Start callback. When the player clicks Go we
    /// apply ConfigureNetwork(...), re-enable the controller, wait one
    /// frame for Start to run with the lobby-chosen roster size and seat
    /// index, then open/attach the transport.
    ///
    /// The lobby is drop-in on the GameScene — LedgeShadowBootstrap's
    /// AutoSpawn skips when a LedgeLobbyBootstrap is already present, so
    /// the two bootstraps never fight for the same controller.
    [DefaultExecutionOrder(-100)]
    public sealed class LedgeLobbyBootstrap : MonoBehaviour
    {
        public enum LobbyMode
        {
            Host = 0,
            Join = 1,
            // Practice = local hot-seat, no driver, no network. Lets a single
            // player rehearse rules / UI without spinning up a session. Uses
            // GameController.NetworkMode.Local and skips all MagiSession wiring.
            Practice = 2,
        }

        [SerializeField] private GameController controller;
        [Tooltip("Default WebSocket-capable base URI for the MagiGameServer.Host launcher. The player can edit this in the overlay before clicking Go. Use https://play.magi-agi.org/ledge for prod (nginx strips the /ledge prefix to the loopback LedgeBoardGame.Host); http://localhost:5080 for a local dev host.")]
        [SerializeField] private string defaultHostBaseUri = "https://play.magi-agi.org/ledge";
        [Tooltip("Seat count the Host advertises when opening a new session. Joiners land on whatever lowest-free seat the server hands out, so this only matters for the host's POST /session/open — once a joiner is in, seat count is tracked server-side.")]
        [SerializeField, Range(2, 8)] private int defaultSeatCount = 8;
        [Tooltip("Pre-seed for the Join session code. Empty until a host shares a code; leave blank at build time.")]
        [SerializeField] private string defaultSessionCode = "";

        private const string PlayerNamePrefKey = "ledge.playerName";
        private const string SkinPrefKey = "ledge.boardSkin";
        private const string TutorialNudgePrefKey = "ledge.tutorialNudgeShown";
        private const string DefaultSkinId = "nightfall";

        private LobbyMode _mode = LobbyMode.Host;
        private int _seatCount;
        private int _ownedSeat;
        private string _sessionCode;
        private string _hostBaseUri;
        private string _playerName = "";

        private enum UiState
        {
            Selecting,
            Connecting,
            Connected,
            Failed,
        }

        private UiState _uiState = UiState.Selecting;
        private string _status = "";
        private string _sharedCode = "";
        private bool _showQuitConfirm;

        private LedgeBoardSessionDriver _driver;
        private CancellationTokenSource _cts;
        private bool _ready;

        // ── Canvas widgets ──────────────────────────────────────────────────
        private Canvas _lobbyCanvas;
        private GameObject _dreamBackdropGo;
        private LedgeTitlePanel _titlePanel;
        private LedgeTutorialPanel _tutorialPanel;
        private LedgeSetupPanel _setupPanel;
        private LedgeSettingsPanel _settingsPanel;
        private bool _titleDismissed;
        private string _selectedSkinId = DefaultSkinId;
        private GameObject _mainPanelGo;
        private GameObject _connectedBannerGo;
        private GameObject _quitOverlayGo;
        private GameObject _quitBackToLobbyGo;
        private LedgeButton _hostTab, _joinTab, _practiceTab;
        private LedgeButton _goButton;
        private TMP_Text _goButtonLabelMirror; // not used; LedgeButton.Text drives label
        private TMP_InputField _nameField, _hostUriField, _sessionCodeField, _bannerCodeField;
        private Slider _seatsSlider;
        private TMP_Text _seatsValueLabel;
        private GameObject _hostUriRow, _sessionCodeRow, _seatsRow, _sharedCodeRow;
        private TMP_Text _statusLabel;
        private TMP_InputField _sharedCodeField;
        private TMP_Text _quitTitle, _quitBody;

        private void Awake()
        {
            if (controller == null) controller = FindAnyObjectByType<GameController>();
            // Suspending the controller until the lobby selection completes
            // is the whole point of running at execution order -100 — Unity
            // schedules Start only after the component is enabled, so we
            // get a clean "configure → enable → Start" ordering.
            if (controller != null) controller.enabled = false;
            _seatCount = Mathf.Clamp(defaultSeatCount, 2, 8);
            // Host always opens seat 0 (first lowest-free claim). Join lets
            // the server claim, so the seat is populated from the driver
            // after ConnectAsync. No user-facing picker.
            _ownedSeat = 0;
            _sessionCode = defaultSessionCode ?? "";
            _hostBaseUri = string.IsNullOrEmpty(defaultHostBaseUri)
                ? "http://localhost:5080"
                : defaultHostBaseUri;
            // PlayerPrefs carries the player's chosen display name across
            // scene reloads and app restarts so the lobby remembers "Anna"
            // between sessions. Empty means "use the default PlayerN label"
            // — we only submit a SetDisplayName action when the field is
            // non-empty, keeping the roster unchanged for anyone who leaves
            // the field blank.
            _playerName = PlayerPrefs.GetString(PlayerNamePrefKey, "") ?? "";
            // F5: skin persists across sessions. Empty/missing falls back
            // to the kit default ("nightfall"). The setup screen rehydrates
            // from this value and writes the chosen one back on Ready.
            _selectedSkinId = PlayerPrefs.GetString(SkinPrefKey, DefaultSkinId);
            if (string.IsNullOrEmpty(_selectedSkinId)) _selectedSkinId = DefaultSkinId;

            BuildCanvas();
            RefreshUi();

            // First-launch flow: no persisted name → start in setup so the
            // player picks identity (name + skin) before seeing the title.
            // Subsequent launches go straight to the title. F6 also tags
            // the title's "How to play" button with a one-time pulse for
            // first-launch sessions (gated on TutorialNudgePrefKey).
            bool firstLaunch = string.IsNullOrEmpty(_playerName);
            bool nudgeShown = PlayerPrefs.GetInt(TutorialNudgePrefKey, 0) != 0;
            bool highlight = firstLaunch && !nudgeShown;
            if (firstLaunch)
            {
                ShowSetup(onComplete: () => ShowTitle(highlightHowToPlay: highlight));
            }
            else
            {
                ShowTitle(highlightHowToPlay: false);
            }
        }

        private void Update()
        {
            if (_ready && _driver != null) _driver.Tick();
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                // Pop overlays in z-order: settings → setup → tutorial →
                // title → quit confirm. (Each transient overlay can sit on
                // top of any other; ESC backs out one layer at a time.)
                if (_settingsPanel != null && _settingsPanel.IsShowing)
                {
                    _settingsPanel.Hide();
                }
                else if (_setupPanel != null && _setupPanel.IsShowing)
                {
                    _setupPanel.Hide();
                }
                else if (_tutorialPanel != null && _tutorialPanel.IsShowing)
                {
                    _tutorialPanel.Hide();
                }
                else if (!_titleDismissed && _titlePanel != null)
                {
                    _titleDismissed = true;
                    _titlePanel.Hide();
                    SelectMode(LobbyMode.Host);
                }
                else
                {
                    _showQuitConfirm = !_showQuitConfirm;
                    RefreshUi();
                }
            }
        }

        private async void OnDestroy()
        {
            _ready = false;
            try { _cts?.Cancel(); } catch { }
            if (_driver != null)
            {
                try { await _driver.DisposeAsync(); }
                catch (Exception ex) { UnityEngine.Debug.LogError($"[lobby] driver dispose failed: {ex}"); }
                _driver = null;
            }
            _cts?.Dispose();
            _cts = null;
        }

        private void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // Full-scene reload is the cleanest way to disconnect: it disposes
        // the driver via OnDestroy, tears down the GameController, and re-
        // runs the Awake-time lobby overlay. Avoids having to reset every
        // transient piece of controller state by hand.
        private void ReloadScene()
        {
            var scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }

        private IEnumerator BeginFlow()
        {
            _uiState = UiState.Connecting;
            _status = "Starting controller…";
            RefreshUi();

            if (controller == null)
            {
                _uiState = UiState.Failed;
                _status = "No GameController in scene.";
                RefreshUi();
                yield break;
            }

            // Practice mode: no driver, no transport, no seat claim. Just
            // configure the controller in Local (hot-seat) mode at the chosen
            // seat count and hand it a Start() pass. _ready stays false —
            // the lobby's per-frame Tick() and the connected banner both
            // gate on a live driver, and Practice has none.
            if (_mode == LobbyMode.Practice)
            {
                if (controller == null)
                {
                    _uiState = UiState.Failed;
                    _status = "No GameController in scene.";
                    RefreshUi();
                    yield break;
                }
                int seats = Mathf.Clamp(_seatCount, 2, 8);
                _seatCount = seats;
                try
                {
                    controller.ConfigureNetwork(
                        GameController.NetworkMode.Local,
                        ownedSeatIndex: 0,
                        totalSeats: seats);
                }
                catch (Exception ex)
                {
                    _uiState = UiState.Failed;
                    _status = $"Configure failed: {ex.Message}";
                    RefreshUi();
                    yield break;
                }
                controller.enabled = true;
                yield return null;
                _uiState = UiState.Connected;
                _status = $"Practice ({seats} seats).";
                RefreshUi();
                // No SetDisplayName action in Practice — GameState lives
                // locally; overwriting Players[0].Name is a simple mutation
                // but we skip it here since Local mode has no network
                // propagation contract and the HUD already rotates through
                // PlayerN labels for hot-seat play.
                yield break;
            }

            SessionId? joinSession = null;
            if (_mode == LobbyMode.Join)
            {
                if (string.IsNullOrWhiteSpace(_sessionCode))
                {
                    _uiState = UiState.Failed;
                    _status = "Session code required for Join.";
                    RefreshUi();
                    yield break;
                }
                // Canonicalise before shipping: server issues lowercase
                // base32 codes but a human typing the code from memory
                // (or pasting from an email that capitalised the first
                // letter) should still land on the right session.
                // Whitespace dropped because the GUI text field will
                // happily carry a trailing space from a clipboard paste.
                var canonical = _sessionCode.Trim().ToLowerInvariant();
                _sessionCode = canonical;
                joinSession = new SessionId(canonical);
            }

            // Host always holds seat 0 (the first ClaimAndAttach on a fresh
            // session lands there). Join has no pre-known seat — the server
            // hands one out during ConnectAsync, and we must connect BEFORE
            // ConfigureNetwork so GameController.Start runs with the real
            // local seat rather than a placeholder.
            if (_mode == LobbyMode.Host)
            {
                if (!TryConfigureController(_ownedSeat, _seatCount, out var configError))
                {
                    _uiState = UiState.Failed;
                    _status = $"Configure failed: {configError.Message}";
                    RefreshUi();
                    yield break;
                }
                controller.enabled = true;
                yield return null;

                _status = "Connecting to server…";
                RefreshUi();
                var hostTask = ConnectAndAttachAsync(joinSession: null);
                while (!hostTask.IsCompleted) yield return null;
                if (hostTask.IsFaulted)
                {
                    var ex = hostTask.Exception?.GetBaseException();
                    _uiState = UiState.Failed;
                    _status = $"Connect failed: {ex?.Message ?? "unknown"}";
                    RefreshUi();
                    UnityEngine.Debug.LogError($"[lobby] connect failed: {ex}");
                    yield break;
                }
            }
            else
            {
                _status = "Connecting to server…";
                RefreshUi();
                var joinTask = ConnectAndAttachAsync(joinSession);
                while (!joinTask.IsCompleted) yield return null;
                if (joinTask.IsFaulted)
                {
                    var ex = joinTask.Exception?.GetBaseException();
                    _uiState = UiState.Failed;
                    _status = $"Connect failed: {ex?.Message ?? "unknown"}";
                    RefreshUi();
                    UnityEngine.Debug.LogError($"[lobby] connect failed: {ex}");
                    yield break;
                }
                // Server picked our seat during ClaimAndAttach — feed it to
                // the controller so Start runs with the real local seat.
                _ownedSeat = _driver.OwnedSeats[0];
                // Pump Tick until the first JoinSnapshot is ingested (up to
                // ~2s) so the driver latches the server-authoritative seat
                // count. ConnectAndAttachAsync returned when the socket is
                // live, but the first frame still needs a Tick pass to be
                // dispatched on the main thread. Without this wait the
                // joiner would configure GameController with the inspector
                // default (8) even when the host opened a 2- or 4-seat
                // session.
                float waitStart = Time.realtimeSinceStartup;
                while (_driver.ObservedSeatCount <= 0
                       && Time.realtimeSinceStartup - waitStart < 2f)
                {
                    _driver.Tick();
                    yield return null;
                }
                if (_driver.ObservedSeatCount > 0)
                {
                    _seatCount = _driver.ObservedSeatCount;
                }
                else
                {
                    UnityEngine.Debug.LogWarning(
                        $"[lobby] JoinSnapshot not observed within 2s; configuring controller with inspector seatCount={_seatCount}. Host/joiner may disagree on seat count.");
                }
                if (!TryConfigureController(_ownedSeat, _seatCount, out var configError))
                {
                    _uiState = UiState.Failed;
                    _status = $"Configure failed: {configError.Message}";
                    RefreshUi();
                    yield break;
                }
                controller.enabled = true;
                yield return null;
                // Late AttachShadowSink: ConnectAndAttachAsync skipped the
                // attach for Join because the controller wasn't enabled yet.
                controller.AttachShadowSink(_driver);
            }

            _ready = true;
            _uiState = UiState.Connected;
            _status = "Connected.";
            if (_mode == LobbyMode.Host && _driver?.ActiveSessionId.HasValue == true)
                _sharedCode = _driver.ActiveSessionId.Value.Value;
            RefreshUi();

            // Propagate the chosen display name as a one-shot SetDisplayName
            // action so every peer's HUD shows "Anna" instead of "Player4".
            // Skipped when the field is empty — clients who opt out keep
            // the default roster label. PlayerId is seat+1 (the Player.Id
            // convention BuildDefaultRoster uses), no further lookup needed.
            // PlayerPrefs is written here rather than on every keystroke so
            // only names attached to a real connect attempt stick around.
            var trimmed = (_playerName ?? "").Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                PlayerPrefs.SetString(PlayerNamePrefKey, trimmed);
                PlayerPrefs.Save();
                int playerId = _ownedSeat + 1;
                try { _driver?.SubmitDisplayName(_ownedSeat, playerId, trimmed); }
                catch (Exception ex) { UnityEngine.Debug.LogError($"[lobby] display-name submit failed: {ex}"); }
            }
        }

        private bool TryConfigureController(int seat, int seatCount, out Exception error)
        {
            try
            {
                controller.ConfigureNetwork(GameController.NetworkMode.Network, seat, seatCount);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private async Task ConnectAndAttachAsync(SessionId? joinSession)
        {
            _cts = new CancellationTokenSource();
            string specJson = controller.GetLedgeSpecJson();
            UnityEngine.Debug.Log($"[lobby-diag] ConnectAndAttachAsync enter join={joinSession.HasValue} baseUri={_hostBaseUri} seatCount={_seatCount} ownedSeat={_ownedSeat} specLen={specJson?.Length ?? 0}");
            _driver = joinSession.HasValue
                ? LedgeBoardSessionDriver.ForJoinClaim(_seatCount, joinSession.Value)
                : LedgeBoardSessionDriver.ForHost(_seatCount, _ownedSeat);
            UnityEngine.Debug.Log("[lobby-diag] driver built, calling HostNew/JoinClaim");
            try
            {
                if (joinSession.HasValue)
                    await _driver.JoinClaimAsync(_hostBaseUri, specJson, _cts.Token);
                else
                    await _driver.HostNewAsync(_hostBaseUri, specJson, _cts.Token);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[lobby-diag] driver start threw: {ex}");
                try { await _driver.DisposeAsync(); } catch { }
                _driver = null;
                throw;
            }
            UnityEngine.Debug.Log($"[lobby-diag] driver ready, session={_driver.ActiveSessionId?.Value ?? "<none>"}");
            // Host: controller already Start-ed with the right seat, attach now.
            // Join: controller isn't enabled yet — AttachShadowSink will happen
            // after BeginFlow reads the claimed seat and configures the controller.
            if (!joinSession.HasValue)
                controller.AttachShadowSink(_driver);
        }

        // ── Canvas construction ─────────────────────────────────────────────

        private void BuildCanvas()
        {
            // Ensure the scene has an EventSystem — uGUI input fields and
            // buttons do nothing without one. IMGUI didn't need this; the
            // scene may not have one yet.
            if (EventSystem.current == null)
            {
                var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                DontDestroyOnLoad(esGo);
            }

            var canvasGo = new GameObject("LobbyCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _lobbyCanvas = canvasGo.GetComponent<Canvas>();
            _lobbyCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the in-game HUD (default 0) so the connected banner +
            // quit overlay layer over it.
            _lobbyCanvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            // Starfield + gradient backdrop: same Dream Glass treatment as
            // the in-game canvas. No board halos registered in the lobby —
            // LedgeDreamCanvas's Awake builds gradient + stars unconditionally,
            // and halos are opt-in via EnsureBoardHalo. Place as the FIRST
            // sibling so the lobby chrome paints over it.
            var dreamGo = new GameObject("LobbyDreamBackdrop", typeof(RectTransform));
            dreamGo.transform.SetParent(canvasGo.transform, false);
            dreamGo.transform.SetAsFirstSibling();
            dreamGo.AddComponent<LedgeDreamCanvas>();
            _dreamBackdropGo = dreamGo;

            BuildMainPanel(canvasGo.transform);
            BuildConnectedBanner(canvasGo.transform);
            BuildQuitOverlay(canvasGo.transform);
            BuildTutorialOverlay(canvasGo.transform);
            BuildSetupOverlay(canvasGo.transform);
            BuildSettingsOverlay(canvasGo.transform);
            BuildTitleOverlay(canvasGo.transform);
        }

        /// Sibling Settings instance on the lobby canvas (the gameplay
        /// canvas spawns its own). Title's "Settings" and the ESC menu's
        /// Settings button both route here.
        private void BuildSettingsOverlay(Transform canvasRoot)
        {
            var go = new GameObject("SettingsHost", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            _settingsPanel = go.AddComponent<LedgeSettingsPanel>();
        }

        /// Open the full Settings panel (Audio / Motion / Accessibility /
        /// Account). The Account section's "Edit display name" / "Change
        /// skin" buttons route to Setup (F1 sub-route). onClose fires
        /// when the user clicks Done.
        public void ShowSettings(System.Action onClose = null)
        {
            if (_settingsPanel == null) return;
            _settingsPanel.Show(
                onClose: onClose,
                // Account → Setup. After Setup commits/cancels, re-open
                // Settings so the user can keep adjusting other tabs.
                onEditProfile: () => ShowSetup(onComplete: () => ShowSettings(onClose)));
        }

        /// Setup (skin picker + name editor) starts hidden. Reachable via
        /// the public ShowSetup() method — a future title/settings entry
        /// point can call into it. Ready commits the chosen name + skin
        /// and re-shows the lobby's main panel; Back just dismisses.
        private void BuildSetupOverlay(Transform canvasRoot)
        {
            var go = new GameObject("SetupHost", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            _setupPanel = go.AddComponent<LedgeSetupPanel>();
        }

        /// Trigger the setup screen with the currently-persisted name + skin.
        /// Ready commits the chosen name to PlayerPrefs (same key the lobby
        /// uses) and updates the in-memory skin selection. Both Ready and
        /// Back fire <paramref name="onComplete"/> so the caller can route
        /// what happens next (back to title, stay in lobby, etc.).
        /// Skin doesn't persist yet — needs a separate PlayerPrefs key
        /// once the skin catalog ships.
        public void ShowSetup(System.Action onComplete = null)
        {
            if (_setupPanel == null) return;
            _setupPanel.Show(
                initialName: _playerName,
                initialSkinId: _selectedSkinId,
                onReady: (name, skinId) =>
                {
                    _playerName = (name ?? "").Trim();
                    _selectedSkinId = string.IsNullOrEmpty(skinId) ? DefaultSkinId : skinId;
                    if (_nameField != null) _nameField.SetTextWithoutNotify(_playerName);
                    PlayerPrefs.SetString(PlayerNamePrefKey, _playerName);
                    PlayerPrefs.SetString(SkinPrefKey, _selectedSkinId);
                    PlayerPrefs.Save();
                    try { onComplete?.Invoke(); }
                    catch (System.Exception ex) { UnityEngine.Debug.LogError($"[lobby] setup-complete callback threw: {ex}"); }
                },
                onBack: onComplete);
        }

        /// Tutorial overlay starts hidden; the title's "How to play" handler
        /// fires .Show(...). Spawned before the title so the title sits on
        /// top until tutorial is invoked; Show() bumps it to the last sibling.
        private void BuildTutorialOverlay(Transform canvasRoot)
        {
            var tutorialGo = new GameObject("TutorialHost", typeof(RectTransform));
            tutorialGo.transform.SetParent(canvasRoot, false);
            _tutorialPanel = tutorialGo.AddComponent<LedgeTutorialPanel>();
        }

        /// Kit title screen lives above the lobby chrome on the same canvas.
        /// Spawned last so it sits on top of the main panel; the boot path
        /// decides whether to Show() it immediately or run setup first
        /// (see Awake's first-launch branch).
        private void BuildTitleOverlay(Transform canvasRoot)
        {
            var titleGo = new GameObject("TitleHost", typeof(RectTransform));
            titleGo.transform.SetParent(canvasRoot, false);
            titleGo.transform.SetAsLastSibling();
            _titlePanel = titleGo.AddComponent<LedgeTitlePanel>();
        }

        /// Show the title screen with the standard callback wiring. Called
        /// from Awake (after the first-launch check) and after the user
        /// closes setup/settings from the title.
        private void ShowTitle(bool highlightHowToPlay = false)
        {
            if (_titlePanel == null) return;
            // F6: mark the tutorial nudge as shown so subsequent launches
            // don't re-pulse the How-to-play button.
            if (highlightHowToPlay)
            {
                PlayerPrefs.SetInt(TutorialNudgePrefKey, 1);
                PlayerPrefs.Save();
            }
            _titlePanel.Show(
                onPlay: () =>
                {
                    _titleDismissed = true;
                    SelectMode(LobbyMode.Host);
                },
                onPractice: () =>
                {
                    _titleDismissed = true;
                    SelectMode(LobbyMode.Practice);
                },
                onHowToPlay: () =>
                {
                    // Tutorial sits over the title on the same canvas.
                    // Start practice → dismiss title + tutorial, jump
                    // straight to lobby's Practice tab. Skip → just hide
                    // the tutorial; title remains visible underneath.
                    if (_tutorialPanel == null) return;
                    _tutorialPanel.Show(
                        onStartPractice: () =>
                        {
                            _titleDismissed = true;
                            _titlePanel?.Hide();
                            SelectMode(LobbyMode.Practice);
                        },
                        onSkip: () => { /* tutorial hides itself; title still on screen */ });
                },
                onSettings: () =>
                {
                    // F1: Title's "Settings" opens the full Settings panel
                    // (Audio / Motion / Accessibility / Account). Account's
                    // Edit/Change buttons route into Setup as a sub-route.
                    // Done returns to the title.
                    ShowSettings(onClose: () => ShowTitle());
                },
                highlightHowToPlay: highlightHowToPlay);
        }

        private void BuildMainPanel(Transform canvasRoot)
        {
            _mainPanelGo = new GameObject("LobbyMainPanel", typeof(RectTransform));
            var rt = (RectTransform)_mainPanelGo.transform;
            rt.SetParent(canvasRoot, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(540f, 560f);
            rt.anchoredPosition = Vector2.zero;

            // Panel chrome per kit/ledge-board-game/project/ui/frame-lobby.jsx
            // → LobbyFrame: elevated (panel-2) + strongly-edged (panel-edge-2)
            // + 44×40 padding. The wider horizontal padding lets fields breathe
            // at the 540px panel width.
            var glass = LedgeGlassPanel.Build(_mainPanelGo.transform, "Glass",
                elevated: true, stronglyEdged: true,
                padding: new Vector2(44f, 40f));
            var gRt = glass.GetComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero; gRt.anchorMax = Vector2.one;
            gRt.offsetMin = Vector2.zero; gRt.offsetMax = Vector2.zero;

            var col = new GameObject("Col", typeof(RectTransform)).GetComponent<RectTransform>();
            col.SetParent(glass.Content, false);
            col.anchorMin = Vector2.zero; col.anchorMax = Vector2.one;
            col.offsetMin = Vector2.zero; col.offsetMax = Vector2.zero;
            var vl = col.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 10f;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childControlWidth = true;
            // childControlHeight=true so nested section subgroups (which are
            // VerticalLayoutGroups) report their preferredHeight to the outer
            // layout. Without it, sections collapse to 0 height and overflow
            // the panel because no one's measuring them.
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            // Title: "Ledge" Fraunces italic 40pt, then a separate centered
            // "LOBBY" SectionLabel below. Mirrors the kit's two-piece title
            // block (frame-lobby.jsx → LobbyFrame).
            var title = BuildLabel(col, "Ledge", 40f, LedgeUITokens.Ink);
            title.font = LedgeUITokens.DisplayFont;
            title.fontStyle = FontStyles.Italic;
            title.alignment = TextAlignmentOptions.Center;
            title.characterSpacing = -2f; // ~0.02em tighten per kit
            AddLayoutHeight(title.gameObject, 48f);

            var subtitle = BuildLabel(col, "LOBBY", LedgeUITokens.SectionLabelSize, LedgeUITokens.InkDim);
            subtitle.font = LedgeUITokens.MonoFont;
            subtitle.fontStyle = FontStyles.UpperCase;
            subtitle.characterSpacing = 22f;
            subtitle.alignment = TextAlignmentOptions.Center;
            AddLayoutHeight(subtitle.gameObject, 14f);

            // Mode tab row
            var tabsRow = BuildHorizontalRow(col, "Tabs", 36f, 8f, TextAnchor.MiddleCenter, equalWidth: true);
            _hostTab = LedgeButton.Build(tabsRow, "Host", LedgeButton.Variant.Ghost, LedgeButton.Size.Md, () => SelectMode(LobbyMode.Host));
            _joinTab = LedgeButton.Build(tabsRow, "Join", LedgeButton.Variant.Ghost, LedgeButton.Size.Md, () => SelectMode(LobbyMode.Join));
            _practiceTab = LedgeButton.Build(tabsRow, "Practice", LedgeButton.Variant.Ghost, LedgeButton.Size.Md, () => SelectMode(LobbyMode.Practice));
            foreach (var b in new[] { _hostTab, _joinTab, _practiceTab })
            {
                var le = b.gameObject.AddComponent<LayoutElement>();
                le.flexibleWidth = 1f;
                le.minWidth = 0f;
            }

            // Every label+field pair is wrapped in a Section group with a
            // tight 4px internal spacing so the caption sits visually attached
            // to the field below. The outer VLG handles inter-section gap.
            var nameSection = BuildSection(col, "NameSection");
            BuildSectionLabel(nameSection, "Your name (optional)");
            _nameField = BuildInputField(nameSection, "NameField", _playerName, "Anonymous");
            _nameField.onValueChanged.AddListener(v => _playerName = v ?? "");

            // Edit-profile entry into the kit-faithful setup screen (skin
            // picker + name editor with "how others see you" preview).
            // Beneath the name section so it reads as the natural follow-on
            // to "your name". Small Ghost Sm — explicitly de-emphasised vs
            // the lobby's primary Go button.
            var editBtn = LedgeButton.Build(nameSection, "Edit profile  →", LedgeButton.Variant.Ghost, LedgeButton.Size.Sm,
                () => ShowSetup());
            AddLayoutHeight(editBtn.gameObject, 26f);

            _hostUriRow = BuildSection(col, "HostUriSection").gameObject;
            BuildSectionLabel(_hostUriRow.transform, "Host base URI");
            _hostUriField = BuildInputField(_hostUriRow.transform, "HostUriField", _hostBaseUri, "https://…");
            _hostUriField.onValueChanged.AddListener(v => _hostBaseUri = v ?? "");

            _sessionCodeRow = BuildSection(col, "SessionCodeSection").gameObject;
            BuildSectionLabel(_sessionCodeRow.transform, "Session code (from host)");
            _sessionCodeField = BuildInputField(_sessionCodeRow.transform, "SessionCodeField", _sessionCode, "abcd-1234");
            _sessionCodeField.onValueChanged.AddListener(v => _sessionCode = v ?? "");

            _seatsRow = BuildSection(col, "SeatsSection").gameObject;
            BuildSectionLabel(_seatsRow.transform, "Seats");
            var sliderRow = BuildHorizontalRow(_seatsRow.transform, "SliderRow", 28f, 12f, TextAnchor.MiddleLeft, equalWidth: false);
            _seatsSlider = BuildSlider(sliderRow, _seatCount, 2, 8);
            _seatsSlider.onValueChanged.AddListener(v =>
            {
                _seatCount = Mathf.Clamp(Mathf.RoundToInt(v), 2, 8);
                if (_seatsValueLabel != null) _seatsValueLabel.text = _seatCount.ToString();
            });
            _seatsValueLabel = BuildLabel(sliderRow, _seatCount.ToString(), LedgeUITokens.IdentNameSize, LedgeUITokens.Ink);
            _seatsValueLabel.alignment = TextAlignmentOptions.MidlineRight;
            var valueLe = _seatsValueLabel.gameObject.AddComponent<LayoutElement>();
            valueLe.preferredWidth = 36f; valueLe.minWidth = 36f;

            // Status (always present, may be empty)
            _statusLabel = BuildLabel(col, "", LedgeUITokens.BodySize, LedgeUITokens.InkFaint);
            _statusLabel.alignment = TextAlignmentOptions.Center;
            _statusLabel.fontStyle = FontStyles.Italic;
            AddLayoutHeight(_statusLabel.gameObject, 22f);

            // Go button — full-width Ghost, 48px tall per the kit.
            _goButton = LedgeButton.Build(col, "Go", LedgeButton.Variant.Ghost, LedgeButton.Size.Lg, () => StartCoroutine(BeginFlow()));
            AddLayoutHeight(_goButton.gameObject, 48f);

            // Shared code row (post-host-connect)
            _sharedCodeRow = BuildSection(col, "SharedCodeSection").gameObject;
            BuildSectionLabel(_sharedCodeRow.transform, "Share this code with other players");
            _sharedCodeField = BuildInputField(_sharedCodeRow.transform, "SharedCodeField", "", "");
            _sharedCodeField.readOnly = true;
        }

        /// Uniform label+field section: vertical column, 4px internal spacing,
        /// reports its own preferred height to the outer VLG.
        private static RectTransform BuildSection(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 4f;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            return rt;
        }

        private void BuildConnectedBanner(Transform canvasRoot)
        {
            _connectedBannerGo = new GameObject("ConnectedBanner", typeof(RectTransform));
            var rt = (RectTransform)_connectedBannerGo.transform;
            rt.SetParent(canvasRoot, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(LedgeUITokens.PanelEdgeInset, -LedgeUITokens.PanelEdgeInset);
            rt.sizeDelta = new Vector2(380f, 84f);

            var glass = LedgeGlassPanel.Build(_connectedBannerGo.transform, "Glass");
            var gRt = glass.GetComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero; gRt.anchorMax = Vector2.one;
            gRt.offsetMin = Vector2.zero; gRt.offsetMax = Vector2.zero;

            var col = new GameObject("Col", typeof(RectTransform)).GetComponent<RectTransform>();
            col.SetParent(glass.Content, false);
            col.anchorMin = Vector2.zero; col.anchorMax = Vector2.one;
            col.offsetMin = Vector2.zero; col.offsetMax = Vector2.zero;
            var vl = col.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 4f; vl.childControlWidth = true; vl.childControlHeight = false; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;

            BuildSectionLabel(col, "Session code — share with other players");

            var row = BuildHorizontalRow(col, "CodeRow", 28f, 6f, TextAnchor.MiddleLeft, equalWidth: false);
            _bannerCodeField = BuildInputField(row, "BannerCode", "", "");
            _bannerCodeField.readOnly = true;
            var fieldLe = _bannerCodeField.GetComponent<LayoutElement>() ?? _bannerCodeField.gameObject.AddComponent<LayoutElement>();
            fieldLe.flexibleWidth = 1f;
            var copyBtn = LedgeButton.Build(row, "Copy", LedgeButton.Variant.Ghost, LedgeButton.Size.Sm,
                () => { GUIUtility.systemCopyBuffer = _sharedCode; });
            var copyLe = copyBtn.gameObject.AddComponent<LayoutElement>();
            copyLe.preferredWidth = 64f; copyLe.minWidth = 64f;
        }

        private void BuildQuitOverlay(Transform canvasRoot)
        {
            _quitOverlayGo = new GameObject("QuitOverlay", typeof(RectTransform));
            var rt = (RectTransform)_quitOverlayGo.transform;
            rt.SetParent(canvasRoot, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            // Scrim — dims everything behind the overlay so it reads modal.
            var scrimGo = new GameObject("Scrim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var scRt = (RectTransform)scrimGo.transform;
            scRt.SetParent(rt, false);
            scRt.anchorMin = Vector2.zero; scRt.anchorMax = Vector2.one;
            scRt.offsetMin = Vector2.zero; scRt.offsetMax = Vector2.zero;
            var scrimImg = scrimGo.GetComponent<Image>();
            scrimImg.color = new Color(0f, 0f, 0f, 0.55f);
            scrimImg.raycastTarget = true;

            // Centered glass panel
            var panelGo = new GameObject("Panel", typeof(RectTransform));
            var prt = (RectTransform)panelGo.transform;
            prt.SetParent(rt, false);
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            // Panel sized to fit the kit's larger title (26pt) + 2-line body
            // copy with extra padding (30 vertical / 32 horizontal per the
            // kit). 380px wide; height grew from 260 to 380 once the F2
            // Settings button joined the BackToLobby + Cancel/Quit stack.
            prt.sizeDelta = new Vector2(380f, 380f);
            var glass = LedgeGlassPanel.Build(panelGo.transform, "Glass",
                elevated: true, stronglyEdged: true,
                padding: new Vector2(32f, 30f));
            var gRt = glass.GetComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero; gRt.anchorMax = Vector2.one;
            gRt.offsetMin = Vector2.zero; gRt.offsetMax = Vector2.zero;

            var col = new GameObject("Col", typeof(RectTransform)).GetComponent<RectTransform>();
            col.SetParent(glass.Content, false);
            col.anchorMin = Vector2.zero; col.anchorMax = Vector2.one;
            col.offsetMin = Vector2.zero; col.offsetMax = Vector2.zero;
            var vl = col.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 8f;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childControlWidth = true; vl.childControlHeight = false;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;

            // Kit specifies the dialog title at 26pt Fraunces italic — a
            // larger commitment than the lobby's compact section copy.
            _quitTitle = BuildLabel(col, "Quit Ledge?", 26f, LedgeUITokens.Ink);
            _quitTitle.font = LedgeUITokens.DisplayFont;
            _quitTitle.fontStyle = FontStyles.Italic;
            _quitTitle.alignment = TextAlignmentOptions.Center;
            AddLayoutHeight(_quitTitle.gameObject, 36f);

            // Body wraps to two lines on a 380px panel at 13pt; enable word
            // wrap explicitly so the kit copy doesn't run off the edge.
            _quitBody = BuildLabel(col, "Exit to desktop?", 13f, LedgeUITokens.InkFaint);
            _quitBody.alignment = TextAlignmentOptions.Center;
            _quitBody.textWrappingMode = TextWrappingModes.Normal;
            AddLayoutHeight(_quitBody.gameObject, 60f);

            // Back-to-lobby (only visible when connected)
            _quitBackToLobbyGo = LedgeButton.Build(col, "Back to lobby", LedgeButton.Variant.Ghost, LedgeButton.Size.Md,
                () => { _showQuitConfirm = false; ReloadScene(); }).gameObject;
            AddLayoutHeight(_quitBackToLobbyGo, 36f);

            // F2: Settings button on the ESC menu. The kit's choice over a
            // TR gear icon — settings live where the player already pauses.
            // Hides the quit overlay and routes to the full Settings panel;
            // closing Settings returns to the quit overlay rather than the
            // game so the player can confirm or cancel from a single ESC.
            var settingsBtn = LedgeButton.Build(col, "Settings", LedgeButton.Variant.Ghost, LedgeButton.Size.Md,
                () =>
                {
                    _showQuitConfirm = false;
                    RefreshUi();
                    ShowSettings(onClose: () =>
                    {
                        _showQuitConfirm = true;
                        RefreshUi();
                    });
                });
            AddLayoutHeight(settingsBtn.gameObject, 36f);

            // Cancel | Quit row
            var btnRow = BuildHorizontalRow(col, "QuitButtons", 36f, 8f, TextAnchor.MiddleCenter, equalWidth: true);
            var cancelBtn = LedgeButton.Build(btnRow, "Cancel", LedgeButton.Variant.Ghost, LedgeButton.Size.Md,
                () => { _showQuitConfirm = false; RefreshUi(); });
            var cancelLe = cancelBtn.gameObject.AddComponent<LayoutElement>();
            cancelLe.flexibleWidth = 1f; cancelLe.minWidth = 0f;
            var quitBtn = LedgeButton.Build(btnRow, "Quit to desktop", LedgeButton.Variant.Danger, LedgeButton.Size.Md,
                () => { _showQuitConfirm = false; QuitApplication(); });
            var quitLe = quitBtn.gameObject.AddComponent<LayoutElement>();
            quitLe.flexibleWidth = 1f; quitLe.minWidth = 0f;
        }

        // ── Canvas refresh ──────────────────────────────────────────────────

        private void SelectMode(LobbyMode m)
        {
            _mode = m;
            RefreshUi();
        }

        private void RefreshUi()
        {
            if (_lobbyCanvas == null) return;

            // Tab selection: tinted via variant (Ghost vs the kit's selected
            // accent). We use a darker outline and brighter ink on the
            // selected tab via re-applying the variant after toggling.
            SetTabSelected(_hostTab, _mode == LobbyMode.Host);
            SetTabSelected(_joinTab, _mode == LobbyMode.Join);
            SetTabSelected(_practiceTab, _mode == LobbyMode.Practice);

            // Conditional rows
            if (_hostUriRow != null) _hostUriRow.SetActive(_mode != LobbyMode.Practice);
            if (_sessionCodeRow != null) _sessionCodeRow.SetActive(_mode == LobbyMode.Join);
            if (_seatsRow != null) _seatsRow.SetActive(_mode == LobbyMode.Practice);

            // Go button label + enabled
            if (_goButton != null)
            {
                _goButton.Text = _mode switch
                {
                    LobbyMode.Host => "HOST NEW SESSION",
                    LobbyMode.Join => "JOIN SESSION",
                    LobbyMode.Practice => "START PRACTICE GAME",
                    _ => "GO",
                };
                bool canGo = _uiState == UiState.Selecting || _uiState == UiState.Failed;
                _goButton.UnityButton.interactable = canGo;
            }

            // Status: dynamic message (connecting, failed, …) takes precedence;
            // otherwise show the kit's mode-contextual tagline so the slot
            // always reads intentional rather than empty.
            if (_statusLabel != null)
            {
                if (!string.IsNullOrEmpty(_status))
                {
                    _statusLabel.text = _status;
                }
                else
                {
                    _statusLabel.text = _mode switch
                    {
                        LobbyMode.Practice => "Pass-and-play on one device.",
                        LobbyMode.Host => "Ready when you are.",
                        LobbyMode.Join => "Ready when you are.",
                        _ => "",
                    };
                }
            }

            // Shared code row visibility + content
            bool showShared = !string.IsNullOrEmpty(_sharedCode) && _uiState != UiState.Connected;
            if (_sharedCodeRow != null) _sharedCodeRow.SetActive(showShared);
            if (_sharedCodeField != null && showShared) _sharedCodeField.SetTextWithoutNotify(_sharedCode);

            // Main panel visibility: hidden once connected so the in-game
            // chrome can take the screen. Still hidden behind the quit
            // overlay during pre-connect Escape presses (the scrim covers
            // it). Also hidden while the title is still up — the title's
            // Play/Practice buttons set _titleDismissed so the lobby
            // appears underneath when those handlers fire.
            if (_mainPanelGo != null)
                _mainPanelGo.SetActive(_uiState != UiState.Connected && _titleDismissed);

            // The dream backdrop is opaque fullscreen at lobby sortingOrder
            // 100, so leaving it active once Connected would paint over the
            // main Canvas (boards live there at sortingOrder 0). The in-game
            // EnsureDreamCanvas spawns its own backdrop on the main Canvas,
            // so dropping the lobby's here is the right hand-off.
            if (_dreamBackdropGo != null) _dreamBackdropGo.SetActive(_uiState != UiState.Connected);

            // Connected banner (TL): host-only, only when we have a code.
            bool showBanner = _uiState == UiState.Connected
                              && _mode == LobbyMode.Host
                              && !string.IsNullOrEmpty(_sharedCode);
            if (_connectedBannerGo != null) _connectedBannerGo.SetActive(showBanner);
            if (showBanner && _bannerCodeField != null) _bannerCodeField.SetTextWithoutNotify(_sharedCode);

            // Quit overlay visibility + connected-conditional Back-to-lobby button.
            bool connected = _uiState == UiState.Connected;
            if (_quitOverlayGo != null) _quitOverlayGo.SetActive(_showQuitConfirm);
            if (_quitTitle != null) _quitTitle.text = connected ? "Leave session?" : "Quit Ledge?";
            if (_quitBody != null) _quitBody.text = connected
                ? "Your seat stays open — you can rejoin with the session code while the game lasts."
                : "You can pick up a practice game any time.";
            if (_quitBackToLobbyGo != null) _quitBackToLobbyGo.SetActive(connected);
        }

        private static void SetTabSelected(LedgeButton btn, bool selected)
        {
            if (btn == null) return;
            // Selected tab gets the kit's accent treatment; unselected tabs
            // stay Ghost. This is the one place we use Primary deliberately
            // — the user objected to it on transient action buttons (End
            // Turn) but a static "current tab" cue is exactly what Primary
            // is for in the kit's frame-hud-2p.jsx pattern.
            btn.CurrentVariant = selected ? LedgeButton.Variant.Primary : LedgeButton.Variant.Ghost;
        }

        // ── Build helpers ───────────────────────────────────────────────────

        private static TMP_Text BuildLabel(Transform parent, string text, float fontSize, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.font = LedgeUITokens.UIFont;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void BuildSectionLabel(Transform parent, string text)
        {
            var lbl = BuildLabel(parent, text.ToUpperInvariant(), LedgeUITokens.SectionLabelSize, LedgeUITokens.InkDim);
            lbl.fontStyle = FontStyles.UpperCase;
            lbl.characterSpacing = 22f;
            // Bottom-aligned + left-justified so the caption sits flush above
            // the field below it (rather than floating in the middle of an
            // over-sized layout slot).
            lbl.alignment = TextAlignmentOptions.BottomLeft;
            AddLayoutHeight(lbl.gameObject, 16f);
        }

        private static void AddLayoutHeight(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        private static RectTransform BuildHorizontalRow(Transform parent, string name, float height, float spacing, TextAnchor align, bool equalWidth)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = spacing;
            hl.childAlignment = align;
            hl.childControlWidth = equalWidth;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = equalWidth;
            hl.childForceExpandHeight = true;
            AddLayoutHeight(go, height);
            return rt;
        }

        private static TMP_InputField BuildInputField(Transform parent, string name, string initial, string placeholder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.28f); // recessed slot
            var outline = go.GetComponent<Outline>();
            outline.effectColor = LedgeUITokens.PanelEdge2;
            outline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);
            AddLayoutHeight(go, 30f);

            // Text area (mask child)
            var textArea = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            var taRt = (RectTransform)textArea.transform;
            taRt.SetParent(rt, false);
            taRt.anchorMin = Vector2.zero; taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(8f, 4f);
            taRt.offsetMax = new Vector2(-8f, -4f);

            var placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
            var phRt = (RectTransform)placeholderGo.transform;
            phRt.SetParent(taRt, false);
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero; phRt.offsetMax = Vector2.zero;
            var ph = placeholderGo.AddComponent<TextMeshProUGUI>();
            ph.text = placeholder ?? "";
            ph.font = LedgeUITokens.UIFont;
            ph.fontSize = LedgeUITokens.BodySize;
            ph.color = LedgeUITokens.InkMute;
            ph.fontStyle = FontStyles.Italic;
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            // Don't intercept clicks — only the bg image (the InputField's
            // targetGraphic) should be the click target.
            ph.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            var txRt = (RectTransform)textGo.transform;
            txRt.SetParent(taRt, false);
            txRt.anchorMin = Vector2.zero; txRt.anchorMax = Vector2.one;
            txRt.offsetMin = Vector2.zero; txRt.offsetMax = Vector2.zero;
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.font = LedgeUITokens.UIFont;
            txt.fontSize = LedgeUITokens.BodySize;
            txt.color = LedgeUITokens.Ink;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.raycastTarget = false;

            var input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;
            input.textViewport = taRt;
            input.textComponent = txt;
            input.placeholder = ph;
            input.fontAsset = LedgeUITokens.UIFont;
            input.pointSize = LedgeUITokens.BodySize;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.text = initial ?? "";
            return input;
        }

        private static Slider BuildSlider(Transform parent, int initialValue, int min, int max)
        {
            var go = new GameObject("SeatsSlider", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AddLayoutHeight(go, 20f);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f; le.minWidth = 200f;

            // Background track
            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.SetParent(rt, false);
            bgRt.anchorMin = new Vector2(0f, 0.4f);
            bgRt.anchorMax = new Vector2(1f, 0.6f);
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.color = LedgeUITokens.Rule;

            // Fill area
            var fillAreaGo = new GameObject("FillArea", typeof(RectTransform));
            var faRt = (RectTransform)fillAreaGo.transform;
            faRt.SetParent(rt, false);
            faRt.anchorMin = new Vector2(0f, 0.4f);
            faRt.anchorMax = new Vector2(1f, 0.6f);
            faRt.offsetMin = new Vector2(8f, 0f);
            faRt.offsetMax = new Vector2(-8f, 0f);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.SetParent(faRt, false);
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.color = LedgeUITokens.AccentCool;

            // Handle
            var handleAreaGo = new GameObject("HandleArea", typeof(RectTransform));
            var haRt = (RectTransform)handleAreaGo.transform;
            haRt.SetParent(rt, false);
            haRt.anchorMin = Vector2.zero; haRt.anchorMax = Vector2.one;
            haRt.offsetMin = new Vector2(8f, 0f);
            haRt.offsetMax = new Vector2(-8f, 0f);

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var hRt = (RectTransform)handleGo.transform;
            hRt.SetParent(haRt, false);
            hRt.sizeDelta = new Vector2(16f, 16f);
            var hImg = handleGo.GetComponent<Image>();
            hImg.color = LedgeUITokens.Ink;

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = hRt;
            slider.targetGraphic = hImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.value = initialValue;
            return slider;
        }
    }
}
