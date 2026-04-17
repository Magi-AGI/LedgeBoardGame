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

        public StateBasedEffectsResult EndTurn()
        {
            int endingPlayerId = CurrentPlayerId;
            // Overflow cap runs before elimination check so any exposed lock from
            // trimming is seen in the same pass. In single-tone stacks the trim
            // can't expose a new tone, but keeping the order explicit means a
            // future rule change (e.g., mixed stacks) stays consistent.
            var result = ApplyOverflowCap(endingPlayerId);
            var sbe = ApplyStateBasedEffects();
            result.NewlyEliminatedPlayerIds.AddRange(sbe.NewlyEliminatedPlayerIds);
            result.GameEnded = sbe.GameEnded;
            result.WinnerId = sbe.WinnerId;

            if (!GameOver)
            {
                CurrentTurnMoves.Clear();
                CurrentTurnPlacements.Clear();
                CurrentPhase = GamePhase.Placement;

                AdvanceToNextPlayer();
                TurnNumber++;
            }
            return result;
        }

        /// End-of-turn stack-size cap: any stack on the ending player's own board
        /// with more than StackCap counters has the excess cleared (removed from the
        /// top, so the bottom lock — if any — is preserved). Only the ending
        /// player's board is affected; cross-board counters on enemy boards are
        /// their owners' responsibility at their own end-of-turn.
        private StateBasedEffectsResult ApplyOverflowCap(int endingPlayerId)
        {
            const int StackCap = 3;
            var result = new StateBasedEffectsResult();
            var board = Boards.FirstOrDefault(b => b.PlayerId == endingPlayerId);
            if (board == null) return result;

            foreach (var kvp in board.Spaces)
            {
                var stack = kvp.Value;
                int excess = stack.TotalCount - StackCap;
                if (excess <= 0) continue;

                // Stacks are mono-tone under the clash rule, so exactly one of
                // Light/Dark is nonzero when total > 0. Pick whichever has counters.
                var tone = stack.LightCount > 0 ? Tone.Light : Tone.Dark;
                for (int i = 0; i < excess; i++) stack.RemoveOne(tone);

                result.OverflowTrims.Add(new OverflowTrim
                {
                    Space = new SpaceId(board.BoardId, kvp.Key),
                    Tone = tone,
                    RemovedCount = excess
                });
            }
            return result;
        }

        private void AdvanceToNextPlayer()
        {
            var activePlayers = Players.Where(p => !p.IsEliminated).OrderBy(p => p.Id).ToList();
            if (activePlayers.Count == 0) return;

            var currentIndex = activePlayers.FindIndex(p => p.Id == CurrentPlayerId);
            var nextIndex = (currentIndex + 1) % activePlayers.Count;
            CurrentPlayerId = activePlayers[nextIndex].Id;
        }

        /// Runs the state-based effects pass: any player whose center carries a
        /// Dark lock is eliminated (regardless of whose turn it is or who placed
        /// the counter), and the game ends when only one — or zero — active players
        /// remain. Returns a report of what changed so callers can narrate the
        /// outcome. Callers should skip this during Placement phase.
        public StateBasedEffectsResult ApplyStateBasedEffects()
        {
            var result = new StateBasedEffectsResult();
            bool wasGameOver = GameOver;

            foreach (var board in Boards)
            {
                var player = Players.FirstOrDefault(p => p.BoardId == board.BoardId);
                if (player != null && !player.IsEliminated && board.IsEliminated())
                {
                    player.IsEliminated = true;
                    result.NewlyEliminatedPlayerIds.Add(player.Id);
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

            result.GameEnded = GameOver && !wasGameOver;
            result.WinnerId = WinnerId;
            return result;
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

        public void CopyFrom(GameState other)
        {
            CurrentPlayerId = other.CurrentPlayerId;
            CurrentPhase = other.CurrentPhase;
            TurnNumber = other.TurnNumber;
            GameOver = other.GameOver;
            WinnerId = other.WinnerId;

            CurrentTurnMoves.Clear();
            CurrentTurnMoves.AddRange(other.CurrentTurnMoves);
            CurrentTurnPlacements.Clear();
            CurrentTurnPlacements.AddRange(other.CurrentTurnPlacements);

            for (int i = 0; i < Players.Count && i < other.Players.Count; i++)
            {
                Players[i].IsEliminated = other.Players[i].IsEliminated;
            }

            for (int i = 0; i < Boards.Count && i < other.Boards.Count; i++)
            {
                Boards[i].CopyFrom(other.Boards[i]);
            }
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

            var turnMoves = CurrentTurnMoves
                .Select(m => new Move(m.From, m.To, m.Tone, m.Count) { Result = m.Result })
                .ToList();
            var turnPlacements = CurrentTurnPlacements
                .Select(p => new PlacementMove(p.Target, p.Tone, p.Count) { Result = p.Result })
                .ToList();

            return new Spec.SpecGameState
            {
                Players = specPlayers,
                Ctx = ctx,
                Data = data,
                CurrentTurnMoves = turnMoves,
                CurrentTurnPlacements = turnPlacements
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

            if (specState.CurrentTurnMoves != null)
            {
                foreach (var m in specState.CurrentTurnMoves)
                {
                    gameState.CurrentTurnMoves.Add(
                        new Move(m.From, m.To, m.Tone, m.Count) { Result = m.Result });
                }
            }

            if (specState.CurrentTurnPlacements != null)
            {
                foreach (var p in specState.CurrentTurnPlacements)
                {
                    gameState.CurrentTurnPlacements.Add(
                        new PlacementMove(p.Target, p.Tone, p.Count) { Result = p.Result });
                }
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
