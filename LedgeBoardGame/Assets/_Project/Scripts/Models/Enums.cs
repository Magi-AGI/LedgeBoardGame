namespace Magi.LedgeBoardGame.Models
{
    public enum Tone
    {
        Light,
        Dark
    }

    public enum SpaceType
    {
        Center,
        InnerBridge,
        InnerStop,
        Ring2,
        Ring3,
        OuterAdded
    }

    public enum GamePhase
    {
        Placement,
        Movement
    }

    public enum MoveResult
    {
        Lock,
        Stack,
        Clear
    }
}