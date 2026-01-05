using UnityEngine;
using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Config;
using Magi.LedgeBoardGame.Builder;
using Magi.LedgeBoardGame.Debug;

namespace Magi.LedgeBoardGame.Board
{
    /// <summary>
    /// Automatically positions board spaces in a hexagonal layout.
    /// Attach this to your Board GameObject and run "Position All Spaces".
    /// </summary>
    public class BoardSpacePositioner : MonoBehaviour
    {
        [Header("Layout Settings")]
        [SerializeField] private float centerRadius = 0f;
        [SerializeField] private float innerRingRadius = 100f;
        [SerializeField] private float ring2Radius = 200f;
        [SerializeField] private float ring3Radius = 300f;
        [SerializeField] private float outerRadius = 400f;
        [SerializeField] private float ledgeRadius = 450f;

        [Header("Space Prefab")]
        [SerializeField] private GameObject spacePrefab;
        [SerializeField] private Transform spacesContainer;

        [Header("Configuration")]
        [SerializeField] private BoardLayoutConfig layoutConfig;
        [SerializeField] private bool autoGenerateOnStart = false;

        private Dictionary<int, Transform> spaceTransforms = new Dictionary<int, Transform>();
        private BoardState boardState;

        private void Start()
        {
            if (autoGenerateOnStart)
            {
                GenerateBoardSpaces();
            }
        }

        [ContextMenu("Generate Board Spaces")]
        public void GenerateBoardSpaces()
        {
            ClearExistingSpaces();

            // Use BoardGraphBuilder to create the board structure
            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            boardState = builder.BuildBoard(0, 0);

            CreateSpaceObjects();
            PositionSpaces();

            // Add visualizer if not present
            var visualizer = GetComponent<BoardGraphVisualizer>();
            if (visualizer == null)
            {
                visualizer = gameObject.AddComponent<BoardGraphVisualizer>();
            }
            visualizer.Initialize(boardState, spaceTransforms);

            UnityEngine.Debug.Log($"Generated {spaceTransforms.Count} board spaces");
        }

        private void ClearExistingSpaces()
        {
            if (spacesContainer == null)
                spacesContainer = transform;

            // Clear existing spaces
            for (int i = spacesContainer.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(spacesContainer.GetChild(i).gameObject);
            }

            spaceTransforms.Clear();
        }

        private void CreateSpaceObjects()
        {
            if (spacePrefab == null)
            {
                // Create a default space prefab if none provided
                UnityEngine.Debug.LogWarning("No space prefab provided, creating default UI button");
            }

            foreach (var kvp in boardState.SpaceMetadata)
            {
                int spaceId = kvp.Key;
                SpaceMeta meta = kvp.Value;

                GameObject spaceObj = CreateSpaceObject(spaceId, meta);
                spaceTransforms[spaceId] = spaceObj.transform;
            }
        }

        private GameObject CreateSpaceObject(int spaceId, SpaceMeta meta)
        {
            GameObject spaceObj;

            if (spacePrefab != null)
            {
                spaceObj = Instantiate(spacePrefab, spacesContainer);
            }
            else
            {
                // Create default UI element
                spaceObj = new GameObject($"Space_{spaceId}");
                spaceObj.transform.SetParent(spacesContainer, false);

                // Add RectTransform for UI positioning
                var rectTransform = spaceObj.AddComponent<RectTransform>();
                rectTransform.sizeDelta = new Vector2(60, 60);

                // Add basic visual
                var image = spaceObj.AddComponent<UnityEngine.UI.Image>();
                image.color = GetSpaceColor(meta);

                // Add button component
                spaceObj.AddComponent<UnityEngine.UI.Button>();
            }

            spaceObj.name = GetSpaceName(spaceId, meta);

            // Add SpaceView component if not present
            var spaceView = spaceObj.GetComponent<SpaceView>();
            if (spaceView == null)
            {
                spaceView = spaceObj.AddComponent<SpaceView>();
            }

            // Initialize with empty stack
            var emptyStack = new TokenStack();
            spaceView.SetData(spaceId, meta, emptyStack);

            return spaceObj;
        }

        private void PositionSpaces()
        {
            foreach (var kvp in boardState.SpaceMetadata)
            {
                int spaceId = kvp.Key;
                SpaceMeta meta = kvp.Value;

                if (!spaceTransforms.ContainsKey(spaceId))
                    continue;

                var radii = new BoardLayoutHelper.Radii
                {
                    Center = centerRadius,
                    Inner = innerRingRadius,
                    Ring2 = ring2Radius,
                    Ring3 = ring3Radius,
                    Outer = outerRadius,
                    Ledge = ledgeRadius
                };

                Vector2 position = BoardLayoutHelper.ComputePosition(spaceId, meta, radii);

                var rectTransform = spaceTransforms[spaceId].GetComponent<RectTransform>();
                if (rectTransform != null)
                {
                    rectTransform.anchoredPosition = position;
                }
                else
                {
                    spaceTransforms[spaceId].localPosition = position;
                }
            }
        }

        private Color GetSpaceColor(SpaceMeta meta)
        {
            switch (meta.Type)
            {
                case SpaceType.Center:
                    return Color.yellow;
                case SpaceType.InnerBridge:
                    return new Color(0.5f, 0.7f, 1f); // Light blue
                case SpaceType.InnerStop:
                    return new Color(0.7f, 0.7f, 0.7f); // Gray
                case SpaceType.Ledge:
                    return GetLedgeColor(meta.ColorLabel);
                default:
                    return new Color(0.9f, 0.9f, 0.9f); // Light gray
            }
        }

        private Color GetLedgeColor(string colorLabel)
        {
            switch (colorLabel)
            {
                case "Ela": return Color.red;
                case "Biz": return Color.blue;
                case "Yun": return Color.yellow;
                case "Jutu": return Color.green;
                case "Glei": return Color.cyan;
                case "Sace": return Color.magenta;
                case "Rha": return new Color(1f, 0.5f, 0f); // Orange
                case "Dau": return new Color(0.5f, 0f, 0.5f); // Purple
                case "Wim": return new Color(0f, 1f, 0.5f); // Lime
                case "Pfi": return new Color(1f, 0.75f, 0.8f); // Pink
                case "Quae": return Color.gray;
                case "Vei": return new Color(0.6f, 0.4f, 0.2f); // Brown
                default: return Color.white;
            }
        }

        private string GetSpaceName(int spaceId, SpaceMeta meta)
        {
            string name = $"Space_{spaceId:00}_{meta.Type}";

            if (meta.Type == SpaceType.Ledge && !string.IsNullOrEmpty(meta.ColorLabel))
            {
                name += $"_{meta.ColorLabel}";
            }

            return name;
        }

        [ContextMenu("Position All Spaces")]
        public void PositionAllSpaces()
        {
            // Find all existing space objects
            spaceTransforms.Clear();

            foreach (Transform child in spacesContainer)
            {
                var spaceView = child.GetComponent<SpaceView>();
                if (spaceView != null)
                {
                    // Extract space ID from name or component
                    string name = child.name;
                    if (name.StartsWith("Space_"))
                    {
                        string idStr = name.Substring(6, 2);
                        if (int.TryParse(idStr, out int spaceId))
                        {
                            spaceTransforms[spaceId] = child;
                        }
                    }
                }
            }

            if (spaceTransforms.Count > 0)
            {
                PositionSpaces();
                UnityEngine.Debug.Log($"Positioned {spaceTransforms.Count} spaces");
            }
            else
            {
                UnityEngine.Debug.LogWarning("No spaces found to position. Run 'Generate Board Spaces' first.");
            }
        }
    }
}


