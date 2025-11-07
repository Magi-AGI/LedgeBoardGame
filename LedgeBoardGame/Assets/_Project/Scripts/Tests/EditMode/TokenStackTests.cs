using NUnit.Framework;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    [TestFixture]
    public class TokenStackTests
    {
        [Test]
        public void EmptyStack_ShouldHaveNoBottomTone()
        {
            var stack = new TokenStack();
            Assert.IsTrue(stack.IsEmpty);
            Assert.IsFalse(stack.BottomTone.HasValue);
        }

        [Test]
        public void PlaceOnEmpty_ShouldLock()
        {
            var stack = new TokenStack();
            var result = stack.ResolveEntry(Tone.Light, 1);

            Assert.AreEqual(MoveResult.Lock, result);
            Assert.AreEqual(1, stack.LightCount);
            Assert.AreEqual(Tone.Light, stack.BottomTone);
            Assert.IsTrue(stack.IsLocked(Tone.Light));
            Assert.IsFalse(stack.CanMove(Tone.Light));
        }

        [Test]
        public void StackSameTone_ShouldStack()
        {
            var stack = new TokenStack(1, 0, Tone.Light);
            var result = stack.ResolveEntry(Tone.Light, 1);

            Assert.AreEqual(MoveResult.Stack, result);
            Assert.AreEqual(2, stack.LightCount);
            Assert.AreEqual(Tone.Light, stack.BottomTone);
            Assert.IsTrue(stack.IsStack(Tone.Light));
            Assert.IsTrue(stack.CanMove(Tone.Light));
        }

        [Test]
        public void ClearOppositeTone_ShouldClear()
        {
            var stack = new TokenStack(2, 0, Tone.Light);
            var result = stack.ResolveEntry(Tone.Dark, 1);

            Assert.AreEqual(MoveResult.Clear, result);
            Assert.AreEqual(1, stack.LightCount);
            Assert.AreEqual(0, stack.DarkCount);
            Assert.AreEqual(Tone.Light, stack.BottomTone);
            Assert.IsTrue(stack.IsLocked(Tone.Light));
        }

        [Test]
        public void ClearToEmpty_ShouldResetBottomTone()
        {
            var stack = new TokenStack(1, 0, Tone.Light);
            var result = stack.ResolveEntry(Tone.Dark, 1);

            Assert.AreEqual(MoveResult.Clear, result);
            Assert.IsTrue(stack.IsEmpty);
            Assert.IsFalse(stack.BottomTone.HasValue);
        }

        [Test]
        public void BottomToken_CannotMove()
        {
            var stack = new TokenStack(1, 0, Tone.Light);
            Assert.IsFalse(stack.CanMove(Tone.Light));

            stack = new TokenStack(0, 1, Tone.Dark);
            Assert.IsFalse(stack.CanMove(Tone.Dark));
        }

        [Test]
        public void StackedTokens_CanMove()
        {
            var stack = new TokenStack(2, 0, Tone.Light);
            Assert.IsTrue(stack.CanMove(Tone.Light));

            stack = new TokenStack(3, 0, Tone.Light);
            Assert.IsTrue(stack.CanMove(Tone.Light));
        }

        [Test]
        public void MixedStack_AfterClear_MaintainsBottomLock()
        {
            var stack = new TokenStack(3, 0, Tone.Light);
            stack.ResolveEntry(Tone.Dark, 2);

            Assert.AreEqual(1, stack.LightCount);
            Assert.AreEqual(0, stack.DarkCount);
            Assert.AreEqual(Tone.Light, stack.BottomTone);
            Assert.IsTrue(stack.IsLocked(Tone.Light));
            Assert.IsFalse(stack.CanMove(Tone.Light));
        }

        [Test]
        public void RemoveOne_UpdatesCorrectly()
        {
            var stack = new TokenStack(3, 0, Tone.Light);
            stack.RemoveOne(Tone.Light);

            Assert.AreEqual(2, stack.LightCount);
            Assert.AreEqual(Tone.Light, stack.BottomTone);
            Assert.IsTrue(stack.CanMove(Tone.Light));
        }

        [Test]
        public void RemoveLastToken_ClearsBottomTone()
        {
            var stack = new TokenStack(1, 0, Tone.Light);
            stack.RemoveOne(Tone.Light);

            Assert.IsTrue(stack.IsEmpty);
            Assert.IsFalse(stack.BottomTone.HasValue);
        }
    }
}