using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Builder
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

            if (!string.IsNullOrEmpty(meta.ColorLabel))
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

        // Builds the 49-space Ledge rosette graph: 1 center + 12 inner (6 bridges + 6 walls) +
        // 12 ring2 + 18 ring3 (6 vertices + 12 offs) + 6 outer-added.
        //
        // Wedges are 30° sectors numbered clockwise from the top (90°):
        //   even wedges {0, 2, 4, 6, 8, 10} are the 6 "outer axes" at 90°, 30°, -30°, -90°, -150°, 150°
        //   odd  wedges {1, 3, 5, 7, 9, 11} are the 6 "vertex axes" at 60°, 0°, -60°, -120°, 180°, 120°
        //
        // SpaceId layout:
        //   0           Center
        //   1..6        InnerBridge, one per outer axis (wedge 2k)
        //   7..12       InnerWall,   one per vertex axis (wedge 2k+1)
        //   13..24      Ring2, one per wedge (13+w at wedge w)
        //   25..36      Ring3 "off" spaces, two per sector flanking each ring3-vertex
        //                 id = 25 + 2k + f, where k is the sector (0..5) and f is 0 (ccw flank) or 1 (cw flank)
        //   37..42      Ring3 vertex spaces on vertex axes (37+k at wedge 2k+1)
        //   43..48      OuterAdded on outer axes (43+k at wedge 2k)
        //
        // The 12 color-labeled "ledge" spaces are IDs 37..48 (6 ring3 vertices + 6 outer-added).
        // Every one of them sits exactly 4 edges from the center (5-space rule including endpoints).
        public static BoardGraphBuilder CreateHexagonalBoard()
        {
            var builder = new BoardGraphBuilder();
            var colors = LedgeConfigConstants.LedgeColors;

            builder.AddSpace(0, new SpaceMeta(SpaceType.Center, 0, 0));

            for (int k = 0; k < 6; k++)
            {
                builder.AddSpace(1 + k, new SpaceMeta(SpaceType.InnerBridge, 1, k * 2));
                builder.AddBidirectionalEdge(0, 1 + k);
            }

            for (int k = 0; k < 6; k++)
            {
                builder.AddSpace(7 + k, new SpaceMeta(SpaceType.InnerWall, 1, k * 2 + 1, true));
            }

            // Inner 12-cycle alternating bridge/wall, clockwise:
            // bridge@90° - wall@60° - bridge@30° - wall@0° - ... - wall@120° - back to bridge@90°
            int[] innerCycle = { 1, 7, 2, 8, 3, 9, 4, 10, 5, 11, 6, 12 };
            for (int i = 0; i < 12; i++)
            {
                builder.AddBidirectionalEdge(innerCycle[i], innerCycle[(i + 1) % 12]);
            }

            for (int w = 0; w < 12; w++)
            {
                builder.AddSpace(13 + w, new SpaceMeta(SpaceType.Ring2, 2, w));
                builder.AddBidirectionalEdge(13 + w, 13 + (w + 1) % 12);
            }

            // Inner ↔ Ring2 on the same axis. Bridges connect to outer-axis ring2 (even wedge),
            // walls connect to vertex-axis ring2 (odd wedge).
            for (int k = 0; k < 6; k++)
            {
                builder.AddBidirectionalEdge(1 + k, 13 + k * 2);
                builder.AddBidirectionalEdge(7 + k, 13 + k * 2 + 1);
            }

            // Ring3 offs: two per sector. For sector k (vertex axis at wedge 2k+1):
            //   25 + 2k     is the "ccw" off (toward outer axis 2k)
            //   25 + 2k + 1 is the "cw"  off (toward outer axis (2k+2) mod 12)
            for (int k = 0; k < 6; k++)
            {
                int ccwOff = 25 + k * 2;
                int cwOff = 25 + k * 2 + 1;
                int prevOuterWedge = k * 2;
                int nextOuterWedge = (k * 2 + 2) % 12;
                builder.AddSpace(ccwOff, new SpaceMeta(SpaceType.Ring3, 3, prevOuterWedge));
                builder.AddSpace(cwOff, new SpaceMeta(SpaceType.Ring3, 3, nextOuterWedge));
            }

            // Ring3 vertices carry odd-indexed colors; they share a vertex axis with a wall.
            for (int k = 0; k < 6; k++)
            {
                int wedge = k * 2 + 1;
                builder.AddSpace(37 + k, new SpaceMeta(SpaceType.Ring3, 3, wedge, false, colors[k * 2 + 1]));
            }

            // OuterAdded carry even-indexed colors; they share an outer axis with a bridge.
            for (int k = 0; k < 6; k++)
            {
                int wedge = k * 2;
                builder.AddSpace(43 + k, new SpaceMeta(SpaceType.OuterAdded, 4, wedge, false, colors[k * 2]));
            }

            // Ring2-outer (13 + 2k) flanked by the two ring3-offs bracketing its outer axis.
            // OuterAdded (43 + k) shares the same pair of flanking offs.
            // The "ccw-side" off for outer wedge 2k is the cw-off of the previous sector:
            //   ccwFlank = 25 + ((2k - 1 + 12) % 12)
            // The "cw-side" off is the ccw-off of the current sector:
            //   cwFlank  = 25 + 2k
            for (int k = 0; k < 6; k++)
            {
                int ring2Outer = 13 + k * 2;
                int outerAdded = 43 + k;
                int ccwFlank = 25 + (2 * k - 1 + 12) % 12;
                int cwFlank = 25 + 2 * k;

                builder.AddBidirectionalEdge(ring2Outer, ccwFlank);
                builder.AddBidirectionalEdge(ring2Outer, cwFlank);

                builder.AddBidirectionalEdge(outerAdded, ccwFlank);
                builder.AddBidirectionalEdge(outerAdded, cwFlank);
            }

            // Ring2-vertex (13 + 2k + 1) connects to the ring3-vertex on the same axis plus both offs flanking that vertex.
            for (int k = 0; k < 6; k++)
            {
                int ring2Vertex = 13 + k * 2 + 1;
                int ring3Vertex = 37 + k;
                int ccwOff = 25 + k * 2;
                int cwOff = 25 + k * 2 + 1;
                builder.AddBidirectionalEdge(ring2Vertex, ring3Vertex);
                builder.AddBidirectionalEdge(ring2Vertex, ccwOff);
                builder.AddBidirectionalEdge(ring2Vertex, cwOff);
            }

            // Ring3 18-cycle walking clockwise from the "79°" off:
            // 25 - 37 - 26 - 27 - 38 - 28 - 29 - 39 - 30 - 31 - 40 - 32 - 33 - 41 - 34 - 35 - 42 - 36 - 25
            int[] ring3Cycle = { 25, 37, 26, 27, 38, 28, 29, 39, 30, 31, 40, 32, 33, 41, 34, 35, 42, 36 };
            for (int i = 0; i < 18; i++)
            {
                builder.AddBidirectionalEdge(ring3Cycle[i], ring3Cycle[(i + 1) % 18]);
            }

            return builder;
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
