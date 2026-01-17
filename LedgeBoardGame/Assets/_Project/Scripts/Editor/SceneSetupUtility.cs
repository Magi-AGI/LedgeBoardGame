using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using Magi.LedgeBoardGame.Board;

namespace Magi.LedgeBoardGame.Editor
{
    /// <summary>
    /// Editor utility to set up the Ledge Board Game scene with all required UI elements.
    /// Run from menu: Ledge > Setup Scene
    /// </summary>
    public static class SceneSetupUtility
    {
        private const string SpaceViewPrefabPath = "Assets/_Project/Prefabs/SpaceView.prefab";
        private const string BoardPresenterPrefabPath = "Assets/_Project/Prefabs/BoardPresenter.prefab";
        private const string LedgeSpecPath = "Assets/_Project/Specs/ledge/ledge-game.v1.json";

        [MenuItem("Ledge/Setup Scene", false, 100)]
        public static void SetupScene()
        {
            // Create SpaceView prefab if it doesn't exist or needs updating
            CreateOrUpdateSpaceViewPrefab();

            // Create BoardPresenter prefab if needed
            CreateOrUpdateBoardPresenterPrefab();

            // Setup scene hierarchy
            SetupSceneHierarchy();

            Debug.Log("Ledge scene setup complete!");
        }

        [MenuItem("Ledge/Create SpaceView Prefab", false, 200)]
        public static void CreateOrUpdateSpaceViewPrefab()
        {
            // Check if prefab already exists
            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpaceViewPrefabPath);

            // Create a new SpaceView GameObject
            var spaceViewGO = new GameObject("SpaceView");
            var rectTransform = spaceViewGO.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(60f, 60f);

            // Add CanvasRenderer
            spaceViewGO.AddComponent<CanvasRenderer>();

            // Add Image component for the space background
            var bgImage = spaceViewGO.AddComponent<Image>();
            bgImage.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
            bgImage.raycastTarget = true;

            // Add SpaceView component
            var spaceView = spaceViewGO.AddComponent<SpaceView>();

            // Create highlight child
            var highlight = CreateUIChild(spaceViewGO, "Highlight", new Vector2(64f, 64f));
            var highlightImg = highlight.AddComponent<Image>();
            highlightImg.color = new Color(0.4f, 0.9f, 0.4f, 0f); // Transparent by default
            highlightImg.raycastTarget = false;

            // Create token display container
            var tokenDisplay = CreateUIChild(spaceViewGO, "TokenDisplay", new Vector2(56f, 24f));
            var tokenRect = tokenDisplay.GetComponent<RectTransform>();
            tokenRect.anchoredPosition = new Vector2(0f, -12f);

            // Create Light count text
            var lightText = CreateTextChild(tokenDisplay, "LightCount", "0", new Vector2(-12f, 0f));
            lightText.color = new Color(0.97f, 0.97f, 0.94f); // Cream white

            // Create Dark count text
            var darkText = CreateTextChild(tokenDisplay, "DarkCount", "0", new Vector2(12f, 0f));
            darkText.color = new Color(0.17f, 0.17f, 0.17f); // Charcoal

            // Create lock indicator
            var lockIndicator = CreateUIChild(spaceViewGO, "LockIndicator", new Vector2(16f, 16f));
            var lockRect = lockIndicator.GetComponent<RectTransform>();
            lockRect.anchoredPosition = new Vector2(20f, 20f);
            var lockImg = lockIndicator.AddComponent<Image>();
            lockImg.color = new Color(1f, 0.8f, 0f, 1f); // Gold
            lockImg.raycastTarget = false;
            lockIndicator.SetActive(false);

            // Wire up SpaceView serialized fields using SerializedObject
            var so = new SerializedObject(spaceView);
            so.FindProperty("highlightEffect").objectReferenceValue = highlight;
            so.FindProperty("highlightImage").objectReferenceValue = highlightImg;
            so.FindProperty("lockIndicator").objectReferenceValue = lockIndicator;
            so.FindProperty("lightCountTMP").objectReferenceValue = lightText;
            so.FindProperty("darkCountTMP").objectReferenceValue = darkText;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Save as prefab
            EnsureDirectoryExists("Assets/_Project/Prefabs");

            if (existingPrefab != null)
            {
                // Update existing prefab
                PrefabUtility.SaveAsPrefabAsset(spaceViewGO, SpaceViewPrefabPath);
                Debug.Log("Updated SpaceView prefab at: " + SpaceViewPrefabPath);
            }
            else
            {
                // Create new prefab
                PrefabUtility.SaveAsPrefabAsset(spaceViewGO, SpaceViewPrefabPath);
                Debug.Log("Created SpaceView prefab at: " + SpaceViewPrefabPath);
            }

            Object.DestroyImmediate(spaceViewGO);
        }

        [MenuItem("Ledge/Create BoardPresenter Prefab", false, 201)]
        public static void CreateOrUpdateBoardPresenterPrefab()
        {
            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoardPresenterPrefabPath);

            var boardPresenterGO = new GameObject("BoardPresenter");
            var rectTransform = boardPresenterGO.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(900f, 900f);

            var presenter = boardPresenterGO.AddComponent<BoardPresenter>();

            // Load SpaceView prefab and assign it
            var spaceViewPrefab = AssetDatabase.LoadAssetAtPath<SpaceView>(SpaceViewPrefabPath);
            if (spaceViewPrefab != null)
            {
                var so = new SerializedObject(presenter);
                so.FindProperty("spaceViewPrefab").objectReferenceValue = spaceViewPrefab;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EnsureDirectoryExists("Assets/_Project/Prefabs");
            PrefabUtility.SaveAsPrefabAsset(boardPresenterGO, BoardPresenterPrefabPath);
            Debug.Log("Created/Updated BoardPresenter prefab at: " + BoardPresenterPrefabPath);

            Object.DestroyImmediate(boardPresenterGO);
        }

        [MenuItem("Ledge/Setup Scene Hierarchy", false, 202)]
        public static void SetupSceneHierarchy()
        {
            // Find or create Canvas
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGO = new GameObject("Canvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
            }

            // Configure CanvasScaler
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            // Find or create GameController
            var gameController = Object.FindFirstObjectByType<GameController>();
            if (gameController == null)
            {
                var gcGO = new GameObject("GameController");
                gameController = gcGO.AddComponent<GameController>();
            }

            // Find or create GameHud
            var gameHud = Object.FindFirstObjectByType<GameHud>();
            if (gameHud == null)
            {
                var hudGO = new GameObject("GameHud");
                hudGO.transform.SetParent(canvas.transform, false);
                gameHud = hudGO.AddComponent<GameHud>();
            }

            // Create HUD UI elements under canvas
            SetupHudUI(canvas.transform, gameHud);

            // Find or create MultiBoardLayout
            var multiBoardLayout = Object.FindFirstObjectByType<MultiBoardLayout>();
            if (multiBoardLayout == null)
            {
                var boardContainer = new GameObject("BoardContainer");
                boardContainer.transform.SetParent(canvas.transform, false);
                var bcRect = boardContainer.AddComponent<RectTransform>();
                bcRect.anchorMin = new Vector2(0.5f, 0.5f);
                bcRect.anchorMax = new Vector2(0.5f, 0.5f);
                bcRect.anchoredPosition = Vector2.zero;
                bcRect.sizeDelta = new Vector2(1600f, 900f);

                multiBoardLayout = boardContainer.AddComponent<MultiBoardLayout>();
            }

            // Wire up GameController references
            WireGameControllerReferences(gameController, gameHud, multiBoardLayout);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("Scene hierarchy setup complete. Save the scene to persist changes.");
        }

