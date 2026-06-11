using System.Collections.Generic;
using NUnit.Framework;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    // P2 of the JIP/LIP rollout: disconnected (IsConnected=false) seats must
    // be skipped in turn rotation, and the win condition must stay gated on
    // elimination rather than connectivity. A transient drop must never
    // hand the game to whoever stayed connected.
    [TestFixture]
    public class TurnRotationJipTests
    {
        private static GameState Build(int seatCount, params int[] disconnectedIds)
        {
            var players = new List<Player>();
            for (int i = 1; i <= seatCount; i++)
            {
                var p = new Player(i, "Player" + i, i - 1);
                foreach (var d in disconnectedIds)
                    if (d == i) p.IsConnected = false;
                players.Add(p);
            }
            return new GameState(players, null);
        }

        [Test]
        public void AdvanceToNextPlayer_SkipsDisconnectedSeat()
        {
            var gs = Build(4, disconnectedIds: 3);
            gs.CurrentPlayerId = 2;
            gs.EndTurn();
            Assert.AreEqual(4, gs.CurrentPlayerId, "P3 disconnected — rotation should jump to P4.");
        }

        [Test]
        public void AdvanceToNextPlayer_SkipsMultipleContiguousDisconnects()
        {
            var gs = Build(5, disconnectedIds: new[] { 3, 4 });
            gs.CurrentPlayerId = 2;
            gs.EndTurn();
            Assert.AreEqual(5, gs.CurrentPlayerId, "P3 and P4 disconnected — rotation should jump to P5.");
        }

        [Test]
        public void AdvanceToNextPlayer_WrapsPastDisconnectedTrailingSeats()
        {
            var gs = Build(4, disconnectedIds: 4);
            gs.CurrentPlayerId = 3;
            gs.EndTurn();
            Assert.AreEqual(1, gs.CurrentPlayerId, "P4 disconnected — wrap should land on P1.");
        }

        [Test]
        public void AdvanceToNextPlayer_CurrentDisconnectedMidTurn_RotatesToNextId()
        {
            // Server-side LIP (P5) will eventually force-end a turn on behalf
            // of a seat that dropped while current. Validate the rotation
            // primitive handles that directly: current id isn't in the
            // active list, so rotation must still pick the next id in order.
            var gs = Build(4);
            gs.CurrentPlayerId = 3;
            gs.Players[2].IsConnected = false;
            gs.EndTurn();
            Assert.AreEqual(4, gs.CurrentPlayerId, "Current (P3) just disconnected — next id in rotation is P4.");
        }

        [Test]
        public void AdvanceToNextPlayer_CurrentDisconnected_WrapsWhenNoHigherIdConnected()
        {
            var gs = Build(4, disconnectedIds: 4);
            gs.CurrentPlayerId = 3;
            gs.Players[2].IsConnected = false;
            gs.EndTurn();
            Assert.AreEqual(1, gs.CurrentPlayerId, "P3 (current) just disconnected and P4 also absent — wrap to P1.");
        }

        [Test]
        public void AdvanceToNextPlayer_AllRemoteSeatsDisconnected_SticksOnLastConnectedPlayer()
        {
            // With only P1 connected, EndTurn on P1 cycles back to P1.
            // The game is not won (no one is eliminated), just stalled
            // until someone rejoins.
            var gs = Build(4, disconnectedIds: new[] { 2, 3, 4 });
            gs.CurrentPlayerId = 1;
            var result = gs.EndTurn();
            Assert.IsFalse(gs.GameOver, "Disconnection is not elimination — game must not end.");
            Assert.IsFalse(result.GameEnded);
            Assert.AreEqual(1, gs.CurrentPlayerId, "Only P1 is active — rotation cycles back to P1.");
        }

        [Test]
        public void ApplyStateBasedEffects_DisconnectedSeats_DoNotTriggerWin()
        {
            var gs = Build(4, disconnectedIds: new[] { 2, 3, 4 });
            gs.ApplyStateBasedEffects();
            Assert.IsFalse(gs.GameOver, "Only P1 is connected, but nobody is eliminated — game stays in progress.");
            Assert.IsNull(gs.WinnerId);
        }

        [Test]
        public void ApplyStateBasedEffects_LastNonEliminated_WinsEvenIfDisconnected()
        {
            // P1 gets dark-locked offscreen — eliminated. P2 and P3 are
            // disconnected but not eliminated. P4 is the last survivor and
            // wins, even though not everyone is present to witness it.
            var gs = Build(4, disconnectedIds: new[] { 2, 3 });
            gs.Players[0].IsEliminated = true;  // P1 eliminated
            gs.Players[2].IsEliminated = true;  // P3 eliminated
            gs.ApplyStateBasedEffects();

            // notEliminated = {P2 (disconnected), P4 (connected)} → two still standing, no winner yet.
            Assert.IsFalse(gs.GameOver, "P2 is disconnected but not eliminated — still blocks the win.");

            // Now P2 also eliminates: P4 is last non-eliminated and wins.
            gs.Players[1].IsEliminated = true;
            gs.ApplyStateBasedEffects();
            Assert.IsTrue(gs.GameOver);
            Assert.AreEqual(4, gs.WinnerId, "Last non-eliminated seat wins regardless of its connectivity.");
        }

        [Test]
        public void LocalMode_DefaultRoster_AllConnected_RotatesNormally()
        {
            // Hot-seat regression guard: Local callers use BuildDefaultRoster(n)
            // with no explicit connectivity — everyone should stay present so
            // rotation behaves exactly as before P1 landed.
            var players = Player.BuildDefaultRoster(4);
            var gs = new GameState(players, null);
            gs.CurrentPlayerId = 1;

            gs.EndTurn();
            Assert.AreEqual(2, gs.CurrentPlayerId);
            gs.EndTurn();
            Assert.AreEqual(3, gs.CurrentPlayerId);
            gs.EndTurn();
            Assert.AreEqual(4, gs.CurrentPlayerId);
            gs.EndTurn();
            Assert.AreEqual(1, gs.CurrentPlayerId, "Full rotation must wrap back to P1.");
        }
    }
}
