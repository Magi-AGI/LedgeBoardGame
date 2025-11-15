using System;
using System.Collections.Generic;
using System.Linq;

namespace Magi.LedgeBoardGame.Models
{
    [Serializable]
    public class BoardState
    {
        public int BoardId { get; set; }
        public int PlayerId { get; set; }
        public Dictionary<int, TokenStack> Spaces { get; set; }
        public Dictionary<int, SpaceMeta> SpaceMetadata { get; set; }
        public Dictionary<int, List<int>> Adjacency { get; set; }
        public Dictionary<string, List<int>> LedgeSpacesByColor { get; set; }

        private const int TOTAL_SPACES = 49;
        private const int CENTER_SPACE_ID = 0;

        public BoardState()
        {
            Spaces = new Dictionary<int, TokenStack>();
            SpaceMetadata = new Dictionary<int, SpaceMeta>();
            Adjacency = new Dictionary<int, List<int>>();
            LedgeSpacesByColor = new Dictionary<string, List<int>>();
        }

        public BoardState(int boardId, int playerId) : this()
        {
            BoardId = boardId;
            PlayerId = playerId;
            InitializeBoard();
        }

        private void InitializeBoard()
        {
            for (int i = 0; i < TOTAL_SPACES; i++)
            {
                Spaces[i] = new TokenStack();
            }

            var centerStack = new TokenStack(1, 0, Tone.Light);
            Spaces[CENTER_SPACE_ID] = centerStack;
        }

        public SpaceId GetSpaceId(int localSpaceId)
        {
            return new SpaceId(BoardId, localSpaceId);
        }

        public TokenStack GetStack(int localSpaceId)
        {
            return Spaces.TryGetValue(localSpaceId, out var stack) ? stack : new TokenStack();
        }

        public void SetStack(int localSpaceId, TokenStack stack)
        {
            Spaces[localSpaceId] = stack;
        }

        public List<int> GetAdjacentSpaces(int localSpaceId)
        {
            return Adjacency.TryGetValue(localSpaceId, out var adjacent) ? adjacent : new List<int>();
        }

        public bool IsLedgeSpace(int localSpaceId)
        {
            if (!SpaceMetadata.TryGetValue(localSpaceId, out var meta))
                return false;
            return meta.Type == SpaceType.Ledge;
        }

        public string GetLedgeColor(int localSpaceId)
        {
            if (!SpaceMetadata.TryGetValue(localSpaceId, out var meta))
                return null;
            return meta.Type == SpaceType.Ledge ? meta.ColorLabel : null;
        }

        public List<int> GetLedgeSpacesWithColor(string color)
        {
            return LedgeSpacesByColor.TryGetValue(color, out var spaces) ? spaces : new List<int>();
        }

        public bool IsCenterSpace(int localSpaceId)
        {
            return localSpaceId == CENTER_SPACE_ID;
        }

        public List<int> GetMovableSpaces(Tone tone)
        {
            var movable = new List<int>();
            foreach (var kvp in Spaces)
            {
                if (kvp.Value.CanMove(tone))
                {
                    movable.Add(kvp.Key);
                }
            }
            return movable;
        }

        public List<int> GetValidPlacementTargets()
        {
            return Spaces.Keys.ToList();
        }

        public bool IsEliminated()
        {
            var centerStack = GetStack(CENTER_SPACE_ID);
            return centerStack.DarkCount > 0 && centerStack.BottomTone == Tone.Dark;
        }

        public BoardState Clone()
        {
            var clone = new BoardState
            {
                BoardId = BoardId,
                PlayerId = PlayerId,
                Spaces = new Dictionary<int, TokenStack>(),
                SpaceMetadata = new Dictionary<int, SpaceMeta>(SpaceMetadata),
                Adjacency = new Dictionary<int, List<int>>(),
                LedgeSpacesByColor = new Dictionary<string, List<int>>()
            };

            foreach (var kvp in Spaces)
            {
                clone.Spaces[kvp.Key] = kvp.Value.Clone();
            }

            foreach (var kvp in Adjacency)
            {
                clone.Adjacency[kvp.Key] = new List<int>(kvp.Value);
            }

            foreach (var kvp in LedgeSpacesByColor)
            {
                clone.LedgeSpacesByColor[kvp.Key] = new List<int>(kvp.Value);
            }

            return clone;
        }
    }
}
