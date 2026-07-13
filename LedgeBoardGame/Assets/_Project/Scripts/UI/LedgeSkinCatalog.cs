using UnityEngine;

namespace Magi.LedgeBoardGame.UI
{
    /// Resolves a player's board skin to the cool accent color used by the
    /// cross-board visitor overlay (Local/hot-seat mode only - see
    /// GameController.TryShowCrossBoardVisitor). Local player's skin comes
    /// from PlayerPrefs (the lobby owns this); non-local players have no
    /// propagated skin yet, so we fall back to a stable seat-indexed pick
    /// into the skin catalog. When a SetBoardSkin network action ships,
    /// swap the fallback for the propagated value.
    public static class LedgeSkinCatalog
    {
        public const string SkinPrefKey = "ledge.boardSkin";
        public const string DefaultSkinId = "nightfall";

        /// Resolve the skin for a player ID (1-based per Player.Id).
        /// isLocal: true if this is the client's own seat (in network
        /// mode). Local seats read from PlayerPrefs; non-local seats -
        /// including every hot-seat visitor - fall back to the
        /// seat-indexed catalog pick so each seat renders a visually
        /// distinct accent.
        public static LedgeSetupPanel.SkinDef GetSkinForPlayer(int playerId, bool isLocal)
        {
            if (isLocal)
            {
                string id = PlayerPrefs.GetString(SkinPrefKey, DefaultSkinId);
                if (string.IsNullOrEmpty(id)) id = DefaultSkinId;
                var resolved = TryFindSkin(id);
                if (resolved.HasValue) return resolved.Value;
            }
            // Seat-indexed fallback: stable, distinguishable, no network
            // dependency. Player IDs are 1-based; clamp defensively before
            // wrapping into the catalog length.
            int idx = Mathf.Max(0, playerId - 1) % LedgeSetupPanel.DefaultSkins.Length;
            return LedgeSetupPanel.DefaultSkins[idx];
        }

        /// Convenience overload - returns the cool accent color directly.
        public static Color GetAccentForPlayer(int playerId, bool isLocal)
        {
            return GetSkinForPlayer(playerId, isLocal).Accent;
        }

        private static LedgeSetupPanel.SkinDef? TryFindSkin(string id)
        {
            var skins = LedgeSetupPanel.DefaultSkins;
            for (int i = 0; i < skins.Length; i++)
                if (skins[i].Id == id) return skins[i];
            return null;
        }
    }
}
