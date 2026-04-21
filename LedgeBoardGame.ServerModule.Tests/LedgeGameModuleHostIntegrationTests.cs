using System.Linq;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Network;
using Magi.LedgeBoardGame.Models.Spec;
using Magi.LedgeBoardGame.Rules;
using Magi.LedgeBoardGame.ServerModule;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using MagiGameServer.Core;
using NUnit.Framework;

namespace Magi.LedgeBoardGame.ServerModule.Tests
{
    /// End-to-end proof that LedgeGameModule plugs into the game-agnostic
    /// Session host without any LedgeBoardGame-specific infrastructure. A
    /// seat-1 action must produce a legal echo visible to seat 2 with the
    /// same post-apply hash — that's the full "action leaves trust boundary,
    /// reconciles on the other side" loop for a public-info game. Commit 2
    /// will layer InProcessMagiTransport on top of this same Session
    /// surface so the Unity client can exercise the loop without a running
    /// HTTP host.
    [TestFixture]
    public class LedgeGameModuleHostIntegrationTests
    {
        private static (Session session, LedgeGameModule module) NewHostedSession(int seatCount = 2)
        {
            var module = new LedgeGameModule();
            var initial = module.CreateInitialState(LedgeGameModule.DefaultConfig(seatCount));
            var session = new Session(new SessionId("ledge-test"), module, initial, seatCount);
            return (session, module);
        }

        private static SpaceId FirstValidPlacementFor(Session session)
        {
            var (projected, _, _) = session.ProjectForSeat(new SeatId(0));
            var spec = (SpecGameState)projected;
            var gs = GameState.FromSpecState(spec);
            var rules = new GameRules();
            var currentPlayer = gs.GetCurrentPlayer();
            return rules.GetValidPlacementTargets(gs, currentPlayer.Id).First();
        }

        [Test]
        public void Seat0_PlaceToken_EchoesLegalStateToBothSeats()
        {
            var (session, _) = NewHostedSession(seatCount: 2);
            var target = FirstValidPlacementFor(session);
            var envelope = new ActionEnvelope<object>
            {
                Session = session.Id,
                Seat = new SeatId(0),
                Seq = new ClientSeq(1),
                Action = LedgeAction.PlaceToken(target, Tone.Light),
                PredictedStateHash = 0,
            };

            var result = session.Apply(envelope);

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Applied));
            Assert.That(result.Revision.Value, Is.EqualTo(1));
            Assert.That(result.Echoes, Has.Count.EqualTo(2), "both seats receive an echo");

            // Perfect-info game: both seats get identical state hashes back,
            // so the reconcile path on seat 1 sees the same canonical state
            // seat 0 just produced — no hidden-info projection divergence.
            var hashes = result.Echoes.Select(e => e.StateHash).Distinct().ToList();
            Assert.That(hashes, Has.Count.EqualTo(1),
                "identity projection → all seats hash the same canonical state");

            foreach (var echo in result.Echoes)
            {
                Assert.That(echo.Outcome, Is.EqualTo(ApplyOutcome.Applied));
                var echoedState = (SpecGameState)echo.State;
                Assert.That(echoedState.CurrentTurnPlacements, Has.Count.EqualTo(1),
                    "echoed state must reflect the placement");
                Assert.That(echoedState.CurrentTurnPlacements[0].Tone, Is.EqualTo(Tone.Light));
            }
        }

        [Test]
        public void Seat1_PlaceTokenDuringSeat0Turn_IsRejectedWithStateAtPrior()
        {
            var (session, _) = NewHostedSession(seatCount: 2);
            var target = FirstValidPlacementFor(session);
            // Seat 1 (player 2) tries to place while it's still seat 0's
            // turn. Even if the target is syntactically valid for player 2's
            // own board, CanPlaceToken gates on the current player — so the
            // adapter rejects. The Session layer still broadcasts the
            // rejection echo to both seats so seat 1's optimistic prediction
            // can roll back.
            var seat1Board = new SpaceId(boardId: 2, id: target.Id);
            var envelope = new ActionEnvelope<object>
            {
                Session = session.Id,
                Seat = new SeatId(1),
                Seq = new ClientSeq(1),
                Action = LedgeAction.PlaceToken(seat1Board, Tone.Light),
                PredictedStateHash = 0,
            };

            var result = session.Apply(envelope);

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Rejected));
            Assert.That(result.Revision.Value, Is.EqualTo(0),
                "rejected action must not advance the revision");
            Assert.That(result.Echoes, Has.Count.EqualTo(2));
            foreach (var echo in result.Echoes)
            {
                Assert.That(echo.Outcome, Is.EqualTo(ApplyOutcome.Rejected));
                var echoedState = (SpecGameState)echo.State;
                Assert.That(echoedState.CurrentTurnPlacements, Is.Empty,
                    "rejected echo carries the pre-action (still-canonical) state");
            }
        }

        [Test]
        public void TwoRoundTrip_Place_Place_EndTurn_AdvancesSeatAndClearsTurnLog()
        {
            var (session, _) = NewHostedSession(seatCount: 2);

            // P2 turn-skip logic advances past disconnected seats, and the JIP
            // default leaves every roster entry IsConnected=false. Without
            // presence flips here, EndTurn loops back to seat 0. Real sessions
            // get presence=true from SessionRuntime when a client attaches.
            session.SetSeatPresence(new SeatId(0), true);
            session.SetSeatPresence(new SeatId(1), true);

            // Seat 0 places Light, then Dark, then ends turn.
            var lightTarget = FirstValidPlacementFor(session);
            session.Apply(new ActionEnvelope<object>
            {
                Session = session.Id,
                Seat = new SeatId(0),
                Seq = new ClientSeq(1),
                Action = LedgeAction.PlaceToken(lightTarget, Tone.Light),
                PredictedStateHash = 0,
            });

            var darkTarget = FirstValidPlacementFor(session);
            session.Apply(new ActionEnvelope<object>
            {
                Session = session.Id,
                Seat = new SeatId(0),
                Seq = new ClientSeq(2),
                Action = LedgeAction.PlaceToken(darkTarget, Tone.Dark),
                PredictedStateHash = 0,
            });

            var endTurnResult = session.Apply(new ActionEnvelope<object>
            {
                Session = session.Id,
                Seat = new SeatId(0),
                Seq = new ClientSeq(3),
                Action = LedgeAction.EndTurn(),
                PredictedStateHash = 0,
            });

            Assert.That(endTurnResult.Outcome, Is.EqualTo(ApplyOutcome.Applied));
            var finalState = (SpecGameState)endTurnResult.Echoes[0].State;
            Assert.That(finalState.CurrentTurnPlacements, Is.Empty,
                "turn log must clear after EndTurn");
            Assert.That(finalState.CurrentTurnMoves, Is.Empty);
            Assert.That(finalState.Ctx.CurrentPlayer, Is.Not.EqualTo("1"),
                "EndTurn must advance the current-player pointer");
        }

        [Test]
        public void Seat0_EndTurnWithIncompletePlacement_IsRejectedAndRevisionStays()
        {
            // Server-auth EndTurn gate: Placement phase requires both tones to be
            // placed before the turn ends. An adversarial client that sends
            // EndTurn directly must see Rejected and both seats must see the
            // session revision stay pinned so their optimistic state rolls back.
            var (session, _) = NewHostedSession(seatCount: 2);
            var result = session.Apply(new ActionEnvelope<object>
            {
                Session = session.Id,
                Seat = new SeatId(0),
                Seq = new ClientSeq(1),
                Action = LedgeAction.EndTurn(),
                PredictedStateHash = 0,
            });

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Rejected));
            Assert.That(result.Revision.Value, Is.EqualTo(0),
                "rejected EndTurn must not advance session revision");
            Assert.That(result.Echoes, Has.Count.EqualTo(2));
            foreach (var echo in result.Echoes)
            {
                Assert.That(echo.Outcome, Is.EqualTo(ApplyOutcome.Rejected));
                var echoedState = (SpecGameState)echo.State;
                Assert.That(echoedState.CurrentTurnPlacements, Is.Empty,
                    "rejected echo carries the pre-action state (still at zero placements)");
            }
        }

        [Test]
        public void CreateInitialState_NoSeedOption_LeavesBoardsEmptyExceptCenter()
        {
            // Sanity: without the U12 seed option, every board starts with
            // only the default Light-locked center stack. Regression guard
            // against pacing-accelerator leaking into normal sessions.
            var module = new LedgeGameModule();
            var initial = (SpecGameState)module.CreateInitialState(LedgeGameModule.DefaultConfig(2));
            var gs = GameState.FromSpecState(initial);
            foreach (var board in gs.Boards)
            {
                foreach (var kvp in board.Spaces)
                {
                    var count = kvp.Value.TotalCount;
                    if (board.IsCenterSpace(kvp.Key))
                        Assert.That(count, Is.EqualTo(1), $"center stays default Light-locked; space={kvp.Key}");
                    else
                        Assert.That(count, Is.EqualTo(0), $"non-center stays empty without seeding; space={kvp.Key}");
                }
            }
        }

        [Test]
        public void CreateInitialState_SeedPlacementsPerTone_FillsTwoPerToneOnEveryBoard()
        {
            var module = new LedgeGameModule();
            var options = new System.Collections.Generic.Dictionary<string, string>
            {
                [LedgeGameModule.SeedPlacementsPerToneKey] = "2",
            };
            var initial = (SpecGameState)module.CreateInitialState(
                LedgeGameModule.DefaultConfig(seatCount: 3, options: options));
            var gs = GameState.FromSpecState(initial);

            foreach (var board in gs.Boards)
            {
                int lightSeeds = 0, darkSeeds = 0;
                foreach (var kvp in board.Spaces)
                {
                    if (board.IsCenterSpace(kvp.Key)) continue;
                    var stack = kvp.Value;
                    if (stack.LightCount == 1 && stack.DarkCount == 0) lightSeeds++;
                    else if (stack.DarkCount == 1 && stack.LightCount == 0) darkSeeds++;
                    else Assert.That(stack.TotalCount, Is.EqualTo(0),
                        $"non-seeded non-center space should stay empty; board={board.BoardId} space={kvp.Key}");
                }
                Assert.That(lightSeeds, Is.EqualTo(2), $"board {board.BoardId} should have 2 seeded Light singletons");
                Assert.That(darkSeeds, Is.EqualTo(2), $"board {board.BoardId} should have 2 seeded Dark singletons");
            }
        }

        [Test]
        public void CreateInitialState_SameSeed_ReproducesSameSeededLayout()
        {
            var module = new LedgeGameModule();
            var options = new System.Collections.Generic.Dictionary<string, string>
            {
                [LedgeGameModule.SeedPlacementsPerToneKey] = "3",
            };
            MagiGameServer.Contracts.Rules.GameConfig MakeCfg(long seed) => new MagiGameServer.Contracts.Rules.GameConfig
            {
                Seed = seed,
                SeatCount = 2,
                Options = options,
            };
            var a = (SpecGameState)module.CreateInitialState(MakeCfg(42));
            var b = (SpecGameState)module.CreateInitialState(MakeCfg(42));

            // Deterministic: same seed, same options → byte-identical seeded
            // board. Swap seeds and the layouts must diverge — guards against
            // the RNG being globally shared or boardId being ignored.
            var gsA = GameState.FromSpecState(a);
            var gsB = GameState.FromSpecState(b);
            for (int boardIx = 0; boardIx < gsA.Boards.Count; boardIx++)
            {
                var boardA = gsA.Boards[boardIx];
                var boardB = gsB.Boards[boardIx];
                foreach (var spaceId in boardA.Spaces.Keys)
                {
                    var sa = boardA.GetStack(spaceId);
                    var sb = boardB.GetStack(spaceId);
                    Assert.That(sa.LightCount, Is.EqualTo(sb.LightCount), $"board {boardIx} space {spaceId} Light");
                    Assert.That(sa.DarkCount, Is.EqualTo(sb.DarkCount), $"board {boardIx} space {spaceId} Dark");
                }
            }

            var c = (SpecGameState)module.CreateInitialState(MakeCfg(99));
            var gsC = GameState.FromSpecState(c);
            bool anyDifference = false;
            for (int boardIx = 0; boardIx < gsA.Boards.Count && !anyDifference; boardIx++)
            {
                foreach (var spaceId in gsA.Boards[boardIx].Spaces.Keys)
                {
                    var sa = gsA.Boards[boardIx].GetStack(spaceId);
                    var sc = gsC.Boards[boardIx].GetStack(spaceId);
                    if (sa.LightCount != sc.LightCount || sa.DarkCount != sc.DarkCount)
                    {
                        anyDifference = true;
                        break;
                    }
                }
            }
            Assert.That(anyDifference, Is.True, "different seeds must produce different layouts");
        }

        [Test]
        public void ValidateSubmission_SetDisplayName_TargetingOwnSeatIsAccepted()
        {
            // BuildDefaultRoster assigns playerId = seatIndex + 1. Seat 0's
            // own player is Id=1 — a SetDisplayName carrying Id=1 from seat
            // 0 is the only shape the server should accept without a
            // cross-cutting rejection.
            var module = new LedgeGameModule();
            var action = LedgeAction.SetDisplayName(playerId: 1, displayName: "Alice");
            var rejection = module.ValidateSubmission(new SeatId(0), action);
            Assert.That(rejection, Is.Null, "seat-owned SetDisplayName must pass validation");
        }

        [Test]
        public void ValidateSubmission_SetDisplayName_TargetingOtherSeatIsRejected()
        {
            // Attack shape: seat 0 (playerId=1) tries to rename seat 1's
            // player (playerId=2). Without the validator, RulesExecutor's
            // ApplySetDisplayName gates only on playerId > 0 and target
            // existence, so an adversarial client could clobber any
            // roster entry every turn.
            var module = new LedgeGameModule();
            var action = LedgeAction.SetDisplayName(playerId: 2, displayName: "Mallory");
            var rejection = module.ValidateSubmission(new SeatId(0), action);
            Assert.That(rejection, Is.EqualTo("display_name_not_owned"),
                "cross-seat rename must be rejected with the documented snake_case reason");
        }

        [Test]
        public void ValidateSubmission_NonSetDisplayName_PassesThrough()
        {
            // Validator is scoped to SetDisplayName only — Place/Move/EndTurn
            // carry no PlayerId and are already gated by the CurrentTurnSeat
            // rule inside RulesExecutor. Returning null here lets Apply run
            // its normal rules-layer checks without a duplicate gate.
            var module = new LedgeGameModule();
            var place = LedgeAction.PlaceToken(new SpaceId(0, 0), Tone.Light);
            Assert.That(module.ValidateSubmission(new SeatId(0), place), Is.Null);
            Assert.That(module.ValidateSubmission(new SeatId(1), LedgeAction.EndTurn()), Is.Null);
        }

        [Test]
        public void ValidateSubmission_NonLedgeAction_PassesThrough()
        {
            // Defensive: if some other module's action type somehow lands
            // here (e.g. future multi-module routing bug), the validator
            // should no-op rather than falsely reject.
            var module = new LedgeGameModule();
            Assert.That(module.ValidateSubmission(new SeatId(0), new object()), Is.Null);
            Assert.That(module.ValidateSubmission(new SeatId(0), null), Is.Null);
        }

        [Test]
        public void CreateSessionFromSpecOptions_CarriesConfigIntoEveryEcho()
        {
            // Codex reviewer blocker: spec-driven clients must see spec-driven rules
            // on the server. This is the end-to-end proof that GameConfig.Options
            // flows: spec JSON → LedgeGameModule.CreateInitialState → SpecGameState.Config
            // → RulesExecutor → echo to every seat.
            var module = new LedgeGameModule();
            var options = new System.Collections.Generic.Dictionary<string, string>
            {
                [LedgeGameModule.SpecJsonOptionKey] = LedgeRulesAdapterTests.LoadCanonicalSpecJson(),
            };
            var gameConfig = LedgeGameModule.DefaultConfig(seatCount: 2, options: options);
            var initial = module.CreateInitialState(gameConfig);
            var session = new Session(new SessionId("ledge-spec-test"), module, initial, 2);

            var target = FirstValidPlacementFor(session);
            var result = session.Apply(new ActionEnvelope<object>
            {
                Session = session.Id,
                Seat = new SeatId(0),
                Seq = new ClientSeq(1),
                Action = LedgeAction.PlaceToken(target, Tone.Light),
                PredictedStateHash = 0,
            });

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Applied));
            foreach (var echo in result.Echoes)
            {
                var echoedState = (SpecGameState)echo.State;
                Assert.That(echoedState.Config, Is.Not.Null,
                    "every echo must carry the spec-driven runtime config");
                Assert.That(echoedState.Config.MinPlayers, Is.EqualTo(2));
                Assert.That(echoedState.Config.MaxPlayers, Is.EqualTo(8));
            }
        }
    }
}
