// 9/29/2025 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using UnityEngine;
using UnityEngine.UI;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Board
{
    public class SpaceView : MonoBehaviour
    {
        // Use regular Unity Text components for now
        // Can be upgraded to TextMeshProUGUI later when TMPro is imported
        [SerializeField] private Text lightCountText;
        [SerializeField] private Text darkCountText;
        [SerializeField] private GameObject lockIndicator;
        [SerializeField] private GameObject highlightEffect;

        private int spaceId;
        private SpaceMeta metadata;

        public void SetData(int id, SpaceMeta meta, TokenStack stack) { }
        public void UpdateTokenDisplay(TokenStack stack) { }
        public void SetHighlight(bool active) { }
    }
}