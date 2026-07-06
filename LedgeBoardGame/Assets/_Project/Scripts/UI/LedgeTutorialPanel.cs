using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.UI
{
    /// "How to play" / tutorial screen. Built from
    /// `kit/ledge-board-game/project/ui/frame-tutorial.jsx` → TutorialFrame:
    /// a 2-column split with a highlighted board diagram on the left and a
    /// stepped explanation on the right (Fraunces italic "Four ideas." +
    /// four numbered steps + Start practice / Skip actions).
    ///
    /// The board diagram is omitted from this port for the same reason as
    /// the title hero: no isolated BoardFrame surface yet outside a live
    /// BoardPresenter. The right-side copy carries the load.
    ///
    /// Copy is verbatim from the kit (flagged in handoff v4 §3.12 as
    /// placeholder — confirm against rules in a follow-up pass).
    public class LedgeTutorialPanel : MonoBehaviour
    {
        private GameObject _overlayGo;
        private Action _onStartPractice;
        private Action _onSkip;

        private static readonly (int n, string title, string body)[] Steps =
        {
            (1, "The wheel", "Each board is a wheel of twelve spirit colors, four rings deep, with twelve outer Ledges."),
            (2, "Place & move", "Spend energy to place Light or Dark counters, then move stacks inward and outward along the rings."),
            (3, "Cross the Ledge", "From an outer Ledge you can cross onto an opponent's board — affecting their spaces only while you pass through."),
            (4, "Claim a Core", "Reach the center to claim a Core. Hold the most when the wheel runs out of moves."),
        };

        private void Awake() => EnsureBuilt();

        public void EnsureBuilt()
        {
            if (_overlayGo != null) return;
            BuildUi();
            HideInternal();
        }

        // ── Public API ─────────────────────────────────────────────────────

        public void Show(Action onStartPractice, Action onSkip)
        {
            EnsureBuilt();
            _onStartPractice = onStartPractice;
            _onSkip = onSkip;
            _overlayGo.SetActive(true);
            _overlayGo.transform.SetAsLastSibling();
        }

        public void Hide() => HideInternal();

        public bool IsShowing => _overlayGo != null && _overlayGo.activeSelf;

        [ContextMenu("Show Tutorial (Test)")]
        private void ShowTestPreview()
        {
            Show(
                onStartPractice: () => UnityEngine.Debug.Log("[tutorial] Start practice (test)"),
                onSkip: () => UnityEngine.Debug.Log("[tutorial] Skip (test)"));
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
            catch (Exception ex) { UnityEngine.Debug.LogError($"[tutorial] callback threw: {ex}"); }
        }

        // ── UI construction ───────────────────────────────────────────────

        private void BuildUi()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            _overlayGo = new GameObject("TutorialOverlay", typeof(RectTransform));
            var overlayRt = (RectTransform)_overlayGo.transform;
            overlayRt.SetParent(canvas.transform, false);
            overlayRt.anchorMin = Vector2.zero; overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero; overlayRt.offsetMax = Vector2.zero;
            overlayRt.SetAsLastSibling();

            // Solid canvas backdrop — tutorial is a full takeover.
            var bgGo = new GameObject("Backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.SetParent(overlayRt, false);
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.color = LedgeUITokens.Canvas;
            bgImg.raycastTarget = true;

            // Copy column carrying all the type. The kit's left column has a
            // highlighted board diagram; we omit that (no isolated BoardFrame
            // surface yet). With the diagram gone, anchoring to the right half
            // left the composition visibly lopsided, so the column is a fixed-
            // width block centred on screen instead — a conservative nudge that
            // keeps the kit's line length while balancing the layout.
            var rightGo = new GameObject("CopyCol", typeof(RectTransform));
            var rightRt = (RectTransform)rightGo.transform;
            rightRt.SetParent(overlayRt, false);
            rightRt.anchorMin = new Vector2(0.5f, 0.5f);
            rightRt.anchorMax = new Vector2(0.5f, 0.5f);
            rightRt.pivot = new Vector2(0.5f, 0.5f);
            rightRt.sizeDelta = new Vector2(980f, 680f);
            rightRt.anchoredPosition = Vector2.zero;

            var vl = rightGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 0f;
            vl.padding = new RectOffset(56, 56, 0, 0);
            vl.childAlignment = TextAnchor.MiddleLeft;
            vl.childControlWidth = true;
            // childControlHeight=true so nested sub-groups (Steps, Actions) and
            // LayoutElement-declared heights are both measured AND sized by this
            // group. With it false the group ignored the Steps sub-group's
            // preferred height and stacked the Actions row on top of the steps.
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            // Section label
            var section = MakeText(rightRt, "SectionLabel", LedgeUITokens.MonoFont,
                LedgeUITokens.SectionLabelSize, LedgeUITokens.InkDim, "HOW TO PLAY");
            section.fontStyle = FontStyles.UpperCase;
            section.characterSpacing = 22f;
            section.alignment = TextAlignmentOptions.MidlineLeft;
            AddLayoutHeight(section.gameObject, 18f);

            AddVerticalSpacer(rightRt, 14f);

            // Display italic "Four ideas."
            var headline = MakeText(rightRt, "Headline", LedgeUITokens.DisplayFont, 44f, LedgeUITokens.Ink, "Four ideas.");
            headline.fontStyle = FontStyles.Italic;
            headline.alignment = TextAlignmentOptions.MidlineLeft;
            AddLayoutHeight(headline.gameObject, 50f);

            AddVerticalSpacer(rightRt, 36f);

            // Steps container
            var stepsGo = new GameObject("Steps", typeof(RectTransform));
            var stepsRt = (RectTransform)stepsGo.transform;
            stepsRt.SetParent(rightRt, false);
            var stepsVl = stepsGo.AddComponent<VerticalLayoutGroup>();
            stepsVl.spacing = 22f;
            stepsVl.childAlignment = TextAnchor.UpperLeft;
            stepsVl.childControlWidth = true;
            // Control height so each row's declared LayoutElement height sizes
            // the row and rolls up into this group's preferred height (which the
            // parent CopyCol now reads to reserve the steps' full footprint).
            stepsVl.childControlHeight = true;
            stepsVl.childForceExpandWidth = true;
            stepsVl.childForceExpandHeight = false;

            foreach (var step in Steps)
            {
                BuildStepRow(stepsRt, step.n, step.title, step.body);
            }

            AddVerticalSpacer(rightRt, 40f);

            // Action row
            var actionsGo = new GameObject("Actions", typeof(RectTransform));
            var actionsRt = (RectTransform)actionsGo.transform;
            actionsRt.SetParent(rightRt, false);
            var hl = actionsGo.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 12f;
            hl.childAlignment = TextAnchor.MiddleLeft;
            // Control both axes from the buttons' LayoutElements: width so the
            // preferred widths below actually apply (with childControlWidth off
            // the group ignored them and the buttons collapsed to 100px), and
            // height WITHOUT force-expand so the buttons keep their ~48px height
            // instead of stretching. force-expand-height also made this row
            // report flexibleHeight to CopyCol, which dumped its vertical slack
            // here and ballooned the buttons; keeping it false pins the row.
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;
            AddLayoutHeight(actionsGo, 52f);

            var startBtn = LedgeButton.Build(actionsRt, "Start practice", LedgeButton.Variant.Primary, LedgeButton.Size.Lg,
                () => FireAndHide(_onStartPractice));
            var startLe = startBtn.gameObject.AddComponent<LayoutElement>();
            startLe.preferredWidth = 210f; startLe.minWidth = 190f;
            startLe.preferredHeight = 48f; startLe.minHeight = 44f;

            var skipBtn = LedgeButton.Build(actionsRt, "Skip", LedgeButton.Variant.Ghost, LedgeButton.Size.Lg,
                () => FireAndHide(_onSkip));
            var skipLe = skipBtn.gameObject.AddComponent<LayoutElement>();
            skipLe.preferredWidth = 110f; skipLe.minWidth = 90f;
            skipLe.preferredHeight = 48f; skipLe.minHeight = 44f;
        }

        private static void BuildStepRow(Transform parent, int n, string title, string body)
        {
            var rowGo = new GameObject($"Step_{n}", typeof(RectTransform));
            var rowRt = (RectTransform)rowGo.transform;
            rowRt.SetParent(parent, false);
            var hl = rowGo.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 18f;
            hl.childAlignment = TextAnchor.UpperLeft;
            hl.childControlWidth = true;
            // Control height so the number badge and the text column are both
            // measured/sized from their declared heights rather than a stale
            // rect. Row height bumped to 76 to give two-line bodies headroom.
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;
            AddLayoutHeight(rowGo, 76f);

            // Numbered circle: 34×34 rounded square with thin border + italic
            // accent numeral. Unity UI can't draw a circle without a sprite,
            // so we render the disc and rely on padding to centre the digit.
            var circleGo = new GameObject("Number",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Outline), typeof(LayoutElement));
            circleGo.transform.SetParent(rowRt, false);
            var circleImg = circleGo.GetComponent<Image>();
            circleImg.sprite = GetCircleSprite();
            circleImg.color = new Color(0f, 0f, 0f, 0f);
            circleImg.raycastTarget = false;
            var circleOutline = circleGo.GetComponent<Outline>();
            circleOutline.effectColor = LedgeUITokens.PanelEdge2;
            circleOutline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);
            var circleLe = circleGo.GetComponent<LayoutElement>();
            circleLe.minWidth = 34f; circleLe.preferredWidth = 34f;
            circleLe.minHeight = 34f; circleLe.preferredHeight = 34f;

            var nText = MakeText(circleGo.transform, "N", LedgeUITokens.DisplayFont, 16f, LedgeUITokens.Accent, n.ToString());
            nText.fontStyle = FontStyles.Italic;
            nText.alignment = TextAlignmentOptions.Center;
            var nRt = nText.rectTransform;
            nRt.anchorMin = Vector2.zero; nRt.anchorMax = Vector2.one;
            nRt.offsetMin = Vector2.zero; nRt.offsetMax = Vector2.zero;

            // Title + body stacked.
            var textColGo = new GameObject("TextCol", typeof(RectTransform));
            var textColRt = (RectTransform)textColGo.transform;
            textColRt.SetParent(rowRt, false);
            var colVl = textColGo.AddComponent<VerticalLayoutGroup>();
            colVl.spacing = 4f;
            colVl.childAlignment = TextAnchor.UpperLeft;
            colVl.childControlWidth = true;
            // Control height so the title + body declared heights size the
            // labels and sum into this column's preferred height (read by the
            // row's HorizontalLayoutGroup to size the whole step).
            colVl.childControlHeight = true;
            colVl.childForceExpandWidth = true;
            colVl.childForceExpandHeight = false;
            var textColLe = textColGo.AddComponent<LayoutElement>();
            textColLe.flexibleWidth = 1f;
            textColLe.minWidth = 200f;

            var titleText = MakeText(textColRt, "Title", LedgeUITokens.UIFont, 16f, LedgeUITokens.Ink, title);
            titleText.fontStyle = FontStyles.Bold;
            titleText.alignment = TextAlignmentOptions.TopLeft;
            AddLayoutHeight(titleText.gameObject, 20f);

            var bodyText = MakeText(textColRt, "Body", LedgeUITokens.UIFont, 13.5f, LedgeUITokens.InkFaint, body);
            bodyText.alignment = TextAlignmentOptions.TopLeft;
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            // 48px holds up to three wrapped lines of body copy at 13.5pt; the
            // narrower centred column wraps the longer steps to two lines.
            AddLayoutHeight(bodyText.gameObject, 48f);
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

        private static void AddVerticalSpacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        // Cached AA circle sprite for the numbered step badges. Same
        // technique as elsewhere in the kit chrome.
        private static Sprite _circleSprite;
        private static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[N * N];
            float c = (N - 1) * 0.5f;
            float rOuter = c;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x - c, dy = y - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(rOuter - d);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _circleSprite.hideFlags = HideFlags.HideAndDontSave;
            return _circleSprite;
        }
    }
}
