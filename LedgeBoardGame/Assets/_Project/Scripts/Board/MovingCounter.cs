using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Board
{
    /// Canvas-level overlay that plays a move as a multi-chip pantomime: a translucent
    /// phantom stack remains at the source while an opaque stack tweens to the destination,
    /// then both are cleaned up and the onComplete callback fires. GameController drives
    /// this in ExecuteStackMove so picks-up-and-places read as physical, not numeric.
    public class MovingCounter : MonoBehaviour
    {
        private const float ChipSize = 40f;
        private const float StackOffset = 4f;
        private const float PhantomAlpha = 0.35f;

        private float _duration;
        private Vector3 _from;
        private Vector3 _to;
        private GameObject _phantom;
        private Action _onComplete;

        public static MovingCounter Play(Transform canvasParent, Vector3 fromWorld, Vector3 toWorld,
            int lightCount, int darkCount, float duration, Action onComplete, bool withPhantom = true)
        {
            int total = Mathf.Max(1, lightCount + darkCount);
            GameObject phantom = null;
            if (withPhantom)
            {
                phantom = BuildStack(canvasParent, "MovePhantom", lightCount, darkCount, PhantomAlpha);
                phantom.transform.position = fromWorld;
            }

            var flying = BuildStack(canvasParent, "MoveFlying", lightCount, darkCount, 1f);
            flying.transform.position = fromWorld;
            flying.transform.SetAsLastSibling();

            var mc = flying.AddComponent<MovingCounter>();
            mc._from = fromWorld;
            mc._to = toWorld;
            mc._duration = Mathf.Max(0.05f, duration);
            mc._phantom = phantom;
            mc._onComplete = onComplete;
            mc.StartCoroutine(mc.TweenRoutine());
            return mc;
        }

        private static GameObject BuildStack(Transform canvasParent, string name, int lightCount, int darkCount, float alpha)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)root.transform;
            rt.SetParent(canvasParent, worldPositionStays: false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(ChipSize, ChipSize);

            int total = lightCount + darkCount;
            if (total == 0) total = 1;
            float baseY = -(total - 1) * StackOffset * 0.5f;
            int idx = 0;
            // Dark on bottom, light on top — mirrors SpaceView so the flying stack
            // reads identically to the source's rendering.
            for (int d = 0; d < darkCount; d++)
            {
                BuildChip(rt, LedgePalette.CounterDark, alpha, new Vector2(0f, baseY + idx * StackOffset));
                idx++;
            }
            for (int l = 0; l < lightCount; l++)
            {
                BuildChip(rt, LedgePalette.CounterLight, alpha, new Vector2(0f, baseY + idx * StackOffset));
                idx++;
            }
            return root;
        }

        private static void BuildChip(Transform parent, Color color, float alpha, Vector2 anchoredPos)
        {
            var go = new GameObject("Chip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(ChipSize, ChipSize);
            var img = go.GetComponent<Image>();
            img.sprite = LedgeSpriteFactory.Counter;
            img.color = new Color(color.r, color.g, color.b, alpha);
            img.raycastTarget = false;
        }

        private IEnumerator TweenRoutine()
        {
            float elapsed = 0f;
            while (elapsed < _duration)
            {
                float t = elapsed / _duration;
                // Ease-out quad: quick takeoff, soft landing — reads like a chip being placed.
                float eased = 1f - (1f - t) * (1f - t);
                transform.position = Vector3.Lerp(_from, _to, eased);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            transform.position = _to;

            var cb = _onComplete;
            _onComplete = null;
            if (_phantom != null)
            {
                Destroy(_phantom);
                _phantom = null;
            }
            cb?.Invoke();
            Destroy(gameObject);
        }
    }
}
