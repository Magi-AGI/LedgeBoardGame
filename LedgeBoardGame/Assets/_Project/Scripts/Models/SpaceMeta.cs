namespace Magi.LedgeBoardGame.Models
{
    [System.Serializable]
    public class SpaceMeta
    {
        public SpaceType Type { get; set; }
        public int RingIndex { get; set; }
        public int WedgeIndex { get; set; }
        public bool IsHalf { get; set; }
        public string ColorLabel { get; set; }

        public SpaceMeta() { }

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