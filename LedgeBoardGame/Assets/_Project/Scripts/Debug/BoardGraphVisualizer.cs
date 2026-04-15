using UnityEngine;
using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Board;
using Magi.LedgeBoardGame.Config;

namespace Magi.LedgeBoardGame.Debug
{
    /// <summary>
    /// Visualizes the adjacency graph between board spaces using Gizmos.
    /// Attach this to your BoardPresenter to see connections in Scene view.
    /// </summary>
    public class BoardGraphVisualizer : MonoBehaviour
    {
        [Header("Visualization Settings")]
        [SerializeField] private bool showAdjacency = true;
        [SerializeField] private bool showSpaceIds = true;
        [SerializeField] private bool showSpaceTypes = false;
        [SerializeField] private bool highlightLedgeSpaces = true;
        [SerializeField] private bool showCrossBoardConnections = false;  // Reserved for future use

        [Header("Colors")]
        [SerializeField] private Color adjacencyColor = Color.green;
        [SerializeField] private Color ledgeConnectionColor = Color.cyan;
        [SerializeField] private Color centerColor = Color.yellow;
        [SerializeField] private Color bridgeColor = Color.blue;
        [SerializeField] private Color ledgeColor = Color.magenta;

        [Header("References")]
        [SerializeField] private BoardLayoutConfig layoutConfig;

        private BoardState boardState;
        private Dictionary<int, Transform> spaceTransforms;

        public void Initialize(BoardState state, Dictionary<int, Transform> transforms)
        {
            boardState = state;
            spaceTransforms = transforms;
        }

        private void OnDrawGizmos()
        {
            if (!showAdjacency || boardState == null || spaceTransforms == null)
                return;

            DrawAdjacencyConnections();
            DrawSpaceLabels();
            HighlightSpecialSpaces();
        }

        private void DrawAdjacencyConnections()
        {
            foreach (var kvp in boardState.Adjacency)
            {
                int fromId = kvp.Key;
                List<int> toIds = kvp.Value;

                if (!spaceTransforms.ContainsKey(fromId))
                    continue;

                Transform fromTransform = spaceTransforms[fromId];

                foreach (int toId in toIds)
                {
                    if (!spaceTransforms.ContainsKey(toId))
                        continue;

                    Transform toTransform = spaceTransforms[toId];

                    // Choose color based on space types
                    Color lineColor = adjacencyColor;

                    if (boardState.IsLedgeSpace(fromId) || boardState.IsLedgeSpace(toId))
                    {
                        lineColor = ledgeConnectionColor;
                    }

                    Gizmos.color = lineColor;
                    Gizmos.DrawLine(fromTransform.position, toTransform.position);

                    // Draw arrowhead to show direction
                    Vector3 direction = (toTransform.position - fromTransform.position).normalized;
                    Vector3 arrowPos = fromTransform.position + direction * 0.3f;
                    DrawArrowhead(arrowPos, direction, 0.1f);
                }
            }
        }

        private void DrawSpaceLabels()
        {
            if (!showSpaceIds && !showSpaceTypes)
                return;

            foreach (var kvp in spaceTransforms)
            {
                int spaceId = kvp.Key;
                Transform spaceTransform = kvp.Value;

                string label = "";

                if (showSpaceIds)
                {
                    label = spaceId.ToString();
                }

                if (showSpaceTypes && boardState.SpaceMetadata.ContainsKey(spaceId))
                {
                    var meta = boardState.SpaceMetadata[spaceId];
                    if (showSpaceIds)
                        label += "\n";
                    label += meta.Type.ToString();

                    if (!string.IsNullOrEmpty(meta.ColorLabel))
                    {
                        label += $"\n({meta.ColorLabel})";
                    }
                }

#if UNITY_EDITOR
                UnityEditor.Handles.Label(spaceTransform.position, label);
#endif
            }
        }

        private void HighlightSpecialSpaces()
        {
            if (!highlightLedgeSpaces)
                return;

            foreach (var kvp in spaceTransforms)
            {
                int spaceId = kvp.Key;
                Transform spaceTransform = kvp.Value;

                Color highlightColor = Color.white;
                float size = 0.2f;

                if (boardState.IsCenterSpace(spaceId))
                {
                    highlightColor = centerColor;
                    size = 0.3f;
                }
                else if (boardState.SpaceMetadata.ContainsKey(spaceId))
                {
                    var meta = boardState.SpaceMetadata[spaceId];

                    if (!string.IsNullOrEmpty(meta.ColorLabel))
                    {
                        highlightColor = GetLedgeColor(meta.ColorLabel);
                        size = 0.25f;
                    }
                    else if (meta.Type == SpaceType.InnerBridge)
                    {
                        highlightColor = bridgeColor;
                    }
                }

                Gizmos.color = highlightColor;
                Gizmos.DrawWireSphere(spaceTransform.position, size);
            }
        }

        private Color GetLedgeColor(string colorLabel)
        {
            // Map ledge color labels to Unity colors
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
                default: return ledgeColor;
            }
        }

        private void DrawArrowhead(Vector3 position, Vector3 direction, float size)
        {
            Vector3 right = Quaternion.Euler(0, 0, -30) * -direction;
            Vector3 left = Quaternion.Euler(0, 0, 30) * -direction;

            Gizmos.DrawLine(position, position + right * size);
            Gizmos.DrawLine(position, position + left * size);
        }

        [ContextMenu("Export Adjacency to JSON")]
        private void ExportAdjacencyToJSON()
        {
            if (boardState == null)
            {
                UnityEngine.Debug.LogWarning("No board state to export");
                return;
            }

            var json = JsonUtility.ToJson(boardState.Adjacency, true);
            UnityEngine.Debug.Log($"Adjacency Graph:\n{json}");
        }
    }
}