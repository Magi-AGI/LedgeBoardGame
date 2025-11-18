using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Rules;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    [TestFixture]
    public class GameRulesTests
    {
        private GameRules _rules;
        private GameState _gameState;

        [SetUp]
        public void Setup()
        {
            _rules = new GameRules(null);
            var players = new List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1)
            };
            _gameState = new GameState(players, null);
            SetupTestAdjacency();
        }

        private void SetupTestAdjacency()
        {
            foreach (var board in _gameState.Boards)
            {
                board.Adjacency[0] = new List<int> { 1, 2, 3, 4, 5, 6 };
                for (int i = 1; i <= 6; i++)
                {
                    board.Adjacency[i] = new List<int> { 0 };
                    if (i > 1) board.Adjacency[i].Add(i - 1);
                    if (i < 6) board.Adjacency[i].Add(i + 1);
                }

                board.SpaceMetadata[0] = new SpaceMeta(SpaceType.Center, 0, 0);
                for (int i = 1; i <= 6; i++)
                {
                    board.SpaceMetadata[i] = new SpaceMeta(SpaceType.InnerBridge, 1, i - 1);
                }

                board.SpaceMetadata[37] = new SpaceMeta(SpaceType.Ledge, 4, 0, false, "Ela");
                board.SpaceMetadata[38] = new SpaceMeta(SpaceType.Ledge, 4, 1, false, "Biz");
                board.LedgeSpacesByColor["Ela"] = new List<int> { 37 };
                board.LedgeSpacesByColor["Biz"] = new List<int> { 38 };
            }

            _gameState.CrossBoardLedgeEdges["Ela"] = new List<CrossBoardEdge>
            {
                new CrossBoardEdge { From = new SpaceId(0, 37), To = new SpaceId(1, 37), Color = "Ela" },
                new CrossBoardEdge { From = new SpaceId(1, 37), To = new SpaceId(0, 37), Color = "Ela" }
            };
        }

        [Test]
        public void PlacementPhase_CanPlaceOnOwnBoard()
        {
            var target = new SpaceId(0, 1);
            Assert.IsTrue(_rules.CanPlaceToken(_gameState, target, Tone.Light));
            Assert.IsTrue(_rules.CanPlaceToken(_gameState, target, Tone.Dark));
        }

        [Test]
        public void PlacementPhase_CannotPlaceOnOpponentBoard()
        {
            var target = new SpaceId(1, 1);
            Assert.IsFalse(_rules.CanPlaceToken(_gameState, target, Tone.Light));
            Assert.IsFalse(_rules.CanPlaceToken(_gameState, target, Tone.Dark));
        }

        [Test]
        public void PlacementPhase_MustPlaceBothTones()
        {
            var target1 = new SpaceId(0, 1);
            var target2 = new SpaceId(0, 2);

            var move1 = _rules.PlaceToken(_gameState, target1, Tone.Light);
            Assert.IsNotNull(move1);
            Assert.AreEqual(GamePhase.Placement, _gameState.CurrentPhase);

            Assert.IsFalse(_rules.CanPlaceToken(_gameState, target2, Tone.Light));

            var move2 = _rules.PlaceToken(_gameState, target2, Tone.Dark);
            Assert.IsNotNull(move2);
            Assert.AreEqual(GamePhase.Movement, _gameState.CurrentPhase);
        }

        [Test]
        public void Movement_CanMoveStackedToken()
        {
            _gameState.CurrentPhase = GamePhase.Movement;
            var board = _gameState.GetBoard(0);
            board.SetStack(1, new TokenStack(2, 0, Tone.Light));

            var from = new SpaceId(0, 1);
            var to = new SpaceId(0, 0);

            Assert.IsTrue(_rules.CanMoveToken(_gameState, from, to, Tone.Light));
        }

        [Test]
        public void Movement_CannotMoveLockedToken()
        {
            _gameState.CurrentPhase = GamePhase.Movement;
            var board = _gameState.GetBoard(0);
            board.SetStack(1, new TokenStack(1, 0, Tone.Light));

            var from = new SpaceId(0, 1);
            var to = new SpaceId(0, 0);

            Assert.IsFalse(_rules.CanMoveToken(_gameState, from, to, Tone.Light));
        }

        [Test]
        public void Movement_LeavesLockTrail()
        {
            _gameState.CurrentPhase = GamePhase.Movement;
            var board = _gameState.GetBoard(0);
            board.SetStack(1, new TokenStack(3, 0, Tone.Light));
            board.SetStack(2, new TokenStack());
            board.SetStack(3, new TokenStack());

            var move1 = _rules.MoveToken(_gameState, new SpaceId(0, 1), new SpaceId(0, 2), Tone.Light);
            Assert.AreEqual(MoveResult.Lock, move1.Result);
            Assert.AreEqual(2, board.GetStack(1).LightCount);
            Assert.AreEqual(1, board.GetStack(2).LightCount);
            Assert.IsTrue(board.GetStack(2).IsLocked(Tone.Light));

            var move2 = _rules.MoveToken(_gameState, new SpaceId(0, 1), new SpaceId(0, 3), Tone.Light);
            Assert.AreEqual(MoveResult.Lock, move2.Result);
            Assert.AreEqual(1, board.GetStack(1).LightCount);
            Assert.AreEqual(1, board.GetStack(3).LightCount);
            Assert.IsTrue(board.GetStack(1).IsLocked(Tone.Light));
            Assert.IsTrue(board.GetStack(3).IsLocked(Tone.Light));
        }

        [Test]
        public void ClearChain_ResolvesCorrectly()
        {
            _gameState.CurrentPhase = GamePhase.Movement;
            var board = _gameState.GetBoard(0);
            board.SetStack(1, new TokenStack(3, 0, Tone.Dark));
            board.SetStack(2, new TokenStack(2, 0, Tone.Light));

            var move = _rules.MoveToken(_gameState, new SpaceId(0, 1), new SpaceId(0, 2), Tone.Dark);
            Assert.AreEqual(MoveResult.Clear, move.Result);

            var stack1 = board.GetStack(1);
            var stack2 = board.GetStack(2);
            Assert.AreEqual(2, stack1.DarkCount);
            Assert.AreEqual(1, stack2.LightCount);
            Assert.IsTrue(stack2.IsLocked(Tone.Light));
        }

        [Test]
        public void CrossBoardMovement_RequiresStackOnLedge()
        {
            _gameState.CurrentPhase = GamePhase.Movement;
            var board0 = _gameState.GetBoard(0);
            var board1 = _gameState.GetBoard(1);

            board0.SetStack(37, new TokenStack(1, 0, Tone.Light));
            board1.SetStack(37, new TokenStack());
            board0.Adjacency[37] = new List<int> { 36 };

            var from = new SpaceId(0, 37);
            var to = new SpaceId(1, 37);

            Assert.IsFalse(_gameState.CanCrossBoard(from));

            board0.SetStack(37, new TokenStack(2, 0, Tone.Light));
            Assert.IsTrue(_gameState.CanCrossBoard(from));
        }

        [Test]
        public void Elimination_DarkOnCenterEliminates()
        {
            _gameState.CurrentPhase = GamePhase.Movement;
            var board0 = _gameState.GetBoard(0);
            var board1 = _gameState.GetBoard(1);

            board0.SetStack(1, new TokenStack(0, 2, Tone.Dark));
            board1.SetStack(0, new TokenStack(1, 0, Tone.Light));
            board1.SetStack(1, new TokenStack());
            board1.Adjacency[1] = new List<int> { 0 };

            _gameState.CurrentPlayerId = 1;
            var move = _rules.MoveToken(_gameState, new SpaceId(0, 1), new SpaceId(1, 1), Tone.Dark);
            _gameState.CurrentTurnMoves.Add(move);

            var move2 = _rules.MoveToken(_gameState, new SpaceId(1, 1), new SpaceId(1, 0), Tone.Dark);
            Assert.AreEqual(MoveResult.Clear, move2.Result);

            var centerStack = board1.GetStack(0);
            Assert.AreEqual(0, centerStack.LightCount);
            Assert.AreEqual(0, centerStack.DarkCount);

            board1.SetStack(0, new TokenStack(0, 1, Tone.Dark));
            Assert.IsTrue(board1.IsEliminated());
        }

        [Test]
        public void GameEnds_WhenOnePlayerRemains()
        {
            _gameState.Players[1].IsEliminated = true;
            _gameState.EndTurn();

            Assert.IsTrue(_gameState.GameOver);
            Assert.AreEqual(1, _gameState.WinnerId);
        }
    }
}
