using System.Collections.Generic;
using System.Linq;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Spec;

namespace Magi.LedgeBoardGame.Rules
{
    public class GameRules : IGameRules
    {
        private readonly LedgeRuntimeConfig _config;

        public GameRules() : this(null)
        {
        }

        public GameRules(LedgeRuntimeConfig config)
        {
            _config = config;
        }

        public bool CanPlaceToken(GameState gameState, SpaceId target, Tone tone)
        {
            if (gameState.GameOver) return false;
            if (gameState.CurrentPhase != GamePhase.Placement) return false;

            var currentPlayer = gameState.GetCurrentPlayer();
            if (currentPlayer == null || currentPlayer.IsEliminated) return false;

            var playerBoard = gameState.GetBoardForPlayer(currentPlayer.Id);
            if (playerBoard == null) return false;

            if (target.BoardId != playerBoard.BoardId) return false;

            if (tone == Tone.Light && gameState.CurrentTurnPlacements.Any(p => p.Tone == Tone.Light))
                return false;
            if (tone == Tone.Dark && gameState.CurrentTurnPlacements.Any(p => p.Tone == Tone.Dark))
                return false;

            if (_config != null)
            {
                var placementsThisTurn = gameState.CurrentTurnPlacements.Count;
                if (placementsThisTurn >= _config.PlacementMaxMoves)
                {
                    return false;
                }
            }

            return true;
        }

        public PlacementMove PlaceToken(GameState gameState, SpaceId target, Tone tone)
        {
            if (!CanPlaceToken(gameState, target, tone))
                return null;

            var board = gameState.GetBoard(target.BoardId);
            if (board == null)
                return null;

            var stack = board.GetStack(target.Id);
            var result = stack.ResolveEntry(tone, 1);

            var move = new PlacementMove(target, tone, 1)
            {
                Result = result
            };

            gameState.CurrentTurnPlacements.Add(move);
            gameState.RecordPlacement(tone);

            if (gameState.IsPlacementComplete())
            {
                gameState.StartMovementPhase();
            }

            return move;
        }

        public bool CanMoveToken(GameState gameState, SpaceId from, SpaceId to, Tone tone)
        {
            if (gameState.GameOver) return false;
            if (gameState.CurrentPhase != GamePhase.Movement) return false;

            if (_config != null)
            {
                var movesThisTurn = gameState.CurrentTurnMoves.Count;
                if (movesThisTurn >= _config.MovementMaxMoves)
                {
                    return false;
                }
            }

            var currentPlayer = gameState.GetCurrentPlayer();
            if (currentPlayer == null || currentPlayer.IsEliminated) return false;

            var fromBoard = gameState.GetBoard(from.BoardId);
            var toBoard = gameState.GetBoard(to.BoardId);
            if (fromBoard == null || toBoard == null) return false;

            var fromStack = fromBoard.GetStack(from.Id);
            if (!fromStack.CanMove(tone)) return false;

            if (!IsValidMove(gameState, from, to))
                return false;

            bool playerControlsSource = IsSpaceControlled(gameState, from, currentPlayer.Id);
            if (!playerControlsSource) return false;

            return true;
        }

        public Move MoveToken(GameState gameState, SpaceId from, SpaceId to, Tone tone)
        {
            if (!CanMoveToken(gameState, from, to, tone))
                return null;

            var fromBoard = gameState.GetBoard(from.BoardId);
            var toBoard = gameState.GetBoard(to.BoardId);
            if (fromBoard == null || toBoard == null)
                return null;

            var fromStack = fromBoard.GetStack(from.Id);
            var toStack = toBoard.GetStack(to.Id);

            fromStack.RemoveOne(tone);

            var result = toStack.ResolveEntry(tone, 1);

            var move = new Move(from, to, tone, 1)
            {
                Result = result
            };

            gameState.CurrentTurnMoves.Add(move);

            return move;
        }

        public List<SpaceId> GetValidMoveTargets(GameState gameState, SpaceId from, Tone tone)
        {
            var targets = new List<SpaceId>();

            if (gameState.CurrentPhase != GamePhase.Movement)
                return targets;

            var fromBoard = gameState.GetBoard(from.BoardId);
            if (fromBoard == null) return targets;

            var fromStack = fromBoard.GetStack(from.Id);
            if (!fromStack.CanMove(tone)) return targets;

            var adjacentSpaces = fromBoard.GetAdjacentSpaces(from.Id);
            foreach (var spaceId in adjacentSpaces)
            {
                targets.Add(new SpaceId(from.BoardId, spaceId));
            }

            if (fromBoard.IsLedgeSpace(from.Id) && gameState.CanCrossBoard(from))
            {
                var crossBoardTargets = gameState.GetCrossBoardTargets(from);
                targets.AddRange(crossBoardTargets);
            }

            return targets;
        }

        // Hop budget is modeled as a single counter: initial = picked-up count; each hop
        // consumes 1; traversing a same-tone stack adds that count back (the player picks
        // those counters up, extending how much further the stack can travel from that
        // point). Opposite-tone intermediates don't reduce the budget directly — they cost
        // carried counters at execution time via ResolveEntry clashes, tracked separately
        // as a tiebreak heuristic so the UI prefers routes that survive with more counters.
        // A hard cap on total exploration depth prevents pathological long chains if the
        // board has many same-tone stacks in a line.
        private const int MaxReachExplorationDepth = 64;

        /// Reachable-space BFS with pickup-extension. Starts from `from` with budget =
        /// maxSteps and expands neighbors as long as the remaining budget allows at least
        /// one more hop. Each same-tone intermediate extends the budget by its counter
        /// count, so a 2-stack traveling through a 2-same-tone space can actually reach
        /// 4 hops out. Returns every reached space keyed by its minimum hop-distance —
        /// the UI uses this to light destinations with distance-based intensity.
        public Dictionary<SpaceId, int> GetReachableTargets(GameState gameState, SpaceId from, Tone tone, int maxSteps)
        {
            var distances = new Dictionary<SpaceId, int>();
            if (maxSteps <= 0) return distances;
            if (gameState.CurrentPhase != GamePhase.Movement) return distances;

            var fromBoard = gameState.GetBoard(from.BoardId);
            if (fromBoard == null) return distances;
            if (!fromBoard.GetStack(from.Id).CanMove(tone)) return distances;

            // Level-synchronous BFS, keeping the max budget seen per space at each level
            // so reach extensions from same-tone pickups propagate forward. Spaces that
            // enter with 0 remaining budget are still "reachable" (can stop here) but
            // can't expand further — mirroring the "stop when you run out of energy" rule.
            var frontier = new List<(SpaceId space, int budget)> { (from, maxSteps) };
            int hops = 0;

            while (frontier.Count > 0 && hops < MaxReachExplorationDepth)
            {
                var nextLevel = new Dictionary<SpaceId, int>();
                foreach (var (u, budget) in frontier)
                {
                    foreach (var v in EnumerateSingleStepNeighbors(gameState, u, tone, u.Equals(from)))
                    {
                        int sameToneAtV = GetSameToneCount(gameState, v, tone);
                        int newBudget = budget - 1 + sameToneAtV;
                        if (newBudget < 0) continue;

                        // Only claim `v` as reached at this level if no shorter route already did.
                        if (distances.ContainsKey(v) && distances[v] < hops + 1) continue;

                        if (!distances.ContainsKey(v))
                            distances[v] = hops + 1;

                        if (!nextLevel.TryGetValue(v, out var existingBudget) || existingBudget < newBudget)
                            nextLevel[v] = newBudget;
                    }
                }

                frontier = new List<(SpaceId, int)>();
                foreach (var kvp in nextLevel)
                {
                    if (kvp.Value > 0) frontier.Add((kvp.Key, kvp.Value));
                }
                hops++;
            }

            return distances;
        }

