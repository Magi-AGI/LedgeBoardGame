using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.UI
{
    /// Title / landing screen. Built from
    /// `kit/ledge-board-game/project/ui/frame-title.jsx` → TitleFrame:
    /// hero board fading into a bottom scrim, giant Fraunces italic "Ledge"
    /// logotype + mono tagline, action row (Play / Practice / How to play /
    /// Settings), version footer.
    ///
    /// Hero board is intentionally omitted in this port — the gameplay
    /// `LedgeDreamCanvas` backdrop sits on the same lobby canvas and
    /// provides the starfield + gradient atmosphere. Reintroducing the
    /// scaled board would need either a live BoardPresenter or a sprite
    /// snapshot; deferred.
    ///
    /// Spawned by LedgeLobbyBootstrap before the lobby main panel becomes
    /// visible. Closing the title (Play / Practice) hands off to the lobby
    /// with the corresponding tab pre-selected.
    public class LedgeTitlePanel : MonoBehaviour
    {
        [Tooltip("Version string rendered in the bottom-right footer.")]
        [SerializeField] private string versionString = "v0.6 · MAGI AGI";

        private GameObject _overlayGo;
        private Action _onPlay;
        private Action _onPractice;
        private Action _onHowToPlay;
        private Action _onSettings;

        // F6 first-launch nudge: warm accent halo pulses on the
        // How-to-play button until the user clicks it (or moves on). The
        // pulse is opt-in via Show's highlightHowToPlay param so subsequent
        // sessions get a clean title.
        private LedgeButton _howBtn;
        private Outline _howGlow;
        private bool _highlightHowToPlay;
        private float _highlightPhase;

        private static Sprite _verticalScrimSprite;

        private void Awake() => EnsureBuilt();

        public void EnsureBuilt()
        {
            if (_overlayGo != null) return;
            BuildUi();
            HideInternal();
        }

        // ── Public API ─────────────────────────────────────────────────────

        public void Show(Action onPlay, Action onPractice, Action onHowToPlay = null,
                         Action onSettings = null, bool highlightHowToPlay = false)
        {
            EnsureBuilt();
            _onPlay = onPlay;
            _onPractice = onPractice;
            _onHowToPlay = onHowToPlay;
            _onSettings = onSettings;
            _highlightHowToPlay = highlightHowToPlay;
            _highlightPhase = 0f;
            if (_howGlow != null && !highlightHowToPlay)
                _howGlow.effectColor = new Color(0f, 0f, 0f, 0f);
            _overlayGo.SetActive(true);
        }

        public void Hide() => HideInternal();

        [ContextMenu("Show Title (Test)")]
        private void ShowTestPreview()
        {
            Show(
                onPlay: () => UnityEngine.Debug.Log("[title] Play (test)"),
                onPractice: () => UnityEngine.Debug.Log("[title] Practice (test)"),
                onHowToPlay: () => UnityEngine.Debug.Log("[title] How to play (test)"),
                onSettings: () => UnityEngine.Debug.Log("[title] Settings (test)"));
        }

        // ── Internals ─────────────────────────────────────────────────────

        private void HideInternal()
        {
            if (_overlayGo != null) _overlayGo.SetActive(false);
        }

        private void FireAndHide(Action callback)
        {
            HideInternal();
            try { callback?.Invoke(); }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[title] callback threw: {ex}"); }
        }

        private static void FireWithoutHide(Action callback)
        {
            try { callback?.Invoke(); }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[title] callback threw: {ex}"); }
        }

        private void Update()
        {
            if (_overlayGo == null || !_overlayGo.activeSelf) return;
            if (!_highlightHowToPlay || _howGlow == null) return;
            // ~0.6Hz pulse between 20% and 60% alpha — visible but
            // restrained, matches the kit's "gentle nudge" call.
            _highlightPhase += Time.unscaledDeltaTime;
            float t = (Mathf.Sin(_highlightPhase * 3.8f) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(0.20f, 0.60f, t);
            _howGlow.effectColor = new Color(
                LedgeUITokens.Accent.r, LedgeUITokens.Accent.g, LedgeUITokens.Accent.b, alpha);
        }

        // ── UI construction ───────────────────────────────────────────────

        private void BuildUi()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            _overlayGo = new GameObject("TitleOverlay", typeof(RectTransform));
            var overlayRt = (RectTransform)_overlayGo.transform;
            overlayRt.SetParent(canvas.transform, false);
            overlayRt.anchorMin = Vector2.zero; overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero; overlayRt.offsetMax = Vector2.zero;
            overlayRt.SetAsLastSibling();

            // Bottom-scrim gradient — transparent top → opaque bottom so the
            // dream backdrop reads at the top and the title lockup pops at
            // the bottom.
            var scrimGo = new GameObject("Scrim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var scrimRt = (RectTransform)scrimGo.transform;
            scrimRt.SetParent(overlayRt, false);
            scrimRt.anchorMin = Vector2.zero; scrimRt.anchorMax = Vector2.one;
            scrimRt.offsetMin = Vector2.zero; scrimRt.offsetMax = Vector2.zero;
            var scrimImg = scrimGo.GetComponent<Image>();
            scrimImg.sprite = GetVerticalScrimSprite();
            scrimImg.color = Color.white;
            scrimImg.raycastTarget = true;
            scrimImg.type = Image.Type.Simple;
            scrimImg.preserveAspect = false;

            // Title lockup — anchored bottom-center, rises 196px to clear
            // the action row (36 + button height ~52 + tagline + margin).
            var lockupGo = new GameObject("Lockup", typeof(RectTransform));
            var lockupRt = (RectTransform)lockupGo.transform;
            lockupRt.SetParent(overlayRt, false);
            lockupRt.anchorMin = new Vector2(0.5f, 0f);
            lockupRt.anchorMax = new Vector2(0.5f, 0f);
            lockupRt.pivot = new Vector2(0.5f, 0f);
            lockupRt.anchoredPosition = new Vector2(0f, 196f);
            lockupRt.sizeDelta = new Vector2(900f, 180f);

            var vl = lockupGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 18f;
            vl.childAlignment = TextAnchor.MiddleCenter;
            vl.childControlWidth = true;
            vl.childControlHeight = false;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            var titleText = MakeText(lockupRt, "Title", LedgeUITokens.DisplayFont, 120f, LedgeUITokens.Ink, "Ledge");
            titleText.fontStyle = FontStyles.Italic;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.characterSpacing = -3f; // ~-0.03em at this size
            // Cool-accent glow per kit's textShadow rgba(143,180,255,0.25).
            // Outline at small offset is the cheapest reasonable facsimile;
            // real text-shadow blur would need a custom shader.
            var glow = titleText.gameObject.AddComponent<Outline>();
            glow.effectColor = new Color(LedgeUITokens.AccentCool.r, LedgeUITokens.AccentCool.g, LedgeUITokens.AccentCool.b, 0.25f);
            glow.effectDistance = new Vector2(2f, -2f);
            glow.useGraphicAlpha = true;
            AddLayoutHeight(titleText.gameObject, 132f);

            var taglineText = MakeText(lockupRt, "Tagline", LedgeUITokens.MonoFont, 11f, LedgeUITokens.InkFaint,
                "A WHEEL OF TWELVE SPIRITS");
            taglineText.fontStyle = FontStyles.UpperCase;
            taglineText.characterSpacing = 42f; // ~0.42em
            taglineText.alignment = TextAlignmentOptions.Center;
            AddLayoutHeight(taglineText.gameObject, 14f);

            // Action row — anchored bottom-center.
            var actionsGo = new GameObject("Actions", typeof(RectTransform));
            var actionsRt = (RectTransform)actionsGo.transform;
            actionsRt.SetParent(overlayRt, false);
            actionsRt.anchorMin = new Vector2(0.5f, 0f);
            actionsRt.anchorMax = new Vector2(0.5f, 0f);
            actionsRt.pivot = new Vector2(0.5f, 0f);
            actionsRt.anchoredPosition = new Vector2(0f, 36f);
            actionsRt.sizeDelta = new Vector2(800f, 52f);

            var hl = actionsGo.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 14f;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = false;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;

            // Play / Practice hide the title (transition to lobby); How-to-
            // play / Settings open a modal on top and leave the title
            // visible behind. Different semantics → different click paths.
            var playBtn = LedgeButton.Build(actionsRt, "Play", LedgeButton.Variant.Primary, LedgeButton.Size.Lg,
                () => FireAndHide(_onPlay));
            var playLe = playBtn.gameObject.AddComponent<LayoutElement>();
            playLe.minWidth = 160f; playLe.preferredWidth = 160f;

            var practiceBtn = LedgeButton.Build(actionsRt, "Practice", LedgeButton.Variant.Ghost, LedgeButton.Size.Lg,
                () => FireAndHide(_onPractice));
            var practiceLe = practiceBtn.gameObject.AddComponent<LayoutElement>();
            practiceLe.preferredWidth = 140f; practiceLe.minWidth = 120f;

            _howBtn = LedgeButton.Build(actionsRt, "How to play", LedgeButton.Variant.Ghost, LedgeButton.Size.Lg,
                () =>
                {
                    // First-launch nudge stops the moment the user actually
                    // clicks the highlighted button — don't keep pulsing if
                    // they're already engaging with the tutorial.
                    _highlightHowToPlay = false;
                    if (_howGlow != null) _howGlow.effectColor = new Color(0f, 0f, 0f, 0f);
                    FireWithoutHide(_onHowToPlay);
                });
            var howLe = _howBtn.gameObject.AddComponent<LayoutElement>();
            howLe.preferredWidth = 160f; howLe.minWidth = 140f;

            // Dedicated halo Outline for F6 pulse. LedgeButton already adds
            // its own variant outline; this is a second Outline at a wider
            // effectDistance whose alpha we animate. Starts transparent so
            // non-highlighted Shows render normally.
            _howGlow = _howBtn.gameObject.AddComponent<Outline>();
            _howGlow.effectDistance = new Vector2(3f, -3f);
            _howGlow.effectColor = new Color(0f, 0f, 0f, 0f);
            _howGlow.useGraphicAlpha = true;

            var settingsBtn = LedgeButton.Build(actionsRt, "Settings", LedgeButton.Variant.Ghost, LedgeButton.Size.Lg,
                () => FireWithoutHide(_onSettings));
            var settingsLe = settingsBtn.gameObject.AddComponent<LayoutElement>();
            settingsLe.preferredWidth = 130f; settingsLe.minWidth = 120f;

            // Version footer — bottom-right.
            var verGo = MakeText(overlayRt, "Version", LedgeUITokens.MonoFont, 9.5f, LedgeUITokens.InkMute,
                versionString ?? "").rectTransform;
            var verTmp = verGo.GetComponent<TMP_Text>();
            verTmp.fontStyle = FontStyles.UpperCase;
            verTmp.characterSpacing = 16f;
            verTmp.alignment = TextAlignmentOptions.BottomRight;
            verGo.anchorMin = new Vector2(1f, 0f);
            verGo.anchorMax = new Vector2(1f, 0f);
            verGo.pivot = new Vector2(1f, 0f);
            verGo.anchoredPosition = new Vector2(-20f, 14f);
            verGo.sizeDelta = new Vector2(220f, 14f);
        }

        // ── Build helpers ─────────────────────────────────────────────────

        private static TMP_Text MakeText(Transform parent, string name, TMP_FontAsset font,
                                         float size, Color color, string text)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.text = text;
            t.raycastTarget = false;
            return t;
        }

        private static void AddLayoutHeight(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        // Vertical scrim gradient: transparent → fade → opaque from top to
        // bottom. Three stops per the kit:
        //   0-40%   transparent
        //   40-78%  0 → 0.85 alpha
        //   78-100% 0.85 → 0.97 alpha
        // 1×256 sprite stretched vertically to canvas height. The y axis in
        // Unity sprite space runs bottom-up, so the kit's "180deg" gradient
        // (which goes top→bottom) maps to bottom-up alpha 0.97 → 0.85 → 0
        // → 0 here.
        private static Sprite GetVerticalScrimSprite()
        {
            if (_verticalScrimSprite != null) return _verticalScrimSprite;
            const int H = 256;
            var tex = new Texture2D(1, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[H];
            const byte R = 10, G = 13, B = 27;
            for (int y = 0; y < H; y++)
            {
                // t=0 bottom, t=1 top in sprite-space → invert for kit's top-down.
                float tTop = 1f - (y / (float)(H - 1));
                float alpha;
                if (tTop < 0.40f) alpha = 0f;
                else if (tTop < 0.78f) alpha = Mathf.Lerp(0f, 0.85f, (tTop - 0.40f) / 0.38f);
                else alpha = Mathf.Lerp(0.85f, 0.97f, (tTop - 0.78f) / 0.22f);
                px[y] = new Color32(R, G, B, (byte)(alpha * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _verticalScrimSprite = Sprite.Create(tex, new Rect(0, 0, 1, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _verticalScrimSprite.hideFlags = HideFlags.HideAndDontSave;
            return _verticalScrimSprite;
        }
    }
}
