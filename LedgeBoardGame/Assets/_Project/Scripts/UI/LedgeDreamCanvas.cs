using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.UI
{
    /// Fullscreen Dream Glass backdrop — the centerpiece of the kit's visual
    /// language (kit/ledge-board-game/project/ledge-dream-treatment.js).
    /// Composed in C# as a stack of UI Images:
    ///
    ///   1. Solid deep night-sky (#0A0D1B = LedgeUITokens.Canvas).
    ///   2. Subtle vertical gradient.
    ///   3. Procedural seeded starfield.
    ///   4. Per-board halo group (corona ring + core moon-halo) — one per
    ///      registered board, all visible simultaneously. The "active" board
    ///      glows at full alpha with a warm tint, the rest sit at a reduced
    ///      cool tint. Active changes lerp over <see cref="fadeDuration"/>.
    ///
    /// Spawn as the FIRST sibling of the gameplay Canvas so it renders behind
    /// boards and HUD chrome. Use <see cref="EnsureBoardHalo"/> to register a
    /// board, <see cref="SetActiveBoard"/> to indicate the current turn.
    [RequireComponent(typeof(RectTransform))]
    public class LedgeDreamCanvas : MonoBehaviour
    {
        [Tooltip("Stars per million pixels of canvas area. ~100 reads as 'visible but not busy'.")]
        public float starDensity = 100f;
        [Tooltip("Deterministic seed for star placement so reloads are stable.")]
        public int starSeed = 1337;

        [Header("Halo behaviour")]
        [Tooltip("Alpha multiplier of the corona+halo for the active board.")]
        [Range(0f, 1f)] public float activeAlpha = 0.55f;
        [Tooltip("Alpha multiplier of the corona+halo for inactive boards.")]
        [Range(0f, 1f)] public float inactiveAlpha = 0.18f;
        [Tooltip("Seconds to fade between active/inactive states.")]
        public float fadeDuration = 0.55f;

        // ── Layers ────────────────────────────────────────────────────────────
        private Image _solid;
        private Image _gradient;
        private Image _stars;
        private RectTransform _haloLayer; // contains all per-board halos, painted under chrome but above starfield

        private Sprite _gradientSprite;
        private Sprite _starsSprite;
        private Sprite _coronaRingSprite;
        private Sprite _haloFalloffSprite;

        private class BoardHalo
        {
            public RectTransform Target;
            public Image Corona;
            public Image CoreHalo;
            // Multipliers of the board's CURRENT rendered min(width,height).
            // The halo size is recomputed each frame so it scales with pan/zoom.
            public float CoronaMul;
            public float HaloMul;
            public Color Tint;        // current (lerped)
            public Color TargetTint;  // lerped toward
            public float Alpha;       // current (lerped)
            public float TargetAlpha; // lerped toward
        }

        private readonly Dictionary<RectTransform, BoardHalo> _haloes = new Dictionary<RectTransform, BoardHalo>();
        private RectTransform _activeBoard;

        private void Awake() => EnsureBuilt();

        public void EnsureBuilt()
        {
            if (_solid != null) return;

            var rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _solid = AddChild(transform, "Solid", LedgeUITokens.Canvas, raycast: false);

            _gradient = AddChild(transform, "Gradient", Color.white, raycast: false);
            _gradientSprite = MakeVerticalGradient(64,
                top:    new Color(LedgeUITokens.Canvas2.r, LedgeUITokens.Canvas2.g, LedgeUITokens.Canvas2.b, 1f),
                bottom: new Color(LedgeUITokens.Canvas.r,  LedgeUITokens.Canvas.g,  LedgeUITokens.Canvas.b,  1f));
            _gradient.sprite = _gradientSprite;

            _stars = AddChild(transform, "Stars", Color.white, raycast: false);
            RebuildStarfield();

            // Halo layer — children are positioned absolutely within the canvas frame.
            var haloGo = new GameObject("Haloes", typeof(RectTransform));
            _haloLayer = (RectTransform)haloGo.transform;
            _haloLayer.SetParent(transform, false);
            _haloLayer.anchorMin = Vector2.zero;
            _haloLayer.anchorMax = Vector2.one;
            _haloLayer.offsetMin = Vector2.zero;
            _haloLayer.offsetMax = Vector2.zero;

            _coronaRingSprite = MakeRadialRing(256,
                innerStop: 0.30f, peakStop: 0.55f, outerStop: 1.0f,
                peak: new Color(1, 1, 1, 0.55f), fade: new Color(0, 0, 0, 0));
            _haloFalloffSprite = MakeRadialFalloff(192, peak: new Color(1, 1, 1, 0.55f), fade: new Color(0, 0, 0, 0));
        }

        // ── Halo registration ────────────────────────────────────────────────
        /// Register or update a board's halo. coronaMul / haloMul are
        /// multipliers of the board's CURRENT rendered min-dimension; the
        /// halo's pixel size is recomputed each frame so it scales naturally
        /// with the per-board zoom (mouse-wheel inside a hovered board).
        public void EnsureBoardHalo(RectTransform boardRect, float coronaMul, float haloMul, Color tint)
        {
            EnsureBuilt();
            if (boardRect == null) return;
            if (_haloes.TryGetValue(boardRect, out var existing))
            {
                existing.CoronaMul  = coronaMul;
                existing.HaloMul    = haloMul;
                existing.TargetTint = tint;
                // Don't snap CurrentTint — let it lerp.
                ApplyTint(existing);
                return;
            }

            var halo = new BoardHalo
            {
                Target = boardRect,
                CoronaMul = coronaMul,
                HaloMul = haloMul,
                Tint = tint,
                TargetTint = tint,
                Alpha = inactiveAlpha,
                TargetAlpha = inactiveAlpha,
            };

            halo.Corona = AddChild(_haloLayer, $"Corona_{boardRect.name}", Color.white, raycast: false);
            halo.Corona.sprite = _coronaRingSprite;
            halo.Corona.preserveAspect = true;
            halo.Corona.rectTransform.anchorMin = halo.Corona.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            halo.Corona.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            halo.CoreHalo = AddChild(_haloLayer, $"CoreHalo_{boardRect.name}", Color.white, raycast: false);
            halo.CoreHalo.sprite = _haloFalloffSprite;
            halo.CoreHalo.preserveAspect = true;
            halo.CoreHalo.rectTransform.anchorMin = halo.CoreHalo.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            halo.CoreHalo.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            // Corona below halo so the inner halo punches through.
            halo.Corona.transform.SetSiblingIndex(0);
            halo.CoreHalo.transform.SetAsLastSibling();

            ApplyTint(halo);
            _haloes[boardRect] = halo;
        }

        public void SetActiveBoard(RectTransform boardRect)
        {
            EnsureBuilt();
            _activeBoard = boardRect;
            foreach (var kv in _haloes)
            {
                bool active = (kv.Key == boardRect);
                kv.Value.TargetAlpha = active ? activeAlpha : inactiveAlpha;
                // All halos cool (AccentCool); only alpha differs between
                // active and inactive. The earlier warm/cool split read as
                // too aggressive — single hue with intensity change instead.
                kv.Value.TargetTint  = LedgeUITokens.AccentCool;
            }
        }

        public void RemoveBoardHalo(RectTransform boardRect)
        {
            if (boardRect == null) return;
            if (_haloes.TryGetValue(boardRect, out var halo))
            {
                if (halo.Corona   != null) Destroy(halo.Corona.gameObject);
                if (halo.CoreHalo != null) Destroy(halo.CoreHalo.gameObject);
                _haloes.Remove(boardRect);
            }
        }

        private static void ApplyTint(BoardHalo h)
        {
            if (h.Corona != null)
                h.Corona.color = new Color(h.Tint.r, h.Tint.g, h.Tint.b, h.Alpha);
            if (h.CoreHalo != null)
                // Core halo follows the corona's tint (a slightly lighter cast)
                // at half alpha so it reads as a soft inner punch rather than a
                // bright white spot.
                h.CoreHalo.color = new Color(
                    Mathf.Lerp(h.Tint.r, 1f, 0.35f),
                    Mathf.Lerp(h.Tint.g, 1f, 0.35f),
                    Mathf.Lerp(h.Tint.b, 1f, 0.35f),
                    h.Alpha * 0.5f);
        }

        private void Update()
        {
            if (_haloes.Count == 0) return;
            float step = (fadeDuration <= 0f) ? 1f : Time.unscaledDeltaTime / fadeDuration;

            foreach (var kv in _haloes)
            {
                var h = kv.Value;
                if (h.Target == null || h.Corona == null || h.CoreHalo == null) continue;

                bool changed = false;
                // Alpha lerp.
                if (!Mathf.Approximately(h.Alpha, h.TargetAlpha))
                {
                    h.Alpha = Mathf.MoveTowards(h.Alpha, h.TargetAlpha, step);
                    changed = true;
                }
                // Tint lerp — RGB space, fadeDuration normalised.
                if (h.Tint != h.TargetTint)
                {
                    h.Tint = new Color(
                        Mathf.MoveTowards(h.Tint.r, h.TargetTint.r, step),
                        Mathf.MoveTowards(h.Tint.g, h.TargetTint.g, step),
                        Mathf.MoveTowards(h.Tint.b, h.TargetTint.b, step),
                        Mathf.MoveTowards(h.Tint.a, h.TargetTint.a, step));
                    changed = true;
                }
                if (changed) ApplyTint(h);

                FollowBoard(h);
            }
        }

        private static readonly Vector3[] _cornerScratch = new Vector3[4];

        private void FollowBoard(BoardHalo h)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var canvasRt = canvas.transform as RectTransform;
            if (canvasRt == null) return;

            // Read the board's four world-space corners and project to screen
            // pixels. This captures any parent zoom/pan applied by ScrollRect
            // and the per-board mouse-wheel zoom, so the halo always tracks
            // the board's actual rendered footprint.
            h.Target.GetWorldCorners(_cornerScratch);
            Vector2 blScreen = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, _cornerScratch[0]);
            Vector2 trScreen = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, _cornerScratch[2]);

            float widthPx  = Mathf.Abs(trScreen.x - blScreen.x);
            float heightPx = Mathf.Abs(trScreen.y - blScreen.y);
            float baseSize = Mathf.Min(widthPx, heightPx);
            if (baseSize < 1f) return;

            Vector2 centerScreen = (blScreen + trScreen) * 0.5f;
            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRt, centerScreen, canvas.worldCamera, out localPoint);

            float coronaSize = baseSize * h.CoronaMul * 2f;
            float haloSize   = baseSize * h.HaloMul   * 2f;

            h.Corona.rectTransform.anchoredPosition = localPoint;
            h.CoreHalo.rectTransform.anchoredPosition = localPoint;
            h.Corona.rectTransform.sizeDelta   = new Vector2(coronaSize, coronaSize);
            h.CoreHalo.rectTransform.sizeDelta = new Vector2(haloSize,   haloSize);
        }

        // ── Starfield ────────────────────────────────────────────────────────
        public void RebuildStarfield()
        {
            EnsureBuilt();
            const int W = 1280, H = 720;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);

            int starCount = Mathf.RoundToInt(W * H / 1_000_000f * starDensity);
            var rng = new System.Random(starSeed);

            for (int s = 0; s < starCount; s++)
            {
                int x = rng.Next(W);
                int y = rng.Next(H);
                float bright = (float)System.Math.Pow(rng.NextDouble(), 3.0);
                byte a = (byte)(bright * 220);
                byte r = (byte)(220 + rng.Next(36));
                byte g = (byte)(220 + rng.Next(36));
                byte b = (byte)(220 + rng.Next(36));
                if (rng.NextDouble() < 0.10) { r = (byte)(180 + rng.Next(40)); g = (byte)(190 + rng.Next(40)); b = 255; }
                else if (rng.NextDouble() < 0.05) { r = 255; g = (byte)(220 + rng.Next(35)); b = (byte)(150 + rng.Next(60)); }

                Plot(px, W, H, x, y, new Color32(r, g, b, a));
                if (bright > 0.7f)
                {
                    byte ha = (byte)(a / 5);
                    Plot(px, W, H, x - 1, y, new Color32(r, g, b, ha));
                    Plot(px, W, H, x + 1, y, new Color32(r, g, b, ha));
                    Plot(px, W, H, x, y - 1, new Color32(r, g, b, ha));
                    Plot(px, W, H, x, y + 1, new Color32(r, g, b, ha));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _starsSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _stars.sprite = _starsSprite;
            _stars.preserveAspect = false;
        }

        private static void Plot(Color32[] px, int W, int H, int x, int y, Color32 c)
        {
            if (x < 0 || x >= W || y < 0 || y >= H) return;
            int idx = y * W + x;
            var cur = px[idx];
            px[idx] = new Color32(
                (byte)Mathf.Min(255, cur.r + c.r),
                (byte)Mathf.Min(255, cur.g + c.g),
                (byte)Mathf.Min(255, cur.b + c.b),
                (byte)Mathf.Min(255, cur.a + c.a));
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private static Image AddChild(Transform parent, string name, Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        private static Sprite MakeVerticalGradient(int height, Color top, Color bottom)
        {
            var tex = new Texture2D(2, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[2 * height];
            for (int y = 0; y < height; y++)
            {
                float t = y / (float)(height - 1);
                var c = Color.Lerp(bottom, top, t);
                px[y * 2 + 0] = c;
                px[y * 2 + 1] = c;
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, 2, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        private static Sprite MakeRadialFalloff(int size, Color peak, Color fade)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float cx = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - cx) / cx;
                    float dy = (y - cx) / cx;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01(1f - r);
                    t = t * t * (3f - 2f * t);
                    px[y * size + x] = Color.Lerp(fade, peak, t);
                }
            tex.SetPixels(px);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        private static Sprite MakeRadialRing(int size, float innerStop, float peakStop, float outerStop, Color peak, Color fade)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float cx = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - cx) / cx;
                    float dy = (y - cx) / cx;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c;
                    if (r < innerStop) c = fade;
                    else if (r < peakStop)
                    {
                        float t = Mathf.InverseLerp(innerStop, peakStop, r);
                        t = t * t * (3f - 2f * t);
                        c = Color.Lerp(fade, peak, t);
                    }
                    else if (r < outerStop)
                    {
                        float t = Mathf.InverseLerp(peakStop, outerStop, r);
                        t = t * t * (3f - 2f * t);
                        c = Color.Lerp(peak, fade, t);
                    }
                    else c = fade;
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        public static LedgeDreamCanvas EnsureOnCanvas(Canvas canvas)
        {
            if (canvas == null) return null;
            var existing = canvas.GetComponentInChildren<LedgeDreamCanvas>(true);
            if (existing != null)
            {
                existing.transform.SetAsFirstSibling();
                return existing;
            }
            var go = new GameObject("DreamCanvas", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            go.transform.SetAsFirstSibling();
            return go.AddComponent<LedgeDreamCanvas>();
        }
    }
}
