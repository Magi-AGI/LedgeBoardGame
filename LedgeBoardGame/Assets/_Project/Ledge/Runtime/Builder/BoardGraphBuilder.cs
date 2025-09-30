using System.Collections.Generic;
using Magi.LedgeBoardGame.Runtime.Models;

namespace Magi.LedgeBoardGame.Runtime.Builder
{
    public class BoardGraphBuilder
    {
        private readonly Dictionary<int, SpaceMeta> _spaceMetadata;
        private readonly Dictionary<int, List<int>> _adjacency;
        private readonly Dictionary<string, List<int>> _ledgesByColor;

        public BoardGraphBuilder()
        {
            _spaceMetadata = new Dictionary<int, SpaceMeta>();
            _adjacency = new Dictionary<int, List<int>>();
            _ledgesByColor = new Dictionary<string, List<int>>();
        }

        public void AddSpace(int spaceId, SpaceMeta meta)
        {
            _spaceMetadata[spaceId] = meta;
            if (!_adjacency.ContainsKey(spaceId))
            {
                _adjacency[spaceId] = new List<int>();
            }

            if (meta.Type == SpaceType.Ledge && !string.IsNullOrEmpty(meta.ColorLabel))
            {
                if (!_ledgesByColor.ContainsKey(meta.ColorLabel))
                {
                    _ledgesByColor[meta.ColorLabel] = new List<int>();
                }
                _ledgesByColor[meta.ColorLabel].Add(spaceId);
            }
        }

        public void AddEdge(int from, int to)
        {
            if (!_adjacency.ContainsKey(from))
            {
                _adjacency[from] = new List<int>();
            }
            if (!_adjacency[from].Contains(to))
            {
                _adjacency[from].Add(to);
            }
        }

        public void AddBidirectionalEdge(int space1, int space2)
        {
            AddEdge(space1, space2);
            AddEdge(space2, space1);
        }

        public BoardState BuildBoard(int boardId, int playerId)
        {
            var board = new BoardState(boardId, playerId);

            foreach (var kvp in _spaceMetadata)
            {
                board.SpaceMetadata[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in _adjacency)
            {
                board.Adjacency[kvp.Key] = new List<int>(kvp.Value);
            }

            foreach (var kvp in _ledgesByColor)
            {
                board.LedgeSpacesByColor[kvp.Key] = new List<int>(kvp.Value);
            }

            return board;
        }

        public static BoardGraphBuilder CreateHexagonalBoard()
        {
            var builder = new BoardGraphBuilder();

            builder.AddSpace(0, new SpaceMeta(SpaceType.Center, 0, 0));

            for (int i = 0; i < 6; i++)
            {
                builder.AddSpace(i + 1, new SpaceMeta(SpaceType.InnerBridge, 1, i * 2, true));
                builder.AddBidirectionalEdge(0, i + 1);
            }

            for (int i = 0; i < 6; i++)
            {
                builder.AddSpace(i + 7, new SpaceMeta(SpaceType.InnerStop, 1, i * 2 + 1, true));
            }

            for (int i = 0; i < 12; i++)
            {
                builder.AddSpace(i + 13, new SpaceMeta(SpaceType.Ring2, 2, i));

                if (i % 2 == 0)
                {
                    builder.AddBidirectionalEdge(i / 2 + 1, i + 13);
                }
                else
                {
                    builder.AddBidirectionalEdge((i - 1) / 2 + 7, i + 13);
                }
            }

            for (int i = 0; i < 18; i++)
            {
                builder.AddSpace(i + 25, new SpaceMeta(SpaceType.Ring3, 3, (i * 2) / 3));

                var ring2Index = 13 + (i * 2) / 3;
                builder.AddBidirectionalEdge(ring2Index, i + 25);
            }

            var ledgeColors = new[]
            {
                "Ela", "Biz", "Yun", "Jutu", "Glei", "Sace",
                "Rha", "Dau", "Wim", "Pfi", "Quae", "Vei"
            };

            for (int i = 0; i < 12; i++)
            {
                var ledgeId = 37 + i;
                builder.AddSpace(ledgeId, new SpaceMeta(SpaceType.Ledge, 4, i, false, ledgeColors[i]));

                var ring3Start = 25 + (i * 18) / 12;
                builder.AddBidirectionalEdge(ring3Start, ledgeId);
                if (i < 11)
                {
                    builder.AddBidirectionalEdge(ring3Start + 1, ledgeId);
                }
            }

            for (int i = 0; i < 6; i++)
            {
                builder.AddSpace(i + 43, new SpaceMeta(SpaceType.OuterAdded, 4, i * 2));

                var ring3Index = 25 + i * 3;
                builder.AddBidirectionalEdge(ring3Index, i + 43);
            }

            ConnectRings(builder);

            return builder;
        }

        private static void ConnectRings(BoardGraphBuilder builder)
        {
            for (int i = 0; i < 6; i++)
            {
                var next = (i + 1) % 6;
                builder.AddBidirectionalEdge(i + 1, next + 1);
            }

            for (int i = 0; i < 6; i++)
            {
                var next = (i + 1) % 6;
                builder.AddBidirectionalEdge(i + 7, next + 7);
            }

            for (int i = 0; i < 12; i++)
            {
                var next = (i + 1) % 12;
                builder.AddBidirectionalEdge(i + 13, next + 13);
            }

            for (int i = 0; i < 18; i++)
            {
                var next = (i + 1) % 18;
                builder.AddBidirectionalEdge(i + 25, next + 25);
            }
        }

        public Dictionary<int, SpaceMeta> GetSpaceMetadata()
        {
            return new Dictionary<int, SpaceMeta>(_spaceMetadata);
        }

        public Dictionary<int, List<int>> GetAdjacency()
        {
            var copy = new Dictionary<int, List<int>>();
            foreach (var kvp in _adjacency)
            {
                copy[kvp.Key] = new List<int>(kvp.Value);
            }
            return copy;
        }

        public Dictionary<string, List<int>> GetLedgesByColor()
        {
            var copy = new Dictionary<string, List<int>>();
            foreach (var kvp in _ledgesByColor)
            {
                copy[kvp.Key] = new List<int>(kvp.Value);
            }
            return copy;
        }
    }
}