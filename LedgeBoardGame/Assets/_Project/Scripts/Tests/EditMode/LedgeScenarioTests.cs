using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Spec;
using Magi.LedgeBoardGame.Rules;
using UnityEngine;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    [TestFixture]
    public class LedgeScenarioTests
    {
        [Test]
        public void PlacementScenario_MustPlaceBothTones_MatchesSpecExpectation()
        {
            var scenario = LoadScenario("placement-basic.v1.json");

            var initialSpecState = new SpecGameState
            {
                Players = scenario.Initial.Players,
                Ctx = scenario.Initial.Ctx,
                Data = null
            };

            var gameState = GameState.FromSpecState(initialSpecState);
            var rules = new GameRules(null);

            foreach (var move in scenario.Moves)
            {
                ApplyMove(rules, gameState, move, expectSuccess: true);
            }

            AssertExpectedState(scenario.Expected, gameState);
        }

        [Test]
        public void PlacementScenario_CannotPlaceTwoLights_SecondMoveIgnored()
        {
            var scenario = LoadScenario("placement-double-light.v1.json");

            var initialSpecState = new SpecGameState
            {
                Players = scenario.Initial.Players,
                Ctx = scenario.Initial.Ctx,
                Data = null
            };

            var gameState = GameState.FromSpecState(initialSpecState);
            var rules = new GameRules(null);

            // First Light should succeed
            ApplyMove(rules, gameState, scenario.Moves[0], expectSuccess: true);

            // Second Light should be rejected by rules (no state change)
            var before = gameState.ToSpecState();
            ApplyMove(rules, gameState, scenario.Moves[1], expectSuccess: false);
            var after = gameState.ToSpecState();

            Assert.AreEqual(before.Ctx.Phase, after.Ctx.Phase);
            Assert.AreEqual(before.Ctx.CurrentPlayer, after.Ctx.CurrentPlayer);
            Assert.AreEqual(before.Ctx.TurnNumber, after.Ctx.TurnNumber);

            AssertExpectedState(scenario.Expected, gameState);
        }

        private static LedgeScenario LoadScenario(string fileName)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var path = Path.Combine(projectRoot, "Specs", "ledge", "scenarios", fileName);
            Assert.IsTrue(File.Exists(path), $"Expected scenario file at {path}");

            var json = File.ReadAllText(path);
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
                {
                    NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()
                },
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            return JsonConvert.DeserializeObject<LedgeScenario>(json, settings);
        }

        [Test]
        public void MovementScenario_SingleStepAfterPlacement_MatchesSpecExpectation()
        {
            var scenario = LoadScenario("movement-single-step.v1.json");

            var initialSpecState = new SpecGameState
            {
                Players = scenario.Initial.Players,
                Ctx = scenario.Initial.Ctx,
                Data = null
            };

            var gameState = GameState.FromSpecState(initialSpecState);
            var rules = new GameRules(null);

            foreach (var move in scenario.Moves)
            {
                ApplyMove(rules, gameState, move, expectSuccess: true);
            }

            AssertExpectedState(scenario.Expected, gameState);
        }

        [Test]
        public void MovementScenario_LockTrail_ThreeEmpties_LeavesLocks()
        {
            var scenario = LoadScenario("movement-locktrail.v1.json");

            var initialSpecState = new SpecGameState
            {
                Players = scenario.Initial.Players,
                Ctx = scenario.Initial.Ctx,
                Data = null
            };

            var gameState = GameState.FromSpecState(initialSpecState);
            var rules = new GameRules(null);

            var board = gameState.GetBoard(0);
            board.SetStack(1, new TokenStack(4, 0, Tone.Light));
            board.SetStack(2, new TokenStack());
            board.SetStack(3, new TokenStack());
            board.SetStack(4, new TokenStack());

            // Simple adjacency allowing 1 to reach 2, 3, and 4 for this scenario.
            board.Adjacency[1] = new List<int> { 2, 3, 4 };

            foreach (var move in scenario.Moves)
            {
                ApplyMove(rules, gameState, move, expectSuccess: true);
            }

            Assert.AreEqual(1, board.GetStack(1).LightCount);
            Assert.AreEqual(1, board.GetStack(2).LightCount);
            Assert.AreEqual(1, board.GetStack(3).LightCount);
            Assert.AreEqual(1, board.GetStack(4).LightCount);

            Assert.IsTrue(board.GetStack(1).IsLocked(Tone.Light));
            Assert.IsTrue(board.GetStack(2).IsLocked(Tone.Light));
            Assert.IsTrue(board.GetStack(3).IsLocked(Tone.Light));
            Assert.IsTrue(board.GetStack(4).IsLocked(Tone.Light));

            AssertExpectedState(scenario.Expected, gameState);
        }

        [Test]
        public void MovementScenario_ClearChain_LayeredTokens_MatchesTokenExpectations()
        {
            var scenario = LoadScenario("movement-clearchain.v1.json");

            var initialSpecState = new SpecGameState
            {
                Players = scenario.Initial.Players,
                Ctx = scenario.Initial.Ctx,
                Data = null
            };

            var gameState = GameState.FromSpecState(initialSpecState);
            var rules = new GameRules(null);

            var board = gameState.GetBoard(0);

            board.SetStack(1, new TokenStack(0, 3, Tone.Dark));
            board.SetStack(2, new TokenStack(2, 0, Tone.Light));
            board.SetStack(3, new TokenStack(3, 0, Tone.Light));

            // Simple adjacency for this scenario: 1 can move to 2 and 3.
            board.Adjacency[1] = new List<int> { 2, 3 };

            foreach (var move in scenario.Moves)
            {
                ApplyMove(rules, gameState, move, expectSuccess: true);
            }

            Assert.AreEqual(0, board.GetStack(1).DarkCount);

            var stack2 = board.GetStack(2);
            var stack3 = board.GetStack(3);

            Assert.AreEqual(1, stack2.LightCount);
            Assert.AreEqual(1, stack3.LightCount);
            Assert.IsTrue(stack3.IsLocked(Tone.Light));

            AssertExpectedState(scenario.Expected, gameState);
        }

        [Test]
        public void MovementScenario_CrossBoard_LedgeJump_MovesBetweenBoards()
        {
            var scenario = LoadScenario("movement-crossboard-ledge.v1.json");

            var initialSpecState = new SpecGameState
            {
                Players = scenario.Initial.Players,
                Ctx = scenario.Initial.Ctx,
                Data = null
            };

            var gameState = GameState.FromSpecState(initialSpecState);
            var rules = new GameRules(null);

            var board0 = gameState.GetBoard(0);
            var board1 = gameState.GetBoard(1);

            // Ensure a stack of at least 2 on a ledge so cross-board is allowed.
            board0.SetStack(37, new TokenStack(2, 0, Tone.Light));
            board1.SetStack(37, new TokenStack());

            Assert.IsTrue(gameState.CanCrossBoard(new SpaceId(0, 37)), "Expected to be able to cross board from ledge 37.");

            foreach (var move in scenario.Moves)
            {
                ApplyMove(rules, gameState, move, expectSuccess: true);
            }

            var sourceStack = board0.GetStack(37);
            var targetStack = board1.GetStack(37);

            Assert.AreEqual(1, sourceStack.LightCount, "Source ledge should have one Light remaining.");
            Assert.AreEqual(1, targetStack.LightCount, "Target ledge should have one Light after cross-board move.");

            AssertExpectedState(scenario.Expected, gameState);
        }

        [Test]
        public void MovementScenario_CrossBoard_LedgeJump_FailsWithoutStack()
        {
            var scenario = LoadScenario("movement-crossboard-fail.v1.json");

            var initialSpecState = new SpecGameState
            {
                Players = scenario.Initial.Players,
                Ctx = scenario.Initial.Ctx,
                Data = null
            };

            var gameState = GameState.FromSpecState(initialSpecState);
            var rules = new GameRules(null);

            var board0 = gameState.GetBoard(0);
            var board1 = gameState.GetBoard(1);

            // Only a single Light on the ledge; not enough to cross-board.
            board0.SetStack(37, new TokenStack(1, 0, Tone.Light));
            board1.SetStack(37, new TokenStack());

            Assert.IsFalse(gameState.CanCrossBoard(new SpaceId(0, 37)), "Expected NOT to be able to cross board from ledge 37 with a single token.");

            var before = board0.GetStack(37).Clone();
            var beforeTarget = board1.GetStack(37).Clone();

            foreach (var move in scenario.Moves)
            {
                ApplyMove(rules, gameState, move, expectSuccess: false);
            }

            var after = board0.GetStack(37);
            var afterTarget = board1.GetStack(37);

            Assert.AreEqual(before.LightCount, after.LightCount, "Source ledge stack should be unchanged after failed cross-board attempt.");
            Assert.AreEqual(beforeTarget.LightCount, afterTarget.LightCount, "Target ledge stack should remain empty after failed cross-board attempt.");

            AssertExpectedState(scenario.Expected, gameState);
        }

        [Test]
        public void MovementScenario_EliminationChain_MultiplePlayers_SetsWinner()
        {
            var scenario = LoadScenario("movement-elimination-chain.v1.json");

            var initialSpecState = new SpecGameState
            {
                Players = scenario.Initial.Players,
                Ctx = scenario.Initial.Ctx,
                Data = null
            };

            var gameState = GameState.FromSpecState(initialSpecState);
            var rules = new GameRules();

            // Board 1 and 2 centers start empty; spaces 1 contain 2 Dark tokens each.
            var board1 = gameState.GetBoard(1);
            var board2 = gameState.GetBoard(2);

            board1.SetStack(0, new TokenStack());
            board2.SetStack(0, new TokenStack());

            board1.SetStack(1, new TokenStack(0, 2, Tone.Dark));
            board2.SetStack(1, new TokenStack(0, 2, Tone.Dark));

            // Adjacency for center moves in this scenario.
            board1.Adjacency[1] = new List<int> { 0 };
            board2.Adjacency[1] = new List<int> { 0 };

            foreach (var move in scenario.Moves)
            {
                ApplyMove(rules, gameState, move, expectSuccess: true);
            }

            Assert.IsTrue(board1.IsEliminated(), "Board 1 should be eliminated after dark token locks on center.");
            Assert.IsTrue(board2.IsEliminated(), "Board 2 should be eliminated after dark token locks on center.");

            Assert.IsTrue(gameState.GameOver, "Game should be over after elimination chain.");
            Assert.AreEqual(1, gameState.WinnerId, "Player 1 should be the winner.");

            AssertExpectedState(scenario.Expected, gameState);
        }

        private static void ApplyMove(GameRules rules, GameState gameState, LedgeScenarioMove move, bool expectSuccess)
        {
            if (move == null) return;

            switch (move.Move)
            {
                case "placeToken":
                    var boardId = move.Args.BoardId;
                    var spaceId = move.Args.SpaceId.GetValueOrDefault();
                    var tone = ParseTone(move.Args.Tone);
                    var target = new SpaceId(boardId, spaceId);
                    var result = rules.PlaceToken(gameState, target, tone);
                    if (expectSuccess)
                    {
                        Assert.IsNotNull(result, "Expected placeToken to succeed but it returned null.");
                    }
                    else
                    {
                        Assert.IsNull(result, "Expected placeToken to fail but it returned a move.");
                    }
                    break;

                case "moveToken":
                    var fromBoardId = move.Args.FromBoardId.GetValueOrDefault();
                    var fromSpaceId = move.Args.FromSpaceId.GetValueOrDefault();
                    var toBoardId = move.Args.ToBoardId.GetValueOrDefault();
                    var toSpaceId = move.Args.ToSpaceId.GetValueOrDefault();
                    var moveTone = ParseTone(move.Args.Tone);
                    var from = new SpaceId(fromBoardId, fromSpaceId);
                    var to = new SpaceId(toBoardId, toSpaceId);
                    var moveResult = rules.MoveToken(gameState, from, to, moveTone);
                    if (expectSuccess)
                    {
                        Assert.IsNotNull(moveResult, "Expected moveToken to succeed but it returned null.");
                    }
                    else
                    {
                        Assert.IsNull(moveResult, "Expected moveToken to fail but it returned a move.");
                    }
                    break;

                case "endTurn":
                    gameState.EndTurn();
                    break;

                default:
                    Assert.Fail($"Unsupported move type '{move.Move}' in scenario.");
                    break;
            }
        }

        private static Tone ParseTone(string tone)
        {
            return (Tone)Enum.Parse(typeof(Tone), tone, true);
        }

        private static void AssertExpectedState(LedgeScenarioExpected expected, GameState gameState)
        {
            if (expected == null) return;

            if (!string.IsNullOrEmpty(expected.Phase))
            {
                var expectedPhase = (GamePhase)Enum.Parse(typeof(GamePhase), expected.Phase, true);
                Assert.AreEqual(expectedPhase, gameState.CurrentPhase, "Phase did not match expected value from scenario.");
            }

            if (!string.IsNullOrEmpty(expected.CurrentPlayer))
            {
                Assert.AreEqual(int.Parse(expected.CurrentPlayer), gameState.CurrentPlayerId, "Current player did not match expected value from scenario.");
            }

            if (expected.TurnNumber.HasValue)
            {
                Assert.AreEqual(expected.TurnNumber.Value, gameState.TurnNumber, "Turn number did not match expected value from scenario.");
            }
        }
    }
}
