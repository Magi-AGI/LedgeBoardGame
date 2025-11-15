using System;

namespace Magi.LedgeBoardGame.Models
{
    [Serializable]
    public class TokenStack
    {
        public int LightCount { get; set; }
        public int DarkCount { get; set; }
        public Tone? BottomTone { get; set; }

        public TokenStack() { }

        public TokenStack(int lightCount, int darkCount, Tone? bottomTone = null)
        {
            LightCount = lightCount;
            DarkCount = darkCount;
            BottomTone = bottomTone;
        }

        public bool IsEmpty => LightCount == 0 && DarkCount == 0;

        public bool HasTokens => !IsEmpty;

        public int TotalCount => LightCount + DarkCount;

        public int GetCount(Tone tone)
        {
            return tone == Tone.Light ? LightCount : DarkCount;
        }

        public void SetCount(Tone tone, int count)
        {
            if (tone == Tone.Light)
                LightCount = count;
            else
                DarkCount = count;
        }

        public bool IsLocked(Tone tone)
        {
            if (IsEmpty) return false;
            if (!BottomTone.HasValue) return false;

            return BottomTone.Value == tone && GetCount(tone) == 1;
        }

        public bool IsStack(Tone tone)
        {
            return GetCount(tone) >= 2;
        }

        public bool CanMove(Tone tone)
        {
            if (IsEmpty) return false;
            if (GetCount(tone) == 0) return false;

            if (!BottomTone.HasValue) return true;

            if (BottomTone.Value == tone)
            {
                return GetCount(tone) > 1;
            }

            return true;
        }

        public MoveResult ResolveEntry(Tone enteringTone, int enteringCount = 1)
        {
            if (IsEmpty)
            {
                SetCount(enteringTone, enteringCount);
                BottomTone = enteringTone;
                return MoveResult.Lock;
            }

            var opposingTone = enteringTone == Tone.Light ? Tone.Dark : Tone.Light;
            var opposingCount = GetCount(opposingTone);

            if (opposingCount > 0)
            {
                var clearAmount = Math.Min(enteringCount, opposingCount);
                SetCount(opposingTone, opposingCount - clearAmount);
                var remainingEntering = enteringCount - clearAmount;

                if (remainingEntering > 0)
                {
                    SetCount(enteringTone, GetCount(enteringTone) + remainingEntering);
                }

                if (IsEmpty)
                {
                    BottomTone = null;
                }
                else if (GetCount(opposingTone) == 0 && GetCount(enteringTone) > 0)
                {
                    BottomTone = enteringTone;
                }

                return MoveResult.Clear;
            }
            else
            {
                SetCount(enteringTone, GetCount(enteringTone) + enteringCount);

                if (!BottomTone.HasValue)
                {
                    BottomTone = enteringTone;
                }

                return MoveResult.Stack;
            }
        }

        public void RemoveOne(Tone tone)
        {
            var count = GetCount(tone);
            if (count > 0)
            {
                SetCount(tone, count - 1);

                if (IsEmpty)
                {
                    BottomTone = null;
                }
                else if (count == 1 && BottomTone == tone)
                {
                    var opposingTone = tone == Tone.Light ? Tone.Dark : Tone.Light;
                    if (GetCount(opposingTone) > 0)
                    {
                        BottomTone = opposingTone;
                    }
                    else
                    {
                        BottomTone = null;
                    }
                }
            }
        }

        public TokenStack Clone()
        {
            return new TokenStack(LightCount, DarkCount, BottomTone);
        }

        public override string ToString()
        {
            if (IsEmpty) return "Empty";
            var parts = new System.Collections.Generic.List<string>();
            if (LightCount > 0) parts.Add($"L:{LightCount}");
            if (DarkCount > 0) parts.Add($"D:{DarkCount}");
            if (BottomTone.HasValue) parts.Add($"[{BottomTone.Value}]");
            return string.Join(" ", parts);
        }
    }
}