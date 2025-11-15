// 9/29/2025 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Board
{
    public class SpaceView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Text lightCountText;
        [SerializeField] private Text darkCountText;
        [SerializeField] private GameObject lockIndicator;
        [SerializeField] private GameObject highlightEffect;

        private int _spaceId;
        private SpaceMeta _metadata;

        public int SpaceId => _spaceId;
        public SpaceMeta Metadata => _metadata;

        public void SetData(int id, SpaceMeta meta, TokenStack stack)
        {
            _spaceId = id;
            _metadata = meta;
            UpdateTokenDisplay(stack);
            SetHighlight(false);
        }

        public void UpdateTokenDisplay(TokenStack stack)
        {
            if (lightCountText != null)
            {
                lightCountText.text = stack.LightCount.ToString();
            }

            if (darkCountText != null)
            {
                darkCountText.text = stack.DarkCount.ToString();
            }

            if (lockIndicator != null)
            {
                // Locked if either tone is locked at this space
                var isLocked = stack.IsLocked(Tone.Light) || stack.IsLocked(Tone.Dark);
                lockIndicator.SetActive(isLocked);
            }
        }

        public void SetHighlight(bool active)
        {
            if (highlightEffect != null)
            {
                highlightEffect.SetActive(active);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            SpaceClickedEvent.Raise(this);
        }
    }
}
