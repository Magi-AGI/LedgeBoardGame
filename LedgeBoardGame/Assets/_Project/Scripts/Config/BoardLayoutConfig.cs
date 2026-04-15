using UnityEngine;
using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Config
{
    [CreateAssetMenu(fileName = "BoardLayoutConfig", menuName = "Ledge/Board Layout Config")]
    public class BoardLayoutConfig : ScriptableObject
    {
        [System.Serializable]
        public class SpaceDefinition
        {
            public int spaceId;
            public SpaceType type;
            public int ringIndex;
            public int wedgeIndex;
            public bool isHalf;
            public string colorLabel;
            public Vector2 position;
        }

        [System.Serializable]
        public class AdjacencyDefinition
        {
            public int spaceId;
            public List<int> adjacentSpaces = new List<int>();
        }

        [Header("Board Configuration")]
        [SerializeField] private int totalSpaces = 49;
        [SerializeField] private int wedgeCount = 12;

        [Header("Space Definitions")]
        [SerializeField] private List<SpaceDefinition> spaces = new List<SpaceDefinition>();

        [Header("Adjacency")]
        [SerializeField] private List<AdjacencyDefinition> adjacencyList = new List<AdjacencyDefinition>();

        [Header("Ledge Colors")]
        [SerializeField] private List<string> ledgeColors = new List<string>
        {
            "Ela", "Biz", "Yun", "Jutu", "Glei", "Sace",
            "Rha", "Dau", "Wim", "Pfi", "Quae", "Vei"
        };

        public int TotalSpaces => totalSpaces;
        public int WedgeCount => wedgeCount;
        public List<SpaceDefinition> Spaces => spaces;
        public List<AdjacencyDefinition> AdjacencyList => adjacencyList;
        public List<string> LedgeColors => ledgeColors;

        public SpaceMeta? GetSpaceMeta(int spaceId)
        {
            var def = spaces.Find(s => s.spaceId == spaceId);
            if (def == null) return null;

            return new SpaceMeta(
                def.type,
                def.ringIndex,
                def.wedgeIndex,
                def.isHalf,
                def.colorLabel
            );
        }

        public Dictionary<int, SpaceMeta> GetAllSpaceMetadata()
        {
            var metadata = new Dictionary<int, SpaceMeta>();
            foreach (var space in spaces)
            {
                metadata[space.spaceId] = new SpaceMeta(
                    space.type,
                    space.ringIndex,
                    space.wedgeIndex,
                    space.isHalf,
                    space.colorLabel
                );
            }
            return metadata;
        }

        public Dictionary<int, List<int>> GetAdjacencyMap()
        {
            var adjacency = new Dictionary<int, List<int>>();
            foreach (var adj in adjacencyList)
            {
                adjacency[adj.spaceId] = new List<int>(adj.adjacentSpaces);
            }
            return adjacency;
        }

        public Dictionary<string, List<int>> GetLedgeSpacesByColor()
        {
            var ledgesByColor = new Dictionary<string, List<int>>();
            foreach (var color in ledgeColors)
            {
                ledgesByColor[color] = new List<int>();
            }

            foreach (var space in spaces)
            {
                if (!string.IsNullOrEmpty(space.colorLabel))
                {
                    if (ledgesByColor.ContainsKey(space.colorLabel))
                    {
                        ledgesByColor[space.colorLabel].Add(space.spaceId);
                    }
                }
            }

            return ledgesByColor;
        }

        [ContextMenu("Generate Default Layout")]
        public void GenerateDefaultLayout()
        {
            spaces.Clear();
            adjacencyList.Clear();
            // Build directly from the authoritative graph so ScriptableObject and code stay in sync.
            var builder = Magi.LedgeBoardGame.Builder.BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            totalSpaces = board.SpaceMetadata.Count;
            ledgeColors = new List<string>(Magi.LedgeBoardGame.Models.LedgeConfigConstants.LedgeColors);

            foreach (var kvp in board.SpaceMetadata)
            {
                var meta = kvp.Value;
                spaces.Add(new SpaceDefinition
                {
                    spaceId = kvp.Key,
                    type = meta.Type,
                    ringIndex = meta.RingIndex,
                    wedgeIndex = meta.WedgeIndex,
                    isHalf = meta.IsHalf,
                    colorLabel = meta.ColorLabel
                });
            }

            foreach (var kvp in board.Adjacency)
            {
                adjacencyList.Add(new AdjacencyDefinition
                {
                    spaceId = kvp.Key,
                    adjacentSpaces = new List<int>(kvp.Value)
                });
            }
        }

        private void GenerateDefaultAdjacency() { }
    }
}
