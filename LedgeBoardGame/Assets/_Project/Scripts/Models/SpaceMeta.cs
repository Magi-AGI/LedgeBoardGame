namespace Magi.LedgeBoardGame.Models
{
    [System.Serializable]
    public struct SpaceMeta
    {
        public SpaceType Type { get; }
        public int RingIndex { get; }
        public int WedgeIndex { get; }
        public bool IsHalf { get; }
        public string ColorLabel { get; }

        public SpaceMeta(SpaceType type, int ringIndex, int wedgeIndex, bool isHalf = false, string colorLabel = null)
        {
            Type = type;
            RingIndex = ringIndex;
            WedgeIndex = wedgeIndex;
            IsHalf = isHalf;
            ColorLabel = colorLabel;
        }
    }
}
