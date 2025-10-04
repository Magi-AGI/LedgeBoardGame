using System;

namespace Magi.LedgeBoardGame.Models
{
    [Serializable]
    public struct SpaceId : IEquatable<SpaceId>
    {
        public int BoardId { get; }
        public int Id { get; }

        public SpaceId(int boardId, int id)
        {
            BoardId = boardId;
            Id = id;
        }

        public bool Equals(SpaceId other)
        {
            return BoardId == other.BoardId && Id == other.Id;
        }

        public override bool Equals(object obj)
        {
            return obj is SpaceId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(BoardId, Id);
        }

        public static bool operator ==(SpaceId left, SpaceId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SpaceId left, SpaceId right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"Space({BoardId}:{Id})";
        }
    }
}