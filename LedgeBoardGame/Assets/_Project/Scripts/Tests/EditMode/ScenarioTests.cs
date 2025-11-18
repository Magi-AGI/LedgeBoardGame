using NUnit.Framework;
using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Rules;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    [TestFixture]
    public class ScenarioTests
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
                new Player(2, "Player2", 1),
                new Player(3, "Player3", 2)
            };
            _gameState = new GameState(players, null);
            SetupFullBoardAdjacency();
        }

        private void SetupFullBoardAdjacency()
        {
            foreach (var board in _gameState.Boards)
            {
                board.Adjacency[0] = new List<int> { 1, 2, 3, 4, 5, 6 };

                for (int i = 1; i <= 6; i++)
                {
                    board.Adjacency[i] = new List<int> { 0 };
                    if (i > 1) board.Adjacency[i].Add(i - 1);
                    if (i < 6) board.Adjacency[i].Add(i + 1);
                    else board.Adjacency[i].Add(1);
                    board.Adjacency[i].Add(i + 6);
                }

                for (int i = 7; i <= 18; i++)
                {
                    board.Adjacency[i] = new List<int> { i - 6 };
                    if (i > 7) board.Adjacency[i].Add(i - 1);
                    if (i < 18) board.Adjacency[i].Add(i + 1);
                    else board.Adjacency[i].Add(7);
                    if (i < 13) board.Adjacency[i].Add(i + 12);
                }

                board.SpaceMetadata[0] = new SpaceMeta(SpaceType.Center, 0, 0);
                for (int i = 1; i <= 6; i++)
                {
                    board.SpaceMetadata[i] = new SpaceMeta(SpaceType.InnerBridge, 1, i - 1);
                }
                for (int i = 7; i <= 12; i++)
                {
                    board.SpaceMetadata[i] = new SpaceMeta(SpaceType.InnerStop, 1, i - 7, true);
                }

                board.SpaceMetadata[37] = new SpaceMeta(SpaceType.Ledge, 4, 0, false, "Ela");
                board.SpaceMetadata[38] = new SpaceMeta(SpaceType.Ledge, 4, 1, false, "Biz");
                board.SpaceMetadata[39] = new SpaceMeta(SpaceType.Ledge, 4, 2, false, "Yun");

                board.LedgeSpacesByColor["Ela"] = new List<int> { 37 };
                board.LedgeSpacesByColor["Biz"] = new List<int> { 38 };
                board.LedgeSpacesByColor["Yun"] = new List<int> { 39 };

                board.Adjacency[37] = new List<int> { 36 };
                board.Adjacency[38] = new List<int> { 36 };
                board.Adjacency[39] = new List<int> { 36 };
            }

            SetupCrossBoardLedges();
        }

        private void SetupCrossBoardLedges()
        {
            var colors = new[] { "Ela", "Biz", "Yun" };
            foreach (var color in colors)
            {
                _gameState.CrossBoardLedgeEdges[color] = new List<CrossBoardEdge>();
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        if (i == j) continue;
                        var spaceId = color == "Ela" ? 37 : (color == "Biz" ? 38 : 39);
                        _gameState.CrossBoardLedgeEdges[color].Add(new CrossBoardEdge
                        {
                            From = new SpaceId(i, spaceId),
                            To = new SpaceId(j, spaceId),
                            Color = color
                        });
                    }
                }
            }
        }

        [Test]
        public void Scenario_LockTrail_ThreeEmpties()
        {
            _gameState.CurrentPhase = GamePhase.Movement;
            var board = _gameState.GetBoard(0);

            board.SetStack(1, new TokenStack(4, 0, Tone.Light));
            board.SetStack(2, new TokenStack());
            board.SetStack(3, new TokenStack());
            board.SetStack(4, new TokenStack());

            var move1 = _rules.MoveToken(_gameState, new SpaceId(0, 1), new SpaceId(0, 2), Tone.Light);
            var move2 = _rules.MoveToken(_gameState, new SpaceId(0, 1), new SpaceId(0, 3), Tone.Light);
            var move3 = _rules.MoveToken(_gameState, new SpaceId(0, 1), new SpaceId(0, 4), Tone.Light);

            Assert.AreEqual(1, board.GetStack(1).LightCount);
            Assert.AreEqual(1, board.GetStack(2).LightCount);
            Assert.AreEqual(1, board.GetStack(3).LightCount);
            Assert.AreEqual(1, board.GetStack(4).LightCount);

            Assert.IsTrue(board.GetStack(1).IsLocked(Tone.Light));
            Assert.IsTrue(board.GetStack(2).IsLocked(Tone.Light));
            Assert.IsTrue(board.GetStack(3).IsLocked(Tone.Light));
            Assert.IsTrue(board.GetStack(4).IsLocked(Tone.Light));
        }

        [Test]
        public void Scenario_ClearChain_LayeredTokens()
        {
            _gameState.CurrentPhase = GamePhase.Movement;
            var board = _gameState.GetBoard(0);

            board.SetStack(1, new TokenStack(0, 3, Tone.Dark));
            board.SetStack(2, new TokenStack(2, 0, Tone.Light));
            board.SetStack(3, new TokenStack(3, 0, Tone.Light));

            var move1 = _rules.MoveToken(_gameState, new SpaceId(0, 1), new SpaceId(0, 2), Tone.Dark);
            Assert.AreEqual(MoveResult.Clear, move1.Result);
            Assert.AreEqual(1, board.GetStack(2).LightCount);

            var move2 = _rules.MoveToken(_gameState, new SpaceId(0, 1), new SpaceId(0, 3), Tone.Dark);
            Assert.AreEqual(MoveResult.Clear, move2.Result);
            Assert.AreEqual(2, board.GetStack(3).LightCount);

            var move3 = _rules.MoveToken(_gameState, new SpaceId(0, 1), new SpaceId(0, 3), Tone.Dark);
            Assert.AreEqual(MoveResult.Clear, move3.Result);
            Assert.AreEqual(1, board.GetStack(3).LightCount);
            Assert.IsTrue(board.GetStack(3).IsLocked(Tone.Light));
        }

        [Test]
        public void Scenario_DoubleCross_LightPeelThenDarkPivot()
        {
            _gameState.CurrentPhase = GamePhase.Movement;
            var board0 = _gameState.GetBoard(0);
            var board1 = _gameState.GetBoard(1);

            board0.SetStack(36, new TokenStack(3, 0, Tone.Light));
            board0.SetStack(37, new TokenStack(0, 2, Tone.Dark));
            board1.SetStack(37, new TokenStack(2, 0, Tone.Light));
            board1.SetStack(36, new TokenStack());

            var move1 = _rules.MoveToken(_gameState, new SpaceId(0, 36), new SpaceId(0, 37), Tone.Light);
            Assert.AreEqual(MoveResult.Clear, move1.Result);

            var stack = board0.GetStack(37);
            Assert.AreEqual(1, stack.LightCount);
            Assert.AreEqual(1, stack.DarkCount);

            board0.SetStack(37, new TokenStack(0, 2, Tone.Dark));

            Assert.IsTrue(_gameState.CanCrossBoard(new SpaceId(0, 37)));

            var targets = _rules.GetValidMoveTargets(_gameState, new SpaceId(0, 37), Tone.Dark);
            Assert.IsTrue(targets.Contains(new SpaceId(1, 37)));
        }

        [Test]
        public void Scenario_MultiplayerDogpile_ThroughThirdBoard()
        {
            _gameState.CurrentPhase = GamePhase.Movement;
            _gameState.CurrentPlayerId = 1;

            var board0 = _gameState.GetBoard(0);
            var board1 = _gameState.GetBoard(1);
            var board2 = _gameState.GetBoard(2);

            board0.SetStack(37, new TokenStack(0, 3, Tone.Dark));
            board1.SetStack(37, new TokenStack());
            board2.SetStack(37, new TokenStack(2, 0, Tone.Light));
            board2.SetStack(36, new TokenStack());
            board2.SetStack(0, new TokenStack(2, 0, Tone.Light));
            board2.Adjacency[36] = new List<int> { 37, 35, 0 };

            var move1 = _rules.MoveToken(_gameState, new SpaceId(0, 37), new SpaceId(2, 37), Tone.Dark);
            Assert.AreEqual(MoveResult.Clear, move1.Result);
            Assert.AreEqual(1, board2.GetStack(37).LightCount);
            Assert.AreEqual(1, board2.GetStack(37).DarkCount);

            board1.SetStack(38, new TokenStack(0, 2, Tone.Dark));
            _gameState.CurrentTurnMoves.Add(new Move(new SpaceId(1, 38), new SpaceId(2, 38), Tone.Dark));

            board2.SetStack(38, new TokenStack(0, 2, Tone.Dark));
            board2.SetStack(36, new TokenStack());
            board2.Adjacency[38] = new List<int> { 36 };

            var move2 = _rules.MoveToken(_gameState, new SpaceId(2, 38), new SpaceId(2, 36), Tone.Dark);
            Assert.AreEqual(MoveResult.Lock, move2.Result);

            var move3 = _rules.MoveToken(_gameState, new SpaceId(2, 38), new SpaceId(2, 0), Tone.Dark);
            Assert.AreEqual(MoveResult.Clear, move3.Result);

            Assert.AreEqual(1, board2.GetStack(0).LightCount);
        }

        [Test]
        public void Scenario_EliminationChain_MultiplePlayers()
        {
            _gameState.CurrentPhase = GamePhase.Movement;
            _gameState.CurrentPlayerId = 1;

            var board1 = _gameState.GetBoard(1);
            var board2 = _gameState.GetBoard(2);

            board1.SetStack(0, new TokenStack(1, 0, Tone.Light));
            board2.SetStack(0, new TokenStack(1, 0, Tone.Light));

            board1.SetStack(1, new TokenStack(0, 2, Tone.Dark));
            board2.SetStack(1, new TokenStack(0, 2, Tone.Dark));

            var move1 = _rules.MoveToken(_gameState, new SpaceId(1, 1), new SpaceId(1, 0), Tone.Dark);
            Assert.AreEqual(MoveResult.Clear, move1.Result);
            board1.SetStack(0, new TokenStack(0, 1, Tone.Dark));

            var move2 = _rules.MoveToken(_gameState, new SpaceId(2, 1), new SpaceId(2, 0), Tone.Dark);
            Assert.AreEqual(MoveResult.Clear, move2.Result);
            board2.SetStack(0, new TokenStack(0, 1, Tone.Dark));

            Assert.IsTrue(board1.IsEliminated());
            Assert.IsTrue(board2.IsEliminated());

            _gameState.EndTurn();
            Assert.IsTrue(_gameState.GameOver);
            Assert.AreEqual(1, _gameState.WinnerId);
        }
    }
}