        /// Lexicographic path search with pickup-extension. Keys are compared in order:
        ///   (1) hop-distance — shortest wins,
        ///   (2) clashes — fewest opposite-tone intermediates wins (player keeps more counters),
        ///   (3) pickups — most same-tone intermediates wins (siphoning bias and reach extension).
        /// Keeping clashes and pickups as separate tiers means a clean empty path will beat a
        /// pickup-rich but casualty-heavy path that nets the same count — matching the player's
        /// intent 99% of the time — while still preferring a pickup route when safety ties.
        /// Returns the path excluding `from` but including `to`; empty if unreachable.
        public List<SpaceId> FindShortestPath(GameState gameState, SpaceId from, SpaceId to, Tone tone, int maxSteps)
        {
            var path = new List<SpaceId>();
            if (maxSteps <= 0 || from.Equals(to)) return path;
            if (gameState.CurrentPhase != GamePhase.Movement) return path;

            var fromBoard = gameState.GetBoard(from.BoardId);
            if (fromBoard == null) return path;
            if (!fromBoard.GetStack(from.Id).CanMove(tone)) return path;

            var bestHops = new Dictionary<SpaceId, int> { { from, 0 } };
            var bestClashes = new Dictionary<SpaceId, int> { { from, 0 } };
            var bestPickups = new Dictionary<SpaceId, int> { { from, 0 } };
            var bestBudget = new Dictionary<SpaceId, int> { { from, maxSteps } };
            var parents = new Dictionary<SpaceId, SpaceId>();

            var frontier = new List<SpaceId> { from };
            int hops = 0;

            while (frontier.Count > 0 && hops < MaxReachExplorationDepth)
            {
                var nextLevelBudget = new Dictionary<SpaceId, int>();
                var discoveredAtLevel = new HashSet<SpaceId>();

                foreach (var u in frontier)
                {
                    int uBudget = bestBudget[u];
                    int uClashes = bestClashes[u];
                    int uPickups = bestPickups[u];
                    foreach (var v in EnumerateSingleStepNeighbors(gameState, u, tone, u.Equals(from)))
                    {
                        int sameToneAtV = GetSameToneCount(gameState, v, tone);
                        int oppositeAtV = GetOppositeToneCount(gameState, v, tone);
                        int newBudget = uBudget - 1 + sameToneAtV;
                        if (newBudget < 0) continue;

                        int newHops = hops + 1;
                        int newClashes = uClashes + oppositeAtV;
                        int newPickups = uPickups + sameToneAtV;

                        // Longer routes can't tiebreak against already-locked shorter ones.
                        if (bestHops.TryGetValue(v, out var dV) && dV < newHops) continue;

                        bool better = false;
                        if (!bestHops.ContainsKey(v))
                        {
                            better = true;
                            discoveredAtLevel.Add(v);
                        }
                        else if (bestHops[v] == newHops)
                        {
                            // Lex order: fewer clashes first, then more pickups.
                            if (newClashes < bestClashes[v]) better = true;
                            else if (newClashes == bestClashes[v] && newPickups > bestPickups[v]) better = true;
                        }

                        if (better)
                        {
                            bestHops[v] = newHops;
                            bestClashes[v] = newClashes;
                            bestPickups[v] = newPickups;
                            parents[v] = u;
                            if (!nextLevelBudget.TryGetValue(v, out var existingBudget) || existingBudget < newBudget)
                                nextLevelBudget[v] = newBudget;
                        }
                    }
                }

                foreach (var kvp in nextLevelBudget)
                {
                    bestBudget[kvp.Key] = kvp.Value;
                }

                frontier = new List<SpaceId>();
                foreach (var v in discoveredAtLevel)
                {
                    if (bestBudget[v] > 0) frontier.Add(v);
                }
                hops++;
            }

            if (!parents.ContainsKey(to)) return path;

            var stack = new Stack<SpaceId>();
            var cursor = to;
            while (!cursor.Equals(from))
            {
                stack.Push(cursor);
                cursor = parents[cursor];
            }
            while (stack.Count > 0) path.Add(stack.Pop());
            return path;
        }

        private static int GetSameToneCount(GameState gameState, SpaceId space, Tone tone)
        {
            var board = gameState.GetBoard(space.BoardId);
            var stack = board?.GetStack(space.Id);
            if (stack == null) return 0;
            return tone == Tone.Light ? stack.LightCount : stack.DarkCount;
        }

        private static int GetOppositeToneCount(GameState gameState, SpaceId space, Tone tone)
        {
            var board = gameState.GetBoard(space.BoardId);
            var stack = board?.GetStack(space.Id);
            if (stack == null) return 0;
            return tone == Tone.Light ? stack.DarkCount : stack.LightCount;
        }

