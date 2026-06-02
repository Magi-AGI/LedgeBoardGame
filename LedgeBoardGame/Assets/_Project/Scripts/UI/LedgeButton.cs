using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.UI
{
    /// Three-variant button matching the kit's `Button` primitive
    /// (kit/ledge-board-game/project/ui/ui-primitives.jsx). Variants:
    ///
    ///   Ghost   — transparent fill, hairline border (default actions)
    ///   Primary — accent halo + accent border (your-turn / committed action)
    ///   Danger  — red-tinted (destructive: takeback, exit, eliminate)
    ///
    /// Sizes: Sm/Md/Lg map to the kit's 6/12/14 px padding scale.
    /// Constructed procedurally; existing scene-assigned Buttons can also
    /// have <see cref="ApplyTo"/> called to retrofit visuals.
    [RequireComponent(typeof(RectTransform))]
    public class LedgeButton : MonoBehaviour
    {
        public enum Variant { Ghost, Primary, Danger }
        public enum Size    { Sm, Md, Lg }

        [SerializeField] private Variant variant = Variant.Ghost;
        [SerializeField] private Size size = Size.Md;
        [SerializeField] private string labelText = "Button";

        private Image _bg;
        private TMP_Text _label;
        private Button _button;

        public Button UnityButton => _button;
        public TMP_Text Label => _label;

        public Variant CurrentVariant
        {
            get => variant;
            set { variant = value; ApplyVariantStyle(); }
        }

        public string Text
        {
            get => _label != null ? _label.text : labelText;
            set { labelText = value; if (_label != null) _label.text = value; }
        }

        private void Awake() { EnsureBuilt(); }

        public LedgeButton EnsureBuilt()
        {
            if (_button != null) return this;

            // Background image (rounded rect via flat color; the kit uses 4px
            // border-radius — Unity UI rounded corners would need a sprite, so
            // we stay flat for now and revisit if needed).
            _bg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            _bg.raycastTarget = true;

            // Outline simulates the 1px border in the kit's variants.
            var outline = gameObject.GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);

            // Label
            var labelGo = new GameObject("Label", typeof(RectTransform));
            var labelRt = (RectTransform)labelGo.transform;
            labelRt.SetParent(transform, false);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            _label = labelGo.AddComponent<TextMeshProUGUI>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.font = LedgeUITokens.UIFont;
            _label.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            _label.text = labelText;
            _label.raycastTarget = false;
            _label.characterSpacing = 18; // ~0.18em — matches the kit's Md/Sm tracking

            _button = gameObject.GetComponent<Button>() ?? gameObject.AddComponent<Button>();
            _button.transition = Selectable.Transition.ColorTint;
            _button.targetGraphic = _bg;

            ApplyVariantStyle();
            ApplySizeStyle();
            return this;
        }

        private void OnValidate()
        {
            if (_button != null) { ApplyVariantStyle(); ApplySizeStyle(); }
        }

        public void ApplyVariantStyle()
        {
            if (_bg == null) return;
            var outline = GetComponent<Outline>();
            switch (variant)
            {
                case Variant.Ghost:
                    _bg.color = new Color(0, 0, 0, 0); // transparent
                    if (outline != null) outline.effectColor = LedgeUITokens.PanelEdge2;
                    if (_label != null) _label.color = LedgeUITokens.InkFaint;
                    SetTint(LedgeUITokens.InkFaint, LedgeUITokens.Ink, LedgeUITokens.InkDim);
                    break;

                case Variant.Primary:
                    _bg.color = new Color(LedgeUITokens.Accent.r, LedgeUITokens.Accent.g, LedgeUITokens.Accent.b, 0.12f);
                    if (outline != null) outline.effectColor = new Color(LedgeUITokens.Accent.r, LedgeUITokens.Accent.g, LedgeUITokens.Accent.b, 0.55f);
                    if (_label != null) _label.color = LedgeUITokens.Accent;
                    SetTint(LedgeUITokens.Accent, Color.white, LedgeUITokens.InkDim);
                    break;

                case Variant.Danger:
                    _bg.color = new Color(0.75f, 0.25f, 0.25f, 0.10f);
                    if (outline != null) outline.effectColor = new Color(0.75f, 0.25f, 0.25f, 0.45f);
                    if (_label != null) _label.color = new Color(0.94f, 0.69f, 0.69f, 1f); // #F0B0B0
                    SetTint(new Color(0.94f, 0.69f, 0.69f, 1f), Color.white, LedgeUITokens.InkDim);
                    break;
            }
        }

        public void ApplySizeStyle()
        {
            if (_label == null) return;
            switch (size)
            {
                case Size.Sm: _label.fontSize = LedgeUITokens.ButtonSmSize; SetMinHeight(28f); break;
                case Size.Md: _label.fontSize = LedgeUITokens.ButtonMdSize; SetMinHeight(36f); break;
                case Size.Lg: _label.fontSize = LedgeUITokens.ButtonLgSize; SetMinHeight(44f); break;
            }
        }

        public void SetClickHandler(UnityAction handler)
        {
            EnsureBuilt();
            _button.onClick.RemoveAllListeners();
            if (handler != null) _button.onClick.AddListener(handler);
        }

        private void SetTint(Color normal, Color highlighted, Color disabled)
        {
            var c = _button.colors;
            c.normalColor      = Color.white;
            c.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            c.pressedColor     = new Color(0.85f, 0.85f, 0.85f, 1f);
            c.selectedColor    = c.highlightedColor;
            c.disabledColor    = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            c.colorMultiplier  = 1f;
            _button.colors = c;
        }

        private void SetMinHeight(float h)
        {
            var rt = (RectTransform)transform;
            // We don't enforce width — let the layout decide. Just set min height.
            var size = rt.sizeDelta;
            size.y = h;
            rt.sizeDelta = size;
        }

        public static LedgeButton Build(Transform parent, string label, Variant variant = Variant.Ghost,
                                        Size size = Size.Md, UnityAction onClick = null)
        {
            var go = new GameObject("LedgeButton_" + label, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var b = go.AddComponent<LedgeButton>();
            // AddComponent fires Awake → EnsureBuilt with the default labelText
            // ("Button"). We assign via the Text property AFTER, which writes to
            // both the field and the now-existing _label.text. Direct field
            // assignment alone leaves the on-screen label stuck at "Button".
            b.variant = variant;
            b.size = size;
            b.Text = label;
            b.ApplyVariantStyle();
            b.ApplySizeStyle();
            if (onClick != null) b.SetClickHandler(onClick);
            return b;
        }

        /// Retrofit an existing Button + Image + TMP_Text scene with kit chrome.
        /// Use when scene-assigned buttons exist (e.g., endTurnButton, undoButton)
        /// and you want them styled without rebuilding the GameObject. On first
        /// attach, captures the legacy label text and disables the legacy label
        /// child so it doesn't ghost behind the kit-styled label.
        public static void ApplyTo(Button button, Variant variant, Size size = Size.Md)
        {
            if (button == null) return;
            var helper = button.gameObject.GetComponent<LedgeButton>();
            bool firstAttach = helper == null;

            // Capture/disable BEFORE AddComponent. AddComponent fires Awake on
            // the new LedgeButton, which calls EnsureBuilt and creates a child
            // named "Label" — if we walked children after that, we'd capture
            // and disable our own freshly-built kit label.
            string captured = null;
            if (firstAttach)
            {
                for (int i = 0; i < button.transform.childCount; i++)
                {
                    var child = button.transform.GetChild(i);
                    var tmp = child.GetComponent<TMP_Text>();
                    if (tmp != null)
                    {
                        if (string.IsNullOrEmpty(captured)) captured = tmp.text;
                        child.gameObject.SetActive(false);
                        continue;
                    }
                    var legacy = child.GetComponent<UnityEngine.UI.Text>();
                    if (legacy != null)
                    {
                        if (string.IsNullOrEmpty(captured)) captured = legacy.text;
                        child.gameObject.SetActive(false);
                    }
                }
            }

            if (helper == null) helper = button.gameObject.AddComponent<LedgeButton>();
            helper.variant = variant;
            helper.size = size;
            if (firstAttach && !string.IsNullOrEmpty(captured)) helper.Text = captured;
            helper.EnsureBuilt();
            helper.ApplyVariantStyle();
            helper.ApplySizeStyle();
        }
    }
}
