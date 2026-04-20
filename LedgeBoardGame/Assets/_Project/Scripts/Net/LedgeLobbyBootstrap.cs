using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
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
        }

        [SerializeField] private GameController controller;
        [Tooltip("Default WebSocket-capable base URI for the MagiGameServer.Host launcher. The player can edit this in the overlay before clicking Go. Use https://play.magi-agi.org/ledge for prod (nginx strips the /ledge prefix to the loopback LedgeBoardGame.Host); http://localhost:5080 for a local dev host.")]
        [SerializeField] private string defaultHostBaseUri = "https://play.magi-agi.org/ledge";
        [Tooltip("Pre-seed for the Host mode seat count slider. Clamped to LedgeGameModule's 2..8 range by the overlay.")]
        [SerializeField, Range(2, 8)] private int defaultSeatCount = 6;
        [Tooltip("Pre-seed for the local seat index. Host defaults to 0; Join players should match whatever slot the host advertised.")]
        [SerializeField, Range(0, 7)] private int defaultOwnedSeat = 0;
        [Tooltip("Pre-seed for the Join session code. Empty until a host shares a code; leave blank at build time.")]
        [SerializeField] private string defaultSessionCode = "";

        private LobbyMode _mode = LobbyMode.Host;
        private int _seatCount;
        private int _ownedSeat;
        private string _sessionCode;
        private string _hostBaseUri;

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
            _ownedSeat = Mathf.Clamp(defaultOwnedSeat, 0, _seatCount - 1);
            _sessionCode = defaultSessionCode ?? "";
            _hostBaseUri = string.IsNullOrEmpty(defaultHostBaseUri)
                ? "http://localhost:5080"
                : defaultHostBaseUri;
        }

        private void Update()
        {
            if (_ready && _driver != null) _driver.Tick();
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
            if (_uiState == UiState.Connected) return;

            const int width = 420;
            const int height = 320;
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
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Host base URI:");
            _hostBaseUri = GUILayout.TextField(_hostBaseUri ?? "");

            if (_mode == LobbyMode.Host)
            {
                GUILayout.Label($"Seats (2–8): {_seatCount}");
                _seatCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(_seatCount, 2f, 8f));
                if (_ownedSeat >= _seatCount) _ownedSeat = _seatCount - 1;
                GUILayout.Label($"Your seat: {_ownedSeat} (0-based)");
                _ownedSeat = Mathf.RoundToInt(GUILayout.HorizontalSlider(_ownedSeat, 0f, _seatCount - 1));
            }
            else
            {
                GUILayout.Label("Session code:");
                _sessionCode = GUILayout.TextField(_sessionCode ?? "");
                GUILayout.Label($"Expected seats (must match host): {_seatCount}");
                _seatCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(_seatCount, 2f, 8f));
                if (_ownedSeat >= _seatCount) _ownedSeat = _seatCount - 1;
                GUILayout.Label($"Your seat: {_ownedSeat} (0-based)");
                _ownedSeat = Mathf.RoundToInt(GUILayout.HorizontalSlider(_ownedSeat, 0f, _seatCount - 1));
            }

            GUILayout.Space(8);
            GUI.enabled = _uiState == UiState.Selecting || _uiState == UiState.Failed;
            if (GUILayout.Button(_mode == LobbyMode.Host ? "Host new session" : "Join session"))
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

            SessionId? joinSession = null;
            if (_mode == LobbyMode.Join)
            {
                if (string.IsNullOrWhiteSpace(_sessionCode))
                {
                    _uiState = UiState.Failed;
                    _status = "Session code required for Join.";
                    yield break;
                }
                joinSession = new SessionId(_sessionCode.Trim());
            }

            // Apply overrides before the controller gets its first Start. If the
            // scene was mis-configured (controller already initialised, e.g. a
            // hot-reload leftover), ConfigureNetwork throws and we stop here.
            Exception configError = null;
            try
            {
                controller.ConfigureNetwork(GameController.NetworkMode.Network, _ownedSeat, _seatCount);
            }
            catch (Exception ex) { configError = ex; }
            if (configError != null)
            {
                _uiState = UiState.Failed;
                _status = $"Configure failed: {configError.Message}";
                yield break;
            }
            controller.enabled = true;
            // One frame for Unity to run GameController.Start so _gameState,
            // board presenters, and the local seat id are live before the
            // first server echo lands.
            yield return null;

            _status = "Connecting to server…";
            var connectTask = ConnectAndAttachAsync(joinSession);
            while (!connectTask.IsCompleted) yield return null;
            if (connectTask.IsFaulted)
            {
                var ex = connectTask.Exception?.GetBaseException();
                _uiState = UiState.Failed;
                _status = $"Connect failed: {ex?.Message ?? "unknown"}";
                UnityEngine.Debug.LogError($"[lobby] connect failed: {ex}");
                yield break;
            }

            _ready = true;
            _uiState = UiState.Connected;
            _status = "Connected.";
            if (_mode == LobbyMode.Host && _driver?.ActiveSessionId.HasValue == true)
                _sharedCode = _driver.ActiveSessionId.Value.Value;
        }

        private async Task ConnectAndAttachAsync(SessionId? joinSession)
        {
            _cts = new CancellationTokenSource();
            string specJson = controller.GetLedgeSpecJson();
            _driver = joinSession.HasValue
                ? LedgeBoardSessionDriver.ForJoin(_seatCount, joinSession.Value, _ownedSeat)
                : LedgeBoardSessionDriver.ForHost(_seatCount, _ownedSeat);
            try
            {
                if (joinSession.HasValue)
                    await _driver.JoinHostedAsync(_hostBaseUri, specJson, _cts.Token);
                else
                    await _driver.HostNewAsync(_hostBaseUri, specJson, _cts.Token);
            }
            catch
            {
                try { await _driver.DisposeAsync(); } catch { }
                _driver = null;
                throw;
            }
            controller.AttachShadowSink(_driver);
        }
    }
}
