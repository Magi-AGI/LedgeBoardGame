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

        public GameState(List<Player> players) : this(players, null)
        {
        }

        public GameState(List<Player> players, Spec.LedgeRuntimeConfig runtimeConfig) : this()
        {
            if (runtimeConfig != null)
            {
                var count = players?.Count ?? 0;
                if (count < runtimeConfig.MinPlayers || count > runtimeConfig.MaxPlayers)
                {
                    throw new System.ArgumentException(
                        $"Player count {count} is outside allowed range [{runtimeConfig.MinPlayers}, {runtimeConfig.MaxPlayers}] defined in spec.");
                }
            }

            Players = players ?? new List<Player>();
            InitializeBoards();
            if (Players.Count > 0)
            {
                CurrentPlayerId = Players.First().Id;
            }
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
            foreach (var color in LedgeConfigConstants.LedgeColors)
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
            return HasPlacedLight && HasPlacedDark;
        }

        public bool HasPlacedLight => CurrentTurnPlacements.Any(p => p.Tone == Tone.Light);
        public bool HasPlacedDark => CurrentTurnPlacements.Any(p => p.Tone == Tone.Dark);

        public void RecordPlacement(Tone tone)
        {
            // CurrentTurnPlacements already record the tone; nothing else needed.
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
                GameOver = GameOver
            };

            return clone;
        }

        public Spec.SpecGameState ToSpecState()
        {
            var specPlayers = Players.Select(p => new Spec.SpecPlayer
            {
                Id = p.Id.ToString(),
                Name = p.Name,
                BoardId = p.BoardId,
                IsEliminated = p.IsEliminated
            }).ToList();

            var ctx = new Spec.SpecCtx
            {
                CurrentPlayer = CurrentPlayerId.ToString(),
                Phase = CurrentPhase.ToString().ToLowerInvariant(),
                TurnNumber = TurnNumber
            };

            var data = new Spec.SpecLedgeData
            {
                Boards = Boards.Select(b => b.Clone()).ToList(),
                WinnerId = WinnerId,
                GameOver = GameOver
            };

            return new Spec.SpecGameState
            {
                Players = specPlayers,
                Ctx = ctx,
                Data = data
            };
        }

        public static GameState FromSpecState(Spec.SpecGameState specState)
        {
            if (specState == null)
                return null;

            var players = specState.Players.Select(p => new Player
            {
                Id = int.Parse(p.Id),
                Name = p.Name,
                BoardId = p.BoardId,
                IsEliminated = p.IsEliminated
            }).ToList();

            GameState gameState;

            // If the spec has full board data, clone it directly; otherwise, construct
            // a fresh GameState from players so boards/metadata are initialized correctly.
            if (specState.Data != null && specState.Data.Boards != null && specState.Data.Boards.Count > 0)
            {
                gameState = new GameState
                {
                    Players = players,
                    Boards = specState.Data.Boards.Select(b => b.Clone()).ToList(),
                    CurrentTurnMoves = new List<Move>(),
                    CurrentTurnPlacements = new List<PlacementMove>(),
                    CrossBoardLedgeEdges = new Dictionary<string, List<CrossBoardEdge>>()
                };
            }
            else
            {
                gameState = new GameState(players, null);
            }

            gameState.CurrentPlayerId = specState.Ctx != null && int.TryParse(specState.Ctx.CurrentPlayer, out var currentId)
                ? currentId
                : (players.FirstOrDefault()?.Id ?? 0);

            gameState.CurrentPhase = specState.Ctx != null && !string.IsNullOrEmpty(specState.Ctx.Phase)
                ? (GamePhase)System.Enum.Parse(typeof(GamePhase), specState.Ctx.Phase, true)
                : GamePhase.Placement;

            gameState.TurnNumber = specState.Ctx?.TurnNumber ?? 1;
            gameState.WinnerId = specState.Data?.WinnerId;
            gameState.GameOver = specState.Data?.GameOver ?? false;

            gameState.GenerateCrossBoardLedgeEdges();

            return gameState;
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