        private IEnumerable<SpaceId> EnumerateSingleStepNeighbors(GameState gameState, SpaceId current, Tone tone, bool isOriginalSource)
        {
            var board = gameState.GetBoard(current.BoardId);
            if (board == null) yield break;

            foreach (var adj in board.GetAdjacentSpaces(current.Id))
                yield return new SpaceId(current.BoardId, adj);

            // Cross-board hops require the source space's stack to satisfy the 2-of-a-tone
            // gate. From an intermediate pass-through that gate is evaluated against the
            // intermediate's domain stack (not the traveling stack), so cross-board hops
            // are only emitted from the original source — matching the single-step rule.
            if (isOriginalSource && board.IsLedgeSpace(current.Id) && gameState.CanCrossBoard(current))
            {
                foreach (var cross in gameState.GetCrossBoardTargets(current))
                    yield return cross;
            }
        }

        public List<SpaceId> GetMovablePieces(GameState gameState, int playerId)
        {
            var movable = new List<SpaceId>();

            if (gameState.CurrentPhase != GamePhase.Movement)
                return movable;

            var player = gameState.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null || player.IsEliminated)
                return movable;

            var playerBoard = gameState.GetBoardForPlayer(playerId);
            if (playerBoard != null)
            {
                var lightMovable = playerBoard.GetMovableSpaces(Tone.Light);
                var darkMovable = playerBoard.GetMovableSpaces(Tone.Dark);

                foreach (var spaceId in lightMovable.Union(darkMovable))
                {
                    movable.Add(new SpaceId(playerBoard.BoardId, spaceId));
                }
            }

            foreach (var move in gameState.CurrentTurnMoves)
            {
                var board = gameState.GetBoard(move.To.BoardId);
                if (board != null && board.BoardId != playerBoard?.BoardId)
                {
                    var stack = board.GetStack(move.To.Id);
                    if (stack.CanMove(Tone.Light) || stack.CanMove(Tone.Dark))
                    {
                        movable.Add(move.To);
                    }
                }
            }

            return movable.Distinct().ToList();
        }

        public List<SpaceId> GetValidPlacementTargets(GameState gameState, int playerId)
        {
            var targets = new List<SpaceId>();

            if (gameState.CurrentPhase != GamePhase.Placement)
                return targets;

            var player = gameState.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null || player.IsEliminated)
                return targets;

            var board = gameState.GetBoardForPlayer(playerId);
            if (board == null) return targets;

            var validSpaces = board.GetValidPlacementTargets();
            foreach (var spaceId in validSpaces)
            {
                targets.Add(new SpaceId(board.BoardId, spaceId));
            }

            return targets;
        }

        public bool IsGameOver(GameState gameState)
        {
            return gameState.GameOver;
        }

        public int? GetWinner(GameState gameState)
        {
            return gameState.WinnerId;
        }

        /// True when the current player is in Movement phase with no legal sources to pick up —
        /// every controllable counter is locked. Used to auto-advance the turn so a deadended
        /// player isn't stuck staring at an unresponsive board. Placement-phase locks aren't
        /// checked here because placement never deadends.
        public bool ShouldAutoSkipTurn(GameState gameState)
        {
            if (gameState == null || gameState.GameOver) return false;
            if (gameState.CurrentPhase != GamePhase.Movement) return false;
            var player = gameState.GetCurrentPlayer();
            if (player == null || player.IsEliminated) return false;
            return GetMovablePieces(gameState, player.Id).Count == 0;
        }

        private bool IsValidMove(GameState gameState, SpaceId from, SpaceId to)
        {
            if (from.BoardId == to.BoardId)
            {
                var board = gameState.GetBoard(from.BoardId);
                var adjacentSpaces = board.GetAdjacentSpaces(from.Id);
                return adjacentSpaces.Contains(to.Id);
            }
            else
            {
                if (!gameState.CanCrossBoard(from))
                    return false;

                var validTargets = gameState.GetCrossBoardTargets(from);
                return validTargets.Contains(to);
            }
        }

        private bool IsSpaceControlled(GameState gameState, SpaceId space, int playerId)
        {
            var board = gameState.GetBoard(space.BoardId);
            if (board == null) return false;

            if (board.PlayerId == playerId)
                return true;

            // Control on an enemy board is earned by landing a counter — Lock or Stack.
            // A Clear result means the entering counter clashed 1-to-1 with an opposing
            // counter and did not land, so it confers no control even though the move
            // targeted this space. ResolveEntry is always called with enteringCount=1
            // by MoveToken, so Clear unambiguously means "0 counters landed from this move."
            foreach (var move in gameState.CurrentTurnMoves)
            {
                if (move.To.Equals(space) && move.Result != MoveResult.Clear)
                    return true;
            }

            return false;
        }
    }
}
