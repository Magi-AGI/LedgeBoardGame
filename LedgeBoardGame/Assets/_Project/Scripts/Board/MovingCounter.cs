using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Board
{
    /// Canvas-level overlay that plays a move as a multi-counter pantomime: a translucent
    /// phantom stack remains at the source while an opaque stack tweens to the destination,
    /// then both are cleaned up and the onComplete callback fires. GameController drives
    /// this in ExecuteStackMove so picks-up-and-places read as physical, not numeric.
    public class MovingCounter : MonoBehaviour
    {
        private const float CounterSize = 60f;
        private const float StackOffset = 5f;
        private const float PhantomAlpha = 0.35f;

        private float _duration;
        private Vector3 _from;
        private Vector3 _to;
        private GameObject _phantom;
        private Action _onComplete;
        private List<Vector3> _waypoints;
        private List<(int light, int dark)> _waypointStacks;
        private Transform _canvasParent;

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

        /// Plays a chained multi-hop animation: the flying stack tweens from the first
        /// waypoint through each subsequent one, with `perHopDuration` spent on each
        /// segment. Used by multi-step reach moves so the stack visibly hops through
        /// intermediate spaces instead of teleporting or restarting between hops.
        /// `waypoints` must contain at least two entries (start + one destination).
        public static MovingCounter PlayPath(Transform canvasParent, List<Vector3> waypoints,
            int lightCount, int darkCount, float perHopDuration, Action onComplete)
        {
            if (waypoints == null || waypoints.Count < 2)
                return Play(canvasParent, Vector3.zero, Vector3.zero, lightCount, darkCount,
                    perHopDuration, onComplete, withPhantom: false);

            var flying = BuildStack(canvasParent, "MoveFlying", lightCount, darkCount, 1f);
            flying.transform.position = waypoints[0];
            flying.transform.SetAsLastSibling();

            var mc = flying.AddComponent<MovingCounter>();
            mc._waypoints = new List<Vector3>(waypoints);
            mc._duration = Mathf.Max(0.05f, perHopDuration);
            mc._onComplete = onComplete;
            mc._canvasParent = canvasParent;
            mc.StartCoroutine(mc.PathRoutine());
            return mc;
        }

        /// Variant that rebuilds the flying stack at each waypoint to reflect pickups
        /// (same-tone siphons grow the carried stack) or clashes (opposite-tone losses
        /// shrink it). `waypointStacks[i]` is the (light, dark) size at `waypoints[i]`:
        /// waypointStacks[0] is the initial liftoff size, and each subsequent entry is
        /// what the stack looks like upon landing at that waypoint. If the collection is
        /// null or wrong length, the stack size stays fixed at the initial value.
        public static MovingCounter PlayPath(Transform canvasParent, List<Vector3> waypoints,
            List<(int light, int dark)> waypointStacks, float perHopDuration, Action onComplete)
        {
            if (waypoints == null || waypoints.Count < 2)
                return Play(canvasParent, Vector3.zero, Vector3.zero, 0, 0,
                    perHopDuration, onComplete, withPhantom: false);

            var initial = (waypointStacks != null && waypointStacks.Count > 0)
                ? waypointStacks[0]
                : (light: 0, dark: 0);

            var flying = BuildStack(canvasParent, "MoveFlying", initial.light, initial.dark, 1f);
            flying.transform.position = waypoints[0];
            flying.transform.SetAsLastSibling();

            var mc = flying.AddComponent<MovingCounter>();
            mc._waypoints = new List<Vector3>(waypoints);
            mc._waypointStacks = waypointStacks != null ? new List<(int, int)>(waypointStacks) : null;
            mc._duration = Mathf.Max(0.05f, perHopDuration);
            mc._onComplete = onComplete;
            mc._canvasParent = canvasParent;
            mc.StartCoroutine(mc.PathRoutine());
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
            rt.sizeDelta = new Vector2(CounterSize, CounterSize);

            int total = lightCount + darkCount;
            if (total == 0) total = 1;
            float baseY = -(total - 1) * StackOffset * 0.5f;
            int idx = 0;
            // Dark on bottom, light on top — mirrors SpaceView so the flying stack
            // reads identically to the source's rendering.
            for (int d = 0; d < darkCount; d++)
            {
                BuildCounter(rt, LedgePalette.CounterDark, alpha, new Vector2(0f, baseY + idx * StackOffset));
                idx++;
            }
            for (int l = 0; l < lightCount; l++)
            {
                BuildCounter(rt, LedgePalette.CounterLight, alpha, new Vector2(0f, baseY + idx * StackOffset));
                idx++;
            }
            return root;
        }

        private static void BuildCounter(Transform parent, Color color, float alpha, Vector2 anchoredPos)
        {
            var go = new GameObject("Counter", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(CounterSize, CounterSize);
            var img = go.GetComponent<Image>();
            img.sprite = LedgeSpriteFactory.Counter;
            img.color = new Color(color.r, color.g, color.b, alpha);
            img.raycastTarget = false;

            // Rim uses the opposite tone so stacked dark counters stay countable.
            bool isDark = Mathf.Approximately(color.r, LedgePalette.CounterDark.r)
                && Mathf.Approximately(color.g, LedgePalette.CounterDark.g)
                && Mathf.Approximately(color.b, LedgePalette.CounterDark.b);
            var rimColor = isDark ? LedgePalette.CounterLight : LedgePalette.CounterDark;

            var rimGo = new GameObject("Rim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rimRt = (RectTransform)rimGo.transform;
            rimRt.SetParent(rt, false);
            rimRt.anchorMin = new Vector2(0.5f, 0.5f);
            rimRt.anchorMax = new Vector2(0.5f, 0.5f);
            rimRt.pivot = new Vector2(0.5f, 0.5f);
            rimRt.anchoredPosition = Vector2.zero;
            rimRt.sizeDelta = new Vector2(CounterSize, CounterSize);
            var rimImg = rimGo.GetComponent<Image>();
            rimImg.sprite = LedgeSpriteFactory.CounterRim;
            rimImg.color = new Color(rimColor.r, rimColor.g, rimColor.b, alpha);
            rimImg.raycastTarget = false;
        }

        private IEnumerator TweenRoutine()
        {
            float elapsed = 0f;
            while (elapsed < _duration)
            {
                float t = elapsed / _duration;
                // Ease-out quad: quick takeoff, soft landing — reads like a counter being placed.
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

        private IEnumerator PathRoutine()
        {
            // Linear ease across the full path so intermediate hops don't stutter —
            // the per-hop ease-out would decelerate into each waypoint and re-accelerate,
            // producing a pulsing rhythm that obscures the hop cadence. Linear keeps
            // the stack moving steadily, and the snap-to-waypoint at each boundary
            // reads as the hop landing.
            for (int i = 0; i < _waypoints.Count - 1; i++)
            {
                var segFrom = _waypoints[i];
                var segTo = _waypoints[i + 1];
                float elapsed = 0f;
                while (elapsed < _duration)
                {
                    float t = elapsed / _duration;
                    transform.position = Vector3.Lerp(segFrom, segTo, t);
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
                transform.position = segTo;

                // On arrival at an intermediate waypoint, rebuild the visual stack to
                // reflect pickups/losses at that hop. Skip the final waypoint — it's
                // destroyed at cleanup anyway, and the landed SpaceView will show the
                // final count.
                int landingIndex = i + 1;
                if (_waypointStacks != null
                    && landingIndex < _waypointStacks.Count
                    && landingIndex < _waypoints.Count - 1)
                {
                    RebuildVisualStack(_waypointStacks[landingIndex]);
                }
            }

            var cb = _onComplete;
            _onComplete = null;
            cb?.Invoke();
            Destroy(gameObject);
        }

        private void RebuildVisualStack((int light, int dark) size)
        {
            var rt = (RectTransform)transform;
            for (int i = rt.childCount - 1; i >= 0; i--)
            {
                Destroy(rt.GetChild(i).gameObject);
            }

            int total = size.light + size.dark;
            if (total == 0) total = 1;
            float baseY = -(total - 1) * StackOffset * 0.5f;
            int idx = 0;
            for (int d = 0; d < size.dark; d++)
            {
                BuildCounter(rt, LedgePalette.CounterDark, 1f, new Vector2(0f, baseY + idx * StackOffset));
                idx++;
            }
            for (int l = 0; l < size.light; l++)
            {
                BuildCounter(rt, LedgePalette.CounterLight, 1f, new Vector2(0f, baseY + idx * StackOffset));
                idx++;
            }
        }
    }
}
