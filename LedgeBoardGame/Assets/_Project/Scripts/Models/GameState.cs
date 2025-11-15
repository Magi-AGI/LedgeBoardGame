using System;
using System.Collections.Generic;
using System.Linq;
using Magi.LedgeBoardGame.Builder;

namespace Magi.LedgeBoardGame.Models
{
    [Serializable]
    public class GameState
    {
        public List<Player> Players { get; set; }
        public List<BoardState> Boards { get; set; }
        public int CurrentPlayerId { get; set; }
        public GamePhase CurrentPhase { get; set; }
        public int TurnNumber { get; set; }
        public List<Move> CurrentTurnMoves { get; set; }
        public List<PlacementMove> CurrentTurnPlacements { get; set; }
        public Dictionary<string, List<CrossBoardEdge>> CrossBoardLedgeEdges { get; set; }
        public int? WinnerId { get; set; }
        public bool GameOver { get; set; }

        private bool _hasPlacedLight;
        private bool _hasPlacedDark;

        public GameState()
        {
            Players = new List<Player>();
            Boards = new List<BoardState>();
            CurrentTurnMoves = new List<Move>();
            CurrentTurnPlacements = new List<PlacementMove>();
            CrossBoardLedgeEdges = new Dictionary<string, List<CrossBoardEdge>>();
            CurrentPhase = GamePhase.Placement;
            TurnNumber = 1;
            GameOver = false;
        }

        public GameState(List<Player> players) : this()
        {
            Players = players;
            InitializeBoards();
            CurrentPlayerId = players.First().Id;
        }

        private void InitializeBoards()
        {
            var builder = BoardGraphBuilder.CreateHexagonalBoard();

            foreach (var player in Players)
            {
                var board = builder.BuildBoard(player.BoardId, player.Id);
                Boards.Add(board);
            }

            GenerateCrossBoardLedgeEdges();
        }

        private void GenerateCrossBoardLedgeEdges()
        {
            var ledgeColors = new[] { "Ela", "Biz", "Yun", "Jutu", "Glei", "Sace",
                                      "Rha", "Dau", "Wim", "Pfi", "Quae", "Vei" };

            foreach (var color in ledgeColors)
            {
                CrossBoardLedgeEdges[color] = new List<CrossBoardEdge>();

                foreach (var sourceBoard in Boards)
                {
                    var sourceLedges = sourceBoard.GetLedgeSpacesWithColor(color);

                    foreach (var targetBoard in Boards)
                    {
                        if (sourceBoard.BoardId == targetBoard.BoardId) continue;

                        var targetLedges = targetBoard.GetLedgeSpacesWithColor(color);

                        foreach (var sourceSpace in sourceLedges)
                        {
                            foreach (var targetSpace in targetLedges)
                            {
                                CrossBoardLedgeEdges[color].Add(new CrossBoardEdge
                                {
                                    From = new SpaceId(sourceBoard.BoardId, sourceSpace),
                                    To = new SpaceId(targetBoard.BoardId, targetSpace),
                                    Color = color
                                });
                            }
                        }
                    }
                }
            }
        }

        public Player GetCurrentPlayer()
        {
            return Players.FirstOrDefault(p => p.Id == CurrentPlayerId);
        }

        public BoardState GetBoard(int boardId)
        {
            return Boards.FirstOrDefault(b => b.BoardId == boardId);
        }

        public BoardState GetBoardForPlayer(int playerId)
        {
            return Boards.FirstOrDefault(b => b.PlayerId == playerId);
        }

        public BoardState GetBoardForSpace(SpaceId spaceId)
        {
            return GetBoard(spaceId.BoardId);
        }

        public bool IsPlacementComplete()
        {
            return _hasPlacedLight && _hasPlacedDark;
        }

        public void RecordPlacement(Tone tone)
        {
            if (tone == Tone.Light)
                _hasPlacedLight = true;
            else
                _hasPlacedDark = true;
        }

        public void StartMovementPhase()
        {
            CurrentPhase = GamePhase.Movement;
        }

        public void EndTurn()
        {
            CheckEliminations();

            if (!GameOver)
            {
                CurrentTurnMoves.Clear();
                CurrentTurnPlacements.Clear();
                _hasPlacedLight = false;
                _hasPlacedDark = false;
                CurrentPhase = GamePhase.Placement;

                AdvanceToNextPlayer();
                TurnNumber++;
            }
        }

        private void AdvanceToNextPlayer()
        {
            var activePlayers = Players.Where(p => !p.IsEliminated).OrderBy(p => p.Id).ToList();
            if (activePlayers.Count == 0) return;

            var currentIndex = activePlayers.FindIndex(p => p.Id == CurrentPlayerId);
            var nextIndex = (currentIndex + 1) % activePlayers.Count;
            CurrentPlayerId = activePlayers[nextIndex].Id;
        }

        private void CheckEliminations()
        {
            foreach (var board in Boards)
            {
                var player = Players.FirstOrDefault(p => p.BoardId == board.BoardId);
                if (player != null && !player.IsEliminated && board.IsEliminated())
                {
                    player.IsEliminated = true;
                }
            }

            var activePlayers = Players.Where(p => !p.IsEliminated).ToList();
            if (activePlayers.Count == 1)
            {
                GameOver = true;
                WinnerId = activePlayers.First().Id;
            }
            else if (activePlayers.Count == 0)
            {
                GameOver = true;
            }
        }

        public List<SpaceId> GetCrossBoardTargets(SpaceId fromSpace)
        {
            var targets = new List<SpaceId>();
            var board = GetBoard(fromSpace.BoardId);

            if (board == null) return targets;

            var color = board.GetLedgeColor(fromSpace.Id);
            if (string.IsNullOrEmpty(color)) return targets;

            if (CrossBoardLedgeEdges.TryGetValue(color, out var edges))
            {
                targets.AddRange(edges
                    .Where(e => e.From.Equals(fromSpace))
                    .Select(e => e.To));
            }

            return targets;
        }

        public bool CanCrossBoard(SpaceId fromSpace)
        {
            var board = GetBoard(fromSpace.BoardId);
            if (board == null) return false;

            if (!board.IsLedgeSpace(fromSpace.Id)) return false;

            var stack = board.GetStack(fromSpace.Id);
            return stack.LightCount >= 2 || stack.DarkCount >= 2;
        }

        public GameState Clone()
        {
            var clone = new GameState
            {
                Players = Players.Select(p => new Player
                {
                    Id = p.Id,
                    Name = p.Name,
                    BoardId = p.BoardId,
                    IsEliminated = p.IsEliminated,
                    IsHuman = p.IsHuman
                }).ToList(),
                Boards = Boards.Select(b => b.Clone()).ToList(),
                CurrentPlayerId = CurrentPlayerId,
                CurrentPhase = CurrentPhase,
                TurnNumber = TurnNumber,
                CurrentTurnMoves = new List<Move>(CurrentTurnMoves),
                CurrentTurnPlacements = new List<PlacementMove>(CurrentTurnPlacements),
                CrossBoardLedgeEdges = new Dictionary<string, List<CrossBoardEdge>>(CrossBoardLedgeEdges),
                WinnerId = WinnerId,
                GameOver = GameOver,
                _hasPlacedLight = _hasPlacedLight,
                _hasPlacedDark = _hasPlacedDark
            };

            return clone;
        }
    }

    [Serializable]
    public class CrossBoardEdge
    {
        public SpaceId From { get; set; }
        public SpaceId To { get; set; }
        public string Color { get; set; }
    }
}