        private static void SetupHudUI(Transform canvasTransform, GameHud gameHud)
        {
            // Create HUD panel at top of screen
            var hudPanel = CreateUIChild(canvasTransform.gameObject, "HudPanel", new Vector2(600f, 120f));
            var hudRect = hudPanel.GetComponent<RectTransform>();
            hudRect.anchorMin = new Vector2(0.5f, 1f);
            hudRect.anchorMax = new Vector2(0.5f, 1f);
            hudRect.anchoredPosition = new Vector2(0f, -70f);

            var panelImg = hudPanel.AddComponent<Image>();
            panelImg.color = new Color(0.1f, 0.1f, 0.15f, 0.9f);
            panelImg.raycastTarget = false;

            // Create phase text
            var phaseTextGO = CreateUIChild(hudPanel, "PhaseText", new Vector2(200f, 30f));
            var phaseRect = phaseTextGO.GetComponent<RectTransform>();
            phaseRect.anchoredPosition = new Vector2(-150f, 30f);
            var phaseText = phaseTextGO.AddComponent<Text>();
            phaseText.text = "Phase: Placement";
            phaseText.fontSize = 18;
            phaseText.color = Color.white;
            phaseText.alignment = TextAnchor.MiddleLeft;
            phaseText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Create current player text
            var playerTextGO = CreateUIChild(hudPanel, "PlayerText", new Vector2(200f, 30f));
            var playerRect = playerTextGO.GetComponent<RectTransform>();
            playerRect.anchoredPosition = new Vector2(150f, 30f);
            var playerText = playerTextGO.AddComponent<Text>();
            playerText.text = "Player: Player1";
            playerText.fontSize = 18;
            playerText.color = Color.white;
            playerText.alignment = TextAnchor.MiddleRight;
            playerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Create status text
            var statusTextGO = CreateUIChild(hudPanel, "StatusText", new Vector2(550f, 30f));
            var statusRect = statusTextGO.GetComponent<RectTransform>();
            statusRect.anchoredPosition = new Vector2(0f, -10f);
            var statusText = statusTextGO.AddComponent<Text>();
            statusText.text = "Place one Light and one Dark token.";
            statusText.fontSize = 16;
            statusText.color = new Color(0.8f, 0.8f, 0.8f);
            statusText.alignment = TextAnchor.MiddleCenter;
            statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Create End Turn button
            var buttonGO = CreateUIChild(hudPanel, "EndTurnButton", new Vector2(120f, 36f));
            var buttonRect = buttonGO.GetComponent<RectTransform>();
            buttonRect.anchoredPosition = new Vector2(0f, -45f);

            var buttonImg = buttonGO.AddComponent<Image>();
            buttonImg.color = new Color(0.2f, 0.4f, 0.6f);

            var button = buttonGO.AddComponent<Button>();
            button.targetGraphic = buttonImg;

            var buttonTextGO = CreateUIChild(buttonGO, "Text", new Vector2(120f, 36f));
            var buttonText = buttonTextGO.AddComponent<Text>();
            buttonText.text = "End Turn";
            buttonText.fontSize = 16;
            buttonText.color = Color.white;
            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Wire up GameHud
            var hudSO = new SerializedObject(gameHud);
            hudSO.FindProperty("phaseText").objectReferenceValue = phaseText;
            hudSO.FindProperty("currentPlayerText").objectReferenceValue = playerText;
            hudSO.FindProperty("statusText").objectReferenceValue = statusText;
            hudSO.FindProperty("endTurnButton").objectReferenceValue = button;
            hudSO.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireGameControllerReferences(GameController gameController, GameHud gameHud, MultiBoardLayout multiBoardLayout)
        {
            var so = new SerializedObject(gameController);

            // Load and assign BoardPresenter prefab
            var boardPresenterPrefab = AssetDatabase.LoadAssetAtPath<BoardPresenter>(BoardPresenterPrefabPath);
            if (boardPresenterPrefab != null)
            {
                so.FindProperty("boardPresenterPrefab").objectReferenceValue = boardPresenterPrefab;
            }

            // Load and assign LedgeSpec JSON
            var ledgeSpec = AssetDatabase.LoadAssetAtPath<TextAsset>(LedgeSpecPath);
            if (ledgeSpec != null)
            {
                so.FindProperty("ledgeSpecJson").objectReferenceValue = ledgeSpec;
            }

            // Assign GameHud
            so.FindProperty("gameHud").objectReferenceValue = gameHud;

            // Assign MultiBoardLayout
            so.FindProperty("multiBoardLayout").objectReferenceValue = multiBoardLayout;

            // Find and assign EndTurn button from GameHud
            var hudSO = new SerializedObject(gameHud);
            var endTurnButton = hudSO.FindProperty("endTurnButton").objectReferenceValue as Button;
            if (endTurnButton != null)
            {
                so.FindProperty("endTurnButton").objectReferenceValue = endTurnButton;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("GameController references wired successfully.");
        }

        private static GameObject CreateUIChild(GameObject parent, string name, Vector2 size)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);

            var rectTransform = child.AddComponent<RectTransform>();
            rectTransform.sizeDelta = size;
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;

            child.AddComponent<CanvasRenderer>();

            return child;
        }

        private static TextMeshProUGUI CreateTextChild(GameObject parent, string name, string text, Vector2 position)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);

            var rectTransform = child.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(24f, 24f);
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = position;

            var tmp = child.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 14;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            return tmp;
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                var parts = path.Split('/');
                var currentPath = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    var newPath = currentPath + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(newPath))
                    {
                        AssetDatabase.CreateFolder(currentPath, parts[i]);
                    }
                    currentPath = newPath;
                }
            }
        }
    }
}
