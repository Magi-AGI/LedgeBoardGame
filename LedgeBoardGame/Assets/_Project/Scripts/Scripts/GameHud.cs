using UnityEngine;
using UnityEngine.UI;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame
{
    /// <summary>
    /// Lightweight HUD updater for phase/status labels and end-turn button state.
    /// Attach alongside GameController and assign UI references in inspector.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        [SerializeField] private Text statusText;
        [SerializeField] private Text phaseText;
        [SerializeField] private Text currentPlayerText;
        [SerializeField] private Button endTurnButton;

        public void UpdateHud(GameState state)
        {
            if (state == null) return;

            if (phaseText != null)
            {
                phaseText.text = $"Phase: {state.CurrentPhase}";
            }

            if (currentPlayerText != null)
            {
                var player = state.GetCurrentPlayer();
                currentPlayerText.text = player != null
                    ? $"Player: {player.Name}"
                    : "Player: -";
            }

            if (statusText != null)
            {
                if (state.GameOver)
                {
                    statusText.text = state.WinnerId.HasValue
                        ? $"Game Over - Winner: Player {state.WinnerId.Value}"
                        : "Game Over - No Winner";
                }
                else if (state.CurrentPhase == GamePhase.Placement)
                {
                    statusText.text = "Place one Light and one Dark token.";
                }
                else
                {
                    statusText.text = "Select a movable stack, then a valid destination.";
                }
            }

            if (endTurnButton != null)
            {
                var canEnd =
                    !state.GameOver &&
                    !(state.CurrentPhase == GamePhase.Placement && !state.IsPlacementComplete());

                endTurnButton.interactable = canEnd;
            }
        }
    }
}
