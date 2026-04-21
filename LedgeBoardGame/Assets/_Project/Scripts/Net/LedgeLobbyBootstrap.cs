using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using MagiGameServer.Contracts.Core;

namespace Magi.LedgeBoardGame.Net
{
    /// Minimal in-scene lobby for the Monday 6-player demo. Replaces
    /// LedgeShadowBootstrap's auto-spawn in builds: shows an IMGUI overlay
    /// at scene start, defers GameController.Start until the user picks
    /// Host or Join, then wires up a single-seat LedgeBoardSessionDriver
    /// via ForHost / ForJoin and attaches it as the controller's shadow
    /// sink so the Network-mode entry points submit through the real
    /// WebSocket transport.
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
        }

        private void Update()
        {
            if (_ready && _driver != null) _driver.Tick();
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) _showQuitConfirm = !_showQuitConfirm;
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

        private void OnGUI()
        {
            if (_showQuitConfirm)
            {
                DrawQuitConfirm();
                return;
            }
            if (_uiState == UiState.Connected)
            {
                DrawConnectedBanner();
                return;
            }

            const int width = 420;
            const int height = 300;
            var rect = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);

            GUI.Box(rect, "Ledge — Lobby");
            GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 28, rect.width - 24, rect.height - 40));

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_mode == LobbyMode.Host, "Host", "Button")) _mode = LobbyMode.Host;
            if (GUILayout.Toggle(_mode == LobbyMode.Join, "Join", "Button")) _mode = LobbyMode.Join;
            if (GUILayout.Toggle(_mode == LobbyMode.Practice, "Practice", "Button")) _mode = LobbyMode.Practice;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Your name (optional):");
            _playerName = GUILayout.TextField(_playerName ?? "");

            if (_mode != LobbyMode.Practice)
            {
                GUILayout.Space(6);
                GUILayout.Label("Host base URI:");
                _hostBaseUri = GUILayout.TextField(_hostBaseUri ?? "");
            }

            if (_mode == LobbyMode.Join)
            {
                GUILayout.Space(6);
                GUILayout.Label("Session code (from host):");
                _sessionCode = GUILayout.TextField(_sessionCode ?? "");
            }

            if (_mode == LobbyMode.Practice)
            {
                GUILayout.Space(6);
                GUILayout.Label($"Seats: {_seatCount}");
                _seatCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(_seatCount, 2, 8));
            }

            GUILayout.Space(8);
            GUI.enabled = _uiState == UiState.Selecting || _uiState == UiState.Failed;
            string buttonLabel = _mode switch
            {
                LobbyMode.Host => "Host new session",
                LobbyMode.Join => "Join session",
                LobbyMode.Practice => "Start practice game",
                _ => "Go",
            };
            if (GUILayout.Button(buttonLabel))
            {
                StartCoroutine(BeginFlow());
            }
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status);
            if (!string.IsNullOrEmpty(_sharedCode))
            {
                GUILayout.Label("Share this code with other players:");
                GUILayout.TextField(_sharedCode);
            }
            GUILayout.EndArea();
        }

        private void DrawConnectedBanner()
        {
            if (_mode != LobbyMode.Host || string.IsNullOrEmpty(_sharedCode)) return;

            const int width = 360;
            const int height = 68;
            var rect = new Rect(12, 12, width, height);
            GUI.Box(rect, "Session code — share with other players");
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 22, rect.width - 16, rect.height - 28));
            GUILayout.BeginHorizontal();
            GUILayout.TextField(_sharedCode);
            if (GUILayout.Button("Copy", GUILayout.Width(60)))
                GUIUtility.systemCopyBuffer = _sharedCode;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawQuitConfirm()
        {
            // While in an active session, Escape should offer two exits:
            // leave-to-lobby (disconnect and re-open the Host/Join overlay)
            // and quit-to-desktop. Before a session is up the menu collapses
            // to the old Cancel/Quit pair because there's nothing to leave.
            bool connected = _uiState == UiState.Connected;
            int height = connected ? 170 : 130;
            const int width = 360;
            var rect = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);
            GUI.Box(rect, connected ? "Leave session?" : "Quit Ledge?");
            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 32, rect.width - 32, rect.height - 44));
            GUILayout.Label(connected
                ? "Return to the lobby or quit to desktop?"
                : "Exit to desktop?");
            GUILayout.Space(10);
            if (connected)
            {
                if (GUILayout.Button("Back to lobby"))
                {
                    _showQuitConfirm = false;
                    ReloadScene();
                    return;
                }
                GUILayout.Space(4);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel")) _showQuitConfirm = false;
            GUILayout.Space(8);
            if (GUILayout.Button("Quit to desktop"))
            {
                _showQuitConfirm = false;
                QuitApplication();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
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

            if (controller == null)
            {
                _uiState = UiState.Failed;
                _status = "No GameController in scene.";
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
                    yield break;
                }
                controller.enabled = true;
                yield return null;
                _uiState = UiState.Connected;
                _status = $"Practice ({seats} seats).";
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
                    yield break;
                }
                controller.enabled = true;
                yield return null;

                _status = "Connecting to server…";
                var hostTask = ConnectAndAttachAsync(joinSession: null);
                while (!hostTask.IsCompleted) yield return null;
                if (hostTask.IsFaulted)
                {
                    var ex = hostTask.Exception?.GetBaseException();
                    _uiState = UiState.Failed;
                    _status = $"Connect failed: {ex?.Message ?? "unknown"}";
                    UnityEngine.Debug.LogError($"[lobby] connect failed: {ex}");
                    yield break;
                }
            }
            else
            {
                _status = "Connecting to server…";
                var joinTask = ConnectAndAttachAsync(joinSession);
                while (!joinTask.IsCompleted) yield return null;
                if (joinTask.IsFaulted)
                {
                    var ex = joinTask.Exception?.GetBaseException();
                    _uiState = UiState.Failed;
                    _status = $"Connect failed: {ex?.Message ?? "unknown"}";
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
            _driver = joinSession.HasValue
                ? LedgeBoardSessionDriver.ForJoinClaim(_seatCount, joinSession.Value)
                : LedgeBoardSessionDriver.ForHost(_seatCount, _ownedSeat);
            try
            {
                if (joinSession.HasValue)
                    await _driver.JoinClaimAsync(_hostBaseUri, specJson, _cts.Token);
                else
                    await _driver.HostNewAsync(_hostBaseUri, specJson, _cts.Token);
            }
            catch
            {
                try { await _driver.DisposeAsync(); } catch { }
                _driver = null;
                throw;
            }
            // Host: controller already Start-ed with the right seat, attach now.
            // Join: controller isn't enabled yet — AttachShadowSink will happen
            // after BeginFlow reads the claimed seat and configures the controller.
            if (!joinSession.HasValue)
                controller.AttachShadowSink(_driver);
        }
    }
}
