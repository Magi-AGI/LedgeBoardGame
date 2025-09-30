using System;

namespace Magi.LedgeBoardGame.Runtime.Models
{
    [Serializable]
    public class Move
    {
        public SpaceId From { get; set; }
        public SpaceId To { get; set; }
        public Tone Tone { get; set; }
        public int Count { get; set; }
        public MoveResult? Result { get; set; }

        public Move()
        {
            Count = 1;
        }

        public Move(SpaceId from, SpaceId to, Tone tone, int count = 1)
        {
            From = from;
            To = to;
            Tone = tone;
            Count = count;
        }

        public override string ToString()
        {
            var result = Result.HasValue ? $" -> {Result.Value}" : "";
            return $"Move {Tone}x{Count} from {From} to {To}{result}";
        }
    }

    [Serializable]
    public class PlacementMove
    {
        public SpaceId Target { get; set; }
        public Tone Tone { get; set; }
        public int Count { get; set; }
        public MoveResult? Result { get; set; }

        public PlacementMove()
        {
            Count = 1;
        }

        public PlacementMove(SpaceId target, Tone tone, int count = 1)
        {
            Target = target;
            Tone = tone;
            Count = count;
        }

        public override string ToString()
        {
            var result = Result.HasValue ? $" -> {Result.Value}" : "";
            return $"Place {Tone}x{Count} at {Target}{result}";
        }
    }
}