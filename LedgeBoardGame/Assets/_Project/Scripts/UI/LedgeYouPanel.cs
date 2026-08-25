using Magi.LedgeBoardGame.Board;
using Magi.LedgeBoardGame.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.UI
{
    /// Top-left "You" panel — the turn-flow read. Answers one question loudly:
    /// <em>what do I do now?</em>
    ///
    /// CP066 inverted this panel's hierarchy. It used to lead with identity —
    /// caption "CURRENT PLAYER", the name, then "Player1's turn." at 22f in
    /// full-strength Ink — and finish with the only line carrying new
    /// information ("Pick a stack, then a valid destination.") at BodySize
    /// 12.5f in InkFaint. Three rows restated who was playing; the actionable
    /// row was the smallest and faintest thing in the panel.
    ///
    /// Rows now, top to bottom:
    ///   1. Identity strip — wedge dot + name, with "TURN n · YOUR MOVE" right-
    ///      aligned. One quiet row instead of three loud ones. Identity and
    ///      wedge colour are still first-class, just no longer dominant.
    ///   2. Action line — the state-specific instruction, at display weight
    ///      (TurnBannerSize, Fraunces italic). This is the panel now.
    ///   3. Tone tracker — placement only: which of Light/Dark is still owed.
    ///   4. Sub line — short supporting detail, the row that is dropped first.
    ///
    /// Compact mode (Comparison view, 3+ seats) keeps rows 1-2 and drops 3-4.
    /// It used to drop the instruction itself, which meant the configuration
    /// with the most turn-order confusion got no next-action guidance at all.
    /// The instruction is the one row that is NOT inferable from the board —
    /// identity and turn number are both readable from nameplates and the
    /// active-board glow — so it is the last thing sacrificed, not the first.
    [RequireComponent(typeof(RectTransform))]
    public class LedgeYouPanel : MonoBehaviour
    {
        /// Turn-flow facts the panel cannot derive from GameState alone: they
        /// live in GameController (selection buffer, tween/echo in flight).
        /// Passed by value so the panel stays a pure renderer of pushed state
        /// and never reaches back into the controller.
        ///
        /// default(TurnFlowHint) is all-false, which degrades to phase-generic
        /// guidance rather than a wrong claim — safe for any caller that hasn't
        /// been updated.
        public readonly struct TurnFlowHint
        {
            /// Movement phase: a source stack is picked up and awaiting a destination.
            public readonly bool HasSelection;
            /// A move tween or network echo is in flight; the board is mid-resolve.
            public readonly bool IsResolving;

            public TurnFlowHint(bool hasSelection, bool isResolving)
            {
                HasSelection = hasSelection;
                IsResolving = isResolving;
            }
        }

        private LedgeGlassPanel _panel;
        private TMP_Text _sectionLabel;
        private Image _wedgeDot;
        private TMP_Text _name;
        private TMP_Text _actionLabel;
        private TMP_Text _toneLabel;
        private TMP_Text _subLabel;

        private bool _compact;

        // Full chrome vs the slim Comparison-view variant. Compact keeps the
        // identity strip + action line and drops the tone tracker / sub line,
        // so the panel stops crowding the SEATS strip at 3+ seats.
        private const float PanelWidth = 360f;
        private const float FullHeight = 130f;
        private const float CompactHeight = 80f;

        // Row geometry, in content-local space (the glass panel insets its
        // content by PanelPadX/PanelPadY, so y=0 is already inside the padding).
        private const float ActionY_Full = -26f;
        private const float ActionY_Compact = -22f;
        private const float ActionH_Full = 40f;
        private const float ActionH_Compact = 28f;
        private const float ActionSizeMax_Full = LedgeUITokens.TurnBannerSize; // 32f
        private const float ActionSizeMax_Compact = 22f;
        private const float ActionSizeMin = 16f;
        // The sub line sits under the tone tracker during placement and slides
        // up into its slot when the tracker is hidden, so movement states don't
        // show a gap where the tracker would have been.
        private const float ToneY = -70f;
        private const float SubY_WithTone = -88f;
        private const float SubY_NoTone = -70f;

        private void Awake() => EnsureBuilt();

        /// Slim the top-left panel for Comparison view at 3+ seats.
        public void SetCompactMode(bool compact)
        {
            EnsureBuilt();
            // Apply size + visibility unconditionally rather than early-returning
            // when the flag is unchanged: the assignments are idempotent, and
            // re-asserting them means compact state can't silently desync if
            // anything else touches the rect or those labels.
            _compact = compact;

            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(PanelWidth, _compact ? CompactHeight : FullHeight);

            // Rows 3 and 4 are the expendable ones. Row 2 (the instruction)
            // stays visible in both modes — that is the whole point of CP066.
            // Only the compact direction is forced here; in full mode their
            // visibility is UpdateFromState's call (the tracker is placement-
            // only, the sub line is empty in some states), so forcing them on
            // would flash a stale row between a mode flip and the next push.
            if (_compact)
            {
                if (_toneLabel != null) _toneLabel.gameObject.SetActive(false);
                if (_subLabel != null) _subLabel.gameObject.SetActive(false);
            }

            if (_actionLabel != null)
            {
                var aRt = _actionLabel.rectTransform;
                aRt.anchoredPosition = new Vector2(0f, _compact ? ActionY_Compact : ActionY_Full);
                aRt.sizeDelta = new Vector2(0f, _compact ? ActionH_Compact : ActionH_Full);
                _actionLabel.fontSizeMax = _compact ? ActionSizeMax_Compact : ActionSizeMax_Full;
            }
        }

        public void EnsureBuilt()
        {
            if (_panel != null) return;

            var rt = (RectTransform)transform;
            // Anchored top-left of canvas.
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(LedgeUITokens.PanelEdgeInset, -LedgeUITokens.PanelEdgeInset);
            rt.sizeDelta = new Vector2(PanelWidth, FullHeight);

            _panel = LedgeGlassPanel.Build(transform, "Glass");
            // Stretch the panel to fill this RectTransform.
            var pRt = _panel.GetComponent<RectTransform>();
            pRt.anchorMin = Vector2.zero;
            pRt.anchorMax = Vector2.one;
            pRt.offsetMin = Vector2.zero;
            pRt.offsetMax = Vector2.zero;

            var content = _panel.Content;

            // ── Row 1: identity strip (dot + name … turn meta) ──────────
            var identGo = new GameObject("Ident", typeof(RectTransform));
            var identRt = (RectTransform)identGo.transform;
            identRt.SetParent(content, false);
            identRt.anchorMin = new Vector2(0f, 1f);
            identRt.anchorMax = new Vector2(1f, 1f);
            identRt.pivot     = new Vector2(0f, 1f);
            identRt.anchoredPosition = new Vector2(0f, 0f);
            identRt.sizeDelta = new Vector2(0f, 18f);

            _wedgeDot = MakeWedgeDot(identRt, 11f);
            var dRt = _wedgeDot.rectTransform;
            dRt.anchorMin = new Vector2(0f, 0.5f);
            dRt.anchorMax = new Vector2(0f, 0.5f);
            dRt.pivot     = new Vector2(0f, 0.5f);
            dRt.anchoredPosition = new Vector2(0f, 0f);

            // Name is demoted from IdentNameSize/Ink to body weight — still
            // bold and full-strength enough to read as identity, no longer
            // competing with the instruction below it.
            _name = MakeText(identRt, "Name", LedgeUITokens.UIFont,
                LedgeUITokens.BodySize, LedgeUITokens.Ink, "—");
            _name.fontStyle = FontStyles.Bold;
            _name.overflowMode = TextOverflowModes.Ellipsis;
            var nRt = _name.rectTransform;
            nRt.anchorMin = new Vector2(0f, 0f);
            nRt.anchorMax = new Vector2(1f, 1f);
            nRt.pivot     = new Vector2(0f, 0.5f);
            // Right inset reserves the turn-meta column so long names ellipsize
            // instead of colliding with "TURN n · YOUR MOVE".
            nRt.offsetMin = new Vector2(18f, 0f);
            nRt.offsetMax = new Vector2(-132f, 0f);

            // Turn meta, right-aligned on the same row. Carries the active-seat
            // accent (see UpdateFromState).
            _sectionLabel = MakeText(identRt, "TurnMeta", LedgeUITokens.MonoFont,
                LedgeUITokens.SectionLabelSize, LedgeUITokens.InkDim, "");
            _sectionLabel.fontStyle = FontStyles.UpperCase;
            _sectionLabel.characterSpacing = 14;
            _sectionLabel.alignment = TextAlignmentOptions.MidlineRight;
            var sRt = _sectionLabel.rectTransform;
            sRt.anchorMin = new Vector2(0f, 0f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.pivot     = new Vector2(0f, 0.5f);
            sRt.offsetMin = new Vector2(0f, 0f);
            sRt.offsetMax = new Vector2(0f, 0f);

            // ── Row 2: the action line — the panel's dominant read ──────
            _actionLabel = MakeText(content, "ActionLabel", LedgeUITokens.DisplayFont,
                ActionSizeMax_Full, LedgeUITokens.Ink, "");
            _actionLabel.fontStyle = FontStyles.Italic;
            // Autosizing rather than a fixed size: "Waiting for <long name>" and
            // "Choose destination" have very different widths, and a 360px panel
            // has to hold both without clipping. Shrink first, ellipsize only if
            // the floor is reached.
            _actionLabel.enableAutoSizing = true;
            _actionLabel.fontSizeMin = ActionSizeMin;
            _actionLabel.fontSizeMax = ActionSizeMax_Full;
            _actionLabel.overflowMode = TextOverflowModes.Ellipsis;
            var tRt = _actionLabel.rectTransform;
            tRt.anchorMin = new Vector2(0f, 1f);
            tRt.anchorMax = new Vector2(1f, 1f);
            tRt.pivot     = new Vector2(0f, 1f);
            tRt.anchoredPosition = new Vector2(0f, ActionY_Full);
            tRt.sizeDelta = new Vector2(0f, ActionH_Full);

            // ── Row 3: placement tone tracker ───────────────────────────
            // Deliberately text + colour rather than drawn pips: the mono font
            // asset's glyph table is not guaranteed to carry ✓/●/○, and a
            // missing glyph renders as a fallback box. Rich-text colour and
            // <s> are engine features, not font features, so they can't fail
            // that way.
            _toneLabel = MakeText(content, "ToneTracker", LedgeUITokens.MonoFont,
                LedgeUITokens.SectionLabelSize, LedgeUITokens.InkDim, "");
            _toneLabel.fontStyle = FontStyles.UpperCase;
            _toneLabel.characterSpacing = 18;
            _toneLabel.richText = true;
            var toRt = _toneLabel.rectTransform;
            toRt.anchorMin = new Vector2(0f, 1f);
            toRt.anchorMax = new Vector2(1f, 1f);
            toRt.pivot     = new Vector2(0f, 1f);
            toRt.anchoredPosition = new Vector2(0f, ToneY);
            toRt.sizeDelta = new Vector2(0f, 16f);

            // ── Row 4: sub line ─────────────────────────────────────────
            _subLabel = MakeText(content, "SubLabel", LedgeUITokens.UIFont,
                LedgeUITokens.BodySize, LedgeUITokens.InkFaint, "");
            _subLabel.overflowMode = TextOverflowModes.Ellipsis;
            var stRt = _subLabel.rectTransform;
            stRt.anchorMin = new Vector2(0f, 1f);
            stRt.anchorMax = new Vector2(1f, 1f);
            stRt.pivot     = new Vector2(0f, 1f);
            stRt.anchoredPosition = new Vector2(0f, SubY_NoTone);
            stRt.sizeDelta = new Vector2(0f, 16f);
        }

        /// Push the latest game state into the panel. Call from GameController
        /// alongside existing GameHud.UpdateHud calls.
        public void UpdateFromState(GameState state, int localSeatId, bool isNetworkMode,
                                    TurnFlowHint hint = default)
        {
            EnsureBuilt();
            if (state == null) return;

            // Identity → local player in network mode; current player in hot-seat
            // (so it functions like a "whose turn" header in shared-screen play).
            Player you = null;
            if (isNetworkMode && state.Players != null)
            {
                for (int i = 0; i < state.Players.Count; i++)
                {
                    var p = state.Players[i];
                    if (p != null && p.Id == localSeatId) { you = p; break; }
                }
            }
            else
            {
                you = state.GetCurrentPlayer();
            }

            _name.text = you != null ? you.Name : "—";

            // Wedge color — derived from the player's board if we can find it.
            int wedge = 0;
            if (you != null && state.Boards != null)
            {
                for (int i = 0; i < state.Boards.Count; i++)
                {
                    var b = state.Boards[i];
                    if (b != null && b.PlayerId == you.Id)
                    {
                        // The "wedge" of a player isn't first-class in the model;
                        // proxy via BoardId (each player owns one board, and seat
                        // assignment defines a colour wedge in the kit). For now
                        // map BoardId → wedge directly; refine when a proper
                        // seat→wedge mapping ships.
                        wedge = b.BoardId;
                        break;
                    }
                }
            }
            _wedgeDot.color = LedgePalette.GetOwnColor(wedge);

            int currentId = state.CurrentPlayerId;
            string activeName = state.GetCurrentPlayer()?.Name ?? $"Player {currentId}";

            // The seat this panel represents is the acting seat. In hot-seat the
            // panel already shows the current player, so it is always active —
            // previously this was `isNetworkMode && …`, which made the accent
            // treatment unreachable in local play, the mode most sessions run in.
            bool seatIsActive = !isNetworkMode || currentId == localSeatId;
            bool waiting = isNetworkMode && !seatIsActive;

            _sectionLabel.text = waiting
                ? $"Turn {state.TurnNumber}"
                : $"Turn {state.TurnNumber} · Your move";
            _sectionLabel.color = (seatIsActive && !state.GameOver)
                ? LedgeUITokens.Accent
                : LedgeUITokens.InkDim;

            // ── The action line ─────────────────────────────────────────
            // Ordering is precedence, not phase: game-over and not-your-turn
            // outrank everything, and a resolving board outranks any prompt to
            // act (telling someone to "Place Dark" mid-tween invites a click
            // the controller will reject).
            string action;
            string sub;
            Color actionColor = LedgeUITokens.Ink;
            bool showTones = false;

            if (state.GameOver)
            {
                action = "Game over";
                sub = state.WinnerId.HasValue
                    ? $"Winner: Player {state.WinnerId.Value}"
                    : "No winner.";
            }
            else if (waiting)
            {
                action = $"Waiting for {activeName}";
                sub = "";
                actionColor = LedgeUITokens.AccentCool;
            }
            else if (hint.IsResolving)
            {
                // Plain ASCII: the display font asset is not guaranteed to carry
                // U+2026, and a missing glyph reads as a box.
                action = "Resolving...";
                sub = "";
                actionColor = LedgeUITokens.InkFaint;
            }
            else if (state.CurrentPhase == GamePhase.Placement)
            {
                showTones = true;
                bool light = state.HasPlacedLight;
                bool dark = state.HasPlacedDark;
                if (!light)
                {
                    // Checked first so a Dark-first placement still reads correctly.
                    action = "Place Light";
                    sub = "Same space stacks.";
                }
                else if (!dark)
                {
                    action = "Place Dark";
                    sub = "Same space stacks.";
                }
                else
                {
                    action = "Ready to end turn";
                    sub = "Both tones placed.";
                    actionColor = LedgeUITokens.Accent;
                }
            }
            else
            {
                if (hint.HasSelection)
                {
                    action = "Choose destination";
                    sub = "Highlighted spaces are in reach.";
                }
                else
                {
                    action = "Select a stack";
                    sub = "Highlighted stacks can move.";
                }
            }

            _actionLabel.text = action;
            _actionLabel.color = actionColor;

            if (showTones) _toneLabel.text = BuildToneTracker(state);

            // Compact drops rows 3-4 wholesale; SetCompactMode owns that and must
            // not be undone here.
            if (!_compact)
            {
                _toneLabel.gameObject.SetActive(showTones);
                _subLabel.gameObject.SetActive(!string.IsNullOrEmpty(sub));
                _subLabel.rectTransform.anchoredPosition =
                    new Vector2(0f, showTones ? SubY_WithTone : SubY_NoTone);
            }
            _subLabel.text = sub;
        }

        /// "LIGHT   DARK" with the owed tone in accent and any placed tone
        /// struck through and dimmed. Answers "which token am I placing next"
        /// from the guidance surface instead of only from the cursor ghost.
        private static string BuildToneTracker(GameState state)
        {
            string light = state.HasPlacedLight
                ? Done("Light")
                : Next("Light");
            string dark = state.HasPlacedDark
                ? Done("Dark")
                : (state.HasPlacedLight ? Next("Dark") : Pending("Dark"));
            return light + "   " + dark;
        }

        private static string Done(string s) =>
            $"<s><color=#{ColorUtility.ToHtmlStringRGBA(LedgeUITokens.InkDim)}>{s}</color></s>";

        private static string Next(string s) =>
            $"<color=#{ColorUtility.ToHtmlStringRGBA(LedgeUITokens.Accent)}>{s}</color>";

        private static string Pending(string s) =>
            $"<color=#{ColorUtility.ToHtmlStringRGBA(LedgeUITokens.InkMute)}>{s}</color>";

        // ── Helpers ──────────────────────────────────────────────────────
        private static TMP_Text MakeText(Transform parent, string name, TMP_FontAsset font,
                                         float size, Color color, string text)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.text = text;
            t.raycastTarget = false;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            return t;
        }

        private static Image MakeWedgeDot(Transform parent, float size)
        {
            var go = new GameObject("WedgeDot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.sprite = GetCircleSprite();
            img.color = Color.white;
            img.raycastTarget = false;
            return img;
        }

        // Cached AA circle sprite, lazily generated. 64×64 with a soft 1px AA
        // ring at the rim so the swatch reads as a disc rather than a square.
        private static Sprite _circleSprite;
        public static Sprite GetCircleSprite()
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
                    float a = Mathf.Clamp01(rOuter - d); // 1 inside, 0 outside, ~1px AA at rim
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
