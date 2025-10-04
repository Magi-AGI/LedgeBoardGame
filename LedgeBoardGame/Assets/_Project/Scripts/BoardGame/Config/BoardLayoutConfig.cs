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

        public SpaceMeta GetSpaceMeta(int spaceId)
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
                if (space.type == SpaceType.Ledge && !string.IsNullOrEmpty(space.colorLabel))
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

            spaces.Add(new SpaceDefinition
            {
                spaceId = 0,
                type = SpaceType.Center,
                ringIndex = 0,
                wedgeIndex = 0,
                isHalf = false,
                position = Vector2.zero
            });

            for (int i = 0; i < 6; i++)
            {
                spaces.Add(new SpaceDefinition
                {
                    spaceId = i + 1,
                    type = SpaceType.InnerBridge,
                    ringIndex = 1,
                    wedgeIndex = i * 2,
                    isHalf = true
                });
            }

            for (int i = 0; i < 6; i++)
            {
                spaces.Add(new SpaceDefinition
                {
                    spaceId = i + 7,
                    type = SpaceType.InnerStop,
                    ringIndex = 1,
                    wedgeIndex = i * 2 + 1,
                    isHalf = true
                });
            }

            for (int i = 0; i < 12; i++)
            {
                spaces.Add(new SpaceDefinition
                {
                    spaceId = i + 13,
                    type = SpaceType.Ring2,
                    ringIndex = 2,
                    wedgeIndex = i,
                    isHalf = false
                });
            }

            for (int i = 0; i < 18; i++)
            {
                spaces.Add(new SpaceDefinition
                {
                    spaceId = i + 25,
                    type = SpaceType.Ring3,
                    ringIndex = 3,
                    wedgeIndex = (i * 2) / 3,
                    isHalf = false
                });
            }

            for (int i = 0; i < 6; i++)
            {
                spaces.Add(new SpaceDefinition
                {
                    spaceId = i + 43,
                    type = SpaceType.OuterAdded,
                    ringIndex = 4,
                    wedgeIndex = i * 2,
                    isHalf = false
                });
            }

            for (int i = 0; i < 12; i++)
            {
                spaces.Add(new SpaceDefinition
                {
                    spaceId = 37 + i,
                    type = SpaceType.Ledge,
                    ringIndex = 4,
                    wedgeIndex = i,
                    isHalf = false,
                    colorLabel = ledgeColors[i % ledgeColors.Count]
                });
            }

            GenerateDefaultAdjacency();
        }

        private void GenerateDefaultAdjacency()
        {
            var centerAdj = new AdjacencyDefinition { spaceId = 0 };
            for (int i = 1; i <= 6; i++)
            {
                centerAdj.adjacentSpaces.Add(i);
            }
            adjacencyList.Add(centerAdj);

            for (int i = 1; i <= 6; i++)
            {
                var adj = new AdjacencyDefinition { spaceId = i };
                adj.adjacentSpaces.Add(0);
                adj.adjacentSpaces.Add(12 + i * 2);
                adj.adjacentSpaces.Add(12 + i * 2 + 1);
                adjacencyList.Add(adj);
            }

            for (int i = 7; i <= 12; i++)
            {
                var adj = new AdjacencyDefinition { spaceId = i };
                adj.adjacentSpaces.Add(12 + (i - 7) * 2 + 1);
                adj.adjacentSpaces.Add(12 + (i - 7) * 2 + 2);
                adjacencyList.Add(adj);
            }
        }
    }
}