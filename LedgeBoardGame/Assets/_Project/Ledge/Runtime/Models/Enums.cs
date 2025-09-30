namespace Magi.LedgeBoardGame.Runtime.Models
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
        OuterAdded,
        Ledge
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