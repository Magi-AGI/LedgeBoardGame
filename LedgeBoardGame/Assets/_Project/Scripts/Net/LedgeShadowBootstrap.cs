using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Magi.LedgeBoardGame.Net
{
    /// Scene-level glue for M6c3a. Constructs one LedgeBoardSessionDriver
    /// per scene, mirrors the GameController's committed actions through
    /// it, and pumps the driver's Tick() each frame so inbound echoes
    /// drain on the Unity main thread.
    ///
    /// Why a MonoBehaviour and not a plain [RuntimeInitializeOnLoadMethod]:
    /// the driver is async (ConnectAsync per seat) and has a meaningful
    /// lifetime — Tick from Update, DisposeAsync on scene unload — so
    /// anchoring it to a scene object is the cleanest lifecycle match.
    /// A static RuntimeInitialize hook (below) spawns one automatically
    /// on AfterSceneLoad so neither the scene nor any prefab has to carry
    /// the reference; drop the .cs in and shadow mode runs on every play.
    ///
    /// Execution order: runs AFTER GameController.Start so we can call
    /// GetLedgeSpecJson() and AttachShadowSink on a fully-initialised
    /// controller. The [DefaultExecutionOrder] attribute pins that order;
    /// the MonoBehaviour's own Start awaits the driver's StartAsync so
    /// the very first shadow submission lands after ConnectAsync completes.
    ///
    /// Shadow mode is editor-by-default: EnableInBuild must be set to
    /// run outside the editor. The intent is for playtest builds to stay
    /// a pure local game until authority flips in M6c3b; the shadow
    /// submissions and Session overhead are strictly development tooling.
    [DefaultExecutionOrder(100)]
    public sealed class LedgeShadowBootstrap : MonoBehaviour
    {
        [SerializeField] private GameController controller;
        [Tooltip("When off, shadow mode only activates inside the Unity editor. Leave off for shipped builds.")]
        [SerializeField] private bool enableInBuild = false;

        /// Auto-spawn hook. Fires once after the first scene loads (and
        /// again for every subsequent scene load via SceneManager.sceneLoaded)
        /// so a fresh scene without a hand-placed bootstrap still gets a
        /// live shadow driver. If any LedgeShadowBootstrap already exists —
        /// either a hand-placed component or a previous auto-spawn that
        /// survived a DontDestroyOnLoad — the spawn is skipped so we never
        /// run two drivers against the same controller.
        ///
        /// Gated by Application.isEditor to match the instance-level default:
        /// shipped builds do not auto-spawn, preserving the "pure local game
        /// until M6c3b flips authority" invariant. A hand-placed component
        /// with enableInBuild=true is still honoured in builds.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (!Application.isEditor) return;
            TrySpawnInScene(SceneManager.GetActiveScene());
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Application.isEditor) return;
            TrySpawnInScene(scene);
        }

        private static void TrySpawnInScene(Scene scene)
        {
            // Only auto-spawn when a GameController is actually in the
            // scene. Menus, splash scenes, and editor-only utility scenes
            // have no controller and therefore nothing to shadow; skipping
            // keeps the Session/transport/MagiSession overhead out of those.
            if (FindAnyObjectByType<GameController>() == null) return;
            if (FindAnyObjectByType<LedgeShadowBootstrap>() != null) return;
            var go = new GameObject(nameof(LedgeShadowBootstrap));
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<LedgeShadowBootstrap>();
        }

        private LedgeBoardSessionDriver _driver;
        private CancellationTokenSource _cts;
        private bool _ready;

        private async void Start()
        {
            if (!Application.isEditor && !enableInBuild) return;

            if (controller == null) controller = FindAnyObjectByType<GameController>();
            if (controller == null)
            {
                UnityEngine.Debug.LogError("[shadow] bootstrap: no GameController found in scene; shadow mode disabled.");
                return;
            }

            int seatCount = controller.PlayerCount;
            _driver = new LedgeBoardSessionDriver(seatCount);
            _cts = new CancellationTokenSource();

            try
            {
                // ledgeSpecJson may be null when GameController is running
                // with defaults; LedgeGameModule tolerates a missing spec
                // key and the driver will initialise a bare state that
                // still hashes-identical to the client's default state.
                await _driver.StartAsync(controller.GetLedgeSpecJson(), _cts.Token);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[shadow] bootstrap: driver StartAsync failed: {ex}");
                await DisposeDriverAsync();
                return;
            }

            controller.AttachShadowSink(_driver);
            _ready = true;
        }

        private void Update()
        {
            if (!_ready || _driver == null) return;
            _driver.Tick();
        }

        private async void OnDestroy()
        {
            _ready = false;
            try { _cts?.Cancel(); } catch { /* cancellation races are fine */ }
            await DisposeDriverAsync();
            _cts?.Dispose();
            _cts = null;
        }

        private async Task DisposeDriverAsync()
        {
            if (_driver == null) return;
            try { await _driver.DisposeAsync(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogError($"[shadow] bootstrap: driver dispose failed: {ex}"); }
            _driver = null;
        }
    }
}
