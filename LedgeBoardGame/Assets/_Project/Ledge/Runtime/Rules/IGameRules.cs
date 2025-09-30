using System.Collections.Generic;
using Magi.LedgeBoardGame.Runtime.Models;

namespace Magi.LedgeBoardGame.Runtime.Rules
{
    public interface IGameRules
    {
        bool CanPlaceToken(GameState gameState, SpaceId target, Tone tone);
        PlacementMove PlaceToken(GameState gameState, SpaceId target, Tone tone);

        bool CanMoveToken(GameState gameState, SpaceId from, SpaceId to, Tone tone);
        Move MoveToken(GameState gameState, SpaceId from, SpaceId to, Tone tone);

        List<SpaceId> GetValidMoveTargets(GameState gameState, SpaceId from, Tone tone);
        List<SpaceId> GetMovablePieces(GameState gameState, int playerId);
        List<SpaceId> GetValidPlacementTargets(GameState gameState, int playerId);

        bool IsGameOver(GameState gameState);
        int? GetWinner(GameState gameState);
    }
}