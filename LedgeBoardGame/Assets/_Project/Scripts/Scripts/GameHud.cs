using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame
{
    /// <summary>
    /// Lightweight HUD updater for phase/status labels and end-turn button state.
    /// Attach alongside GameController and assign UI references in inspector.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        [Header("Text Display (TMP preferred)")]
        [SerializeField] private TextMeshProUGUI statusTextTMP;
        [SerializeField] private TextMeshProUGUI phaseTextTMP;
        [SerializeField] private TextMeshProUGUI currentPlayerTextTMP;

        [Header("Text Display (Legacy fallback)")]
        [SerializeField] private Text statusText;
        [SerializeField] private Text phaseText;
        [SerializeField] private Text currentPlayerText;

        [Header("Controls")]
        [SerializeField] private Button endTurnButton;

        public void UpdateHud(GameState state)
        {
            if (state == null) return;

            // Phase text
            var phaseStr = $"Phase: {state.CurrentPhase}";
            SetText(phaseTextTMP, phaseText, phaseStr);

            // Current player text
            var player = state.GetCurrentPlayer();
            var playerStr = player != null ? $"Player: {player.Name}" : "Player: -";
            SetText(currentPlayerTextTMP, currentPlayerText, playerStr);

            // Status text
            string statusStr;
            if (state.GameOver)
            {
                statusStr = state.WinnerId.HasValue
                    ? $"Game Over - Winner: Player {state.WinnerId.Value}"
                    : "Game Over - No Winner";
            }
            else if (state.CurrentPhase == GamePhase.Placement)
            {
                statusStr = "Place one Light and one Dark token.";
            }
            else
            {
                statusStr = "Select a movable stack, then a valid destination.";
            }
            SetText(statusTextTMP, statusText, statusStr);

            // End turn button
            if (endTurnButton != null)
            {
                var canEnd =
                    !state.GameOver &&
                    !(state.CurrentPhase == GamePhase.Placement && !state.IsPlacementComplete());

                endTurnButton.interactable = canEnd;
            }
        }

        private void SetText(TextMeshProUGUI tmpText, Text legacyText, string value)
        {
            if (tmpText != null)
            {
                tmpText.text = value;
            }
            else if (legacyText != null)
            {
                legacyText.text = value;
            }
        }
    }
}
