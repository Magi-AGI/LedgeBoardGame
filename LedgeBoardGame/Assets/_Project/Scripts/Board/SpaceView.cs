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
        [SerializeField] private Image highlightImage;
        [SerializeField] private UnityEngine.Events.UnityEvent<SpaceView> onClicked;

        private int _spaceId;
        private SpaceMeta _metadata;

        public int SpaceId => _spaceId;
        public SpaceMeta Metadata => _metadata;

        public void SetData(int id, SpaceMeta meta, TokenStack stack)
        {
            _spaceId = id;
            _metadata = meta;
            TryCacheHighlightImage();
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
            if (highlightImage != null)
            {
                var color = highlightImage.color;
                color.a = active ? color.a : 0f;
                highlightImage.color = color;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            onClicked?.Invoke(this);
            SpaceClickedEvent.Raise(this);
        }

        public void SetHighlightColor(Color color)
        {
            if (highlightImage != null)
            {
                highlightImage.color = color;
            }
            else
            {
                TryCacheHighlightImage();
                if (highlightImage != null)
                {
                    highlightImage.color = color;
                }
            }
        }

        public void RegisterClickListener(UnityEngine.Events.UnityAction<SpaceView> handler)
        {
            onClicked ??= new UnityEngine.Events.UnityEvent<SpaceView>();
            onClicked.AddListener(handler);
        }

        public void UnregisterClickListener(UnityEngine.Events.UnityAction<SpaceView> handler)
        {
            if (onClicked == null) return;
            onClicked.RemoveListener(handler);
        }

        private void TryCacheHighlightImage()
        {
            if (highlightImage != null && highlightImage.enabled)
                return;

            if (highlightEffect != null && highlightImage == null)
            {
                highlightImage = highlightEffect.GetComponent<Image>();
            }
            if (highlightImage == null)
            {
                highlightImage = GetComponentInChildren<Image>(includeInactive: true);
            }
        }
    }
}
