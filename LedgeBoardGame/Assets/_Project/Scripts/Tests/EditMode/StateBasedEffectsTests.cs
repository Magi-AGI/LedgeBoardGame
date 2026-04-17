using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Rules;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    /// Covers the state-based-effects contract: Dark-lock on a player's center
    /// eliminates them regardless of whose turn it is, last-player-standing wins,
    /// and a fully-locked active player should auto-skip their turn. Placement
    /// phase is explicitly excluded from SBE per design.
    [TestFixture]
    public class StateBasedEffectsTests
    {
        private const int CENTER = 0;

        private GameState BuildGame(int playerCount = 2)
        {
            var players = new List<Player>();
            for (int i = 0; i < playerCount; i++)
            {
                players.Add(new Player(i + 1, $"Player{i + 1}", i));
            }
            var state = new GameState(players, null);
            state.CurrentPhase = GamePhase.Movement;
            return state;
        }

        [Test]
        public void ApplyStateBasedEffects_DarkLockOnCenter_EliminatesOwner()
        {
            var state = BuildGame();
            // Player 2's center gets Dark-locked. Player 2 did not cause it (not their turn),
            // but the lock is on their board — SBE must still eliminate them.
            state.Boards[1].SetStack(CENTER, new TokenStack(0, 1, Tone.Dark));

            var result = state.ApplyStateBasedEffects();

            Assert.Contains(2, result.NewlyEliminatedPlayerIds,
                "Player 2 should be newly eliminated from Dark lock on their center.");
            Assert.IsTrue(state.Players.First(p => p.Id == 2).IsEliminated);
            Assert.IsFalse(state.Players.First(p => p.Id == 1).IsEliminated);
        }

        [Test]
        public void ApplyStateBasedEffects_LastPlayerStanding_Wins()
        {
            var state = BuildGame();
            state.Boards[1].SetStack(CENTER, new TokenStack(0, 1, Tone.Dark));

            var result = state.ApplyStateBasedEffects();

            Assert.IsTrue(result.GameEnded);
            Assert.AreEqual(1, result.WinnerId);
            Assert.IsTrue(state.GameOver);
            Assert.AreEqual(1, state.WinnerId);
        }

        [Test]
        public void ApplyStateBasedEffects_SameStateTwice_SecondPassIsNoOp()
        {
            var state = BuildGame();
            state.Boards[1].SetStack(CENTER, new TokenStack(0, 1, Tone.Dark));
            state.ApplyStateBasedEffects();

            var second = state.ApplyStateBasedEffects();

            Assert.IsFalse(second.HasAnyEffect,
                "Idempotent re-run: no new eliminations or state-transitions on unchanged state.");
        }

        [Test]
        public void ApplyStateBasedEffects_UntouchedBoard_NoEffect()
        {
            var state = BuildGame();
            var result = state.ApplyStateBasedEffects();

            Assert.IsFalse(result.HasAnyEffect);
            Assert.IsFalse(state.GameOver);
        }

        [Test]
        public void ApplyStateBasedEffects_ThreePlayerWithTwoDarkLocked_LeavesWinner()
        {
            var state = BuildGame(3);
            state.Boards[1].SetStack(CENTER, new TokenStack(0, 1, Tone.Dark));
            state.Boards[2].SetStack(CENTER, new TokenStack(0, 1, Tone.Dark));

            var result = state.ApplyStateBasedEffects();

            CollectionAssert.AreEquivalent(new[] { 2, 3 }, result.NewlyEliminatedPlayerIds);
            Assert.IsTrue(result.GameEnded);
            Assert.AreEqual(1, result.WinnerId);
        }

        [Test]
        public void ShouldAutoSkipTurn_MovementPhase_AllSourcesLocked_ReturnsTrue()
        {
            var state = BuildGame();
            var rules = new GameRules(null);

            // Player 1's board: Light-lock the center (default is already Light-locked);
            // leave all other spaces empty. No movable sources anywhere.
            foreach (var kvp in state.Boards[0].Spaces.Keys.ToList())
            {
                if (kvp == CENTER) continue;
                state.Boards[0].SetStack(kvp, new TokenStack());
            }

            Assert.IsTrue(rules.ShouldAutoSkipTurn(state),
                "Only a locked center remains — no movable counters, turn should auto-skip.");
        }

        [Test]
        public void ShouldAutoSkipTurn_MovementPhase_HasMovableStack_ReturnsFalse()
        {
            var state = BuildGame();
            var rules = new GameRules(null);
            state.Boards[0].SetStack(1, new TokenStack(2, 0, Tone.Light));

            Assert.IsFalse(rules.ShouldAutoSkipTurn(state),
                "A 2-Light stack is movable — turn should not be skipped.");
        }

        [Test]
        public void ShouldAutoSkipTurn_PlacementPhase_ReturnsFalse()
        {
            var state = BuildGame();
            state.CurrentPhase = GamePhase.Placement;
            var rules = new GameRules(null);

            Assert.IsFalse(rules.ShouldAutoSkipTurn(state),
                "Auto-skip is Movement-only; placement is always actionable.");
        }

        [Test]
        public void ShouldAutoSkipTurn_GameOver_ReturnsFalse()
        {
            var state = BuildGame();
            state.GameOver = true;
            var rules = new GameRules(null);

            Assert.IsFalse(rules.ShouldAutoSkipTurn(state));
        }

        [Test]
        public void EndTurn_RunsStateBasedEffectsInternally_ForDefense()
        {
            // EndTurn historically ran SBE internally; the refactor keeps this as a
            // safety net so a caller that forgets to run SBE explicitly doesn't leak
            // a half-settled state into the next turn.
            var state = BuildGame();
            state.Boards[1].SetStack(CENTER, new TokenStack(0, 1, Tone.Dark));

            state.EndTurn();

            Assert.IsTrue(state.Players.First(p => p.Id == 2).IsEliminated);
            Assert.IsTrue(state.GameOver);
        }

        [Test]
        public void EndTurn_OverflowCap_TrimsStackAboveThreeOnEndingPlayerBoard()
        {
            var state = BuildGame();
            // Player 1 is current. Put 5 Light on a space on P1's board.
            state.Boards[0].SetStack(3, new TokenStack(5, 0, Tone.Light));

            var result = state.EndTurn();

            Assert.AreEqual(3, state.Boards[0].GetStack(3).TotalCount,
                "5-counter stack should be trimmed to the 3-counter cap.");
            Assert.AreEqual(1, result.OverflowTrims.Count);
            Assert.AreEqual(2, result.OverflowTrims[0].RemovedCount);
            Assert.AreEqual(Tone.Light, result.OverflowTrims[0].Tone);
            Assert.AreEqual(new SpaceId(0, 3), result.OverflowTrims[0].Space);
        }

        [Test]
        public void EndTurn_OverflowCap_ExactlyThreeIsNotTrimmed()
        {
            var state = BuildGame();
            state.Boards[0].SetStack(3, new TokenStack(3, 0, Tone.Light));

            var result = state.EndTurn();

            Assert.AreEqual(3, state.Boards[0].GetStack(3).TotalCount);
            Assert.AreEqual(0, result.OverflowTrims.Count);
        }

        [Test]
        public void EndTurn_OverflowCap_DoesNotTouchOpponentBoard()
        {
            var state = BuildGame();
            // Oversized stack on Player 2's board; Player 1 ends the turn.
            state.Boards[1].SetStack(5, new TokenStack(0, 6, Tone.Dark));

            var result = state.EndTurn();

            Assert.AreEqual(6, state.Boards[1].GetStack(5).TotalCount,
                "Opponent's overflowing stack must not be trimmed during Player 1's end-of-turn.");
            Assert.AreEqual(0, result.OverflowTrims.Count);
        }

        [Test]
        public void EndTurn_OverflowCap_PreservesBottomLockOnTrim()
        {
            var state = BuildGame();
            // 4-Light stack locked on Light bottom; trimming 1 leaves the lock
            // intact (RemoveOne pops from the top when count > 1).
            state.Boards[0].SetStack(3, new TokenStack(4, 0, Tone.Light));

            state.EndTurn();

            var after = state.Boards[0].GetStack(3);
            Assert.AreEqual(3, after.TotalCount);
            Assert.AreEqual(Tone.Light, after.BottomTone);
        }

        [Test]
        public void EndTurn_OverflowCap_TrimsMultipleSpacesInOnePass()
        {
            var state = BuildGame();
            state.Boards[0].SetStack(3, new TokenStack(4, 0, Tone.Light));
            state.Boards[0].SetStack(5, new TokenStack(5, 0, Tone.Light));

            var result = state.EndTurn();

            Assert.AreEqual(2, result.OverflowTrims.Count);
            Assert.AreEqual(3, state.Boards[0].GetStack(3).TotalCount);
            Assert.AreEqual(3, state.Boards[0].GetStack(5).TotalCount);
            Assert.AreEqual(3, result.OverflowTrims.Sum(t => t.RemovedCount));
        }
    }
}
