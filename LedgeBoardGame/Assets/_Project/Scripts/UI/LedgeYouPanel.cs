using Magi.LedgeBoardGame.Board;
using Magi.LedgeBoardGame.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.UI
{
    /// Top-left "You" panel — identity + current turn state. Folds the kit's
    /// PlayerIdent + TurnBanner + phase/status text into a single chrome
    /// surface that lives directly above the local player's board.
    ///
    /// Three rows:
    ///   1. Section label "YOU" (or "PLAYER N" in hot-seat)
    ///   2. Identity row — wedge swatch + display name
    ///   3. Turn state row — Fraunces italic "Your turn" / "Player N's turn"
    ///                       + body line ("place a counter", "make a move", …)
    [RequireComponent(typeof(RectTransform))]
    public class LedgeYouPanel : MonoBehaviour
    {
        private LedgeGlassPanel _panel;
        private TMP_Text _sectionLabel;
        private Image _wedgeDot;
        private TMP_Text _name;
        private TMP_Text _turnLabel;
        private TMP_Text _statusLabel;

        private bool _compact;

        // Full chrome vs the slim Comparison-view variant. Compact drops the
        // turn/status rows (their content folds into the section caption), so
        // the panel stops crowding the SEATS strip at 3+ seats.
        private const float PanelWidth = 360f;
        private const float FullHeight = 130f;
        private const float CompactHeight = 70f;

        private void Awake() => EnsureBuilt();

        /// Slim the top-left panel for Comparison view at 3+ seats. Keeps the
        /// section caption + identity row and folds the turn line into the
        /// caption (see UpdateFromState). Full mode is unchanged.
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
            if (_turnLabel != null) _turnLabel.gameObject.SetActive(!_compact);
            if (_statusLabel != null) _statusLabel.gameObject.SetActive(!_compact);
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
            rt.sizeDelta = new Vector2(360f, 130f);

            _panel = LedgeGlassPanel.Build(transform, "Glass");
            // Stretch the panel to fill this RectTransform.
            var pRt = _panel.GetComponent<RectTransform>();
            pRt.anchorMin = Vector2.zero;
            pRt.anchorMax = Vector2.one;
            pRt.offsetMin = Vector2.zero;
            pRt.offsetMax = Vector2.zero;

            var content = _panel.Content;

            // Section label (small, mono, dim, uppercase)
            _sectionLabel = MakeText(content, "SectionLabel", LedgeUITokens.MonoFont,
                LedgeUITokens.SectionLabelSize, LedgeUITokens.InkDim, "YOU");
            _sectionLabel.fontStyle = FontStyles.UpperCase;
            _sectionLabel.characterSpacing = 22; // ~0.22em
            var sRt = _sectionLabel.rectTransform;
            sRt.anchorMin = new Vector2(0f, 1f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.pivot     = new Vector2(0f, 1f);
            sRt.anchoredPosition = new Vector2(0f, 0f);
            sRt.sizeDelta = new Vector2(0f, 14f);

            // Identity row: wedge dot + name
            var identGo = new GameObject("Ident", typeof(RectTransform));
            var identRt = (RectTransform)identGo.transform;
            identRt.SetParent(content, false);
            identRt.anchorMin = new Vector2(0f, 1f);
            identRt.anchorMax = new Vector2(1f, 1f);
            identRt.pivot     = new Vector2(0f, 1f);
            identRt.anchoredPosition = new Vector2(0f, -18f);
            identRt.sizeDelta = new Vector2(0f, 24f);

            _wedgeDot = MakeWedgeDot(identRt, 18f);
            var dRt = _wedgeDot.rectTransform;
            dRt.anchorMin = new Vector2(0f, 0.5f);
            dRt.anchorMax = new Vector2(0f, 0.5f);
            dRt.pivot     = new Vector2(0f, 0.5f);
            dRt.anchoredPosition = new Vector2(0f, 0f);

            _name = MakeText(identRt, "Name", LedgeUITokens.UIFont,
                LedgeUITokens.IdentNameSize, LedgeUITokens.Ink, "—");
            _name.fontStyle = FontStyles.Bold;
            var nRt = _name.rectTransform;
            nRt.anchorMin = new Vector2(0f, 0f);
            nRt.anchorMax = new Vector2(1f, 1f);
            nRt.pivot     = new Vector2(0f, 0.5f);
            nRt.offsetMin = new Vector2(28f, 0f); // dot width + gap
            nRt.offsetMax = new Vector2(0f, 0f);

            // Turn line: Fraunces italic
            _turnLabel = MakeText(content, "TurnLabel", LedgeUITokens.DisplayFont,
                22f, LedgeUITokens.Ink, "");
            _turnLabel.fontStyle = FontStyles.Italic;
            var tRt = _turnLabel.rectTransform;
            tRt.anchorMin = new Vector2(0f, 1f);
            tRt.anchorMax = new Vector2(1f, 1f);
            tRt.pivot     = new Vector2(0f, 1f);
            tRt.anchoredPosition = new Vector2(0f, -50f);
            tRt.sizeDelta = new Vector2(0f, 28f);

            // Status line (smaller, faint)
            _statusLabel = MakeText(content, "StatusLabel", LedgeUITokens.UIFont,
                LedgeUITokens.BodySize, LedgeUITokens.InkFaint, "");
            var stRt = _statusLabel.rectTransform;
            stRt.anchorMin = new Vector2(0f, 1f);
            stRt.anchorMax = new Vector2(1f, 1f);
            stRt.pivot     = new Vector2(0f, 1f);
            stRt.anchoredPosition = new Vector2(0f, -82f);
            stRt.sizeDelta = new Vector2(0f, 18f);
        }

        /// Push the latest game state into the panel. Call from GameController
        /// alongside existing GameHud.UpdateHud calls.
        public void UpdateFromState(GameState state, int localSeatId, bool isNetworkMode)
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

            _sectionLabel.text = isNetworkMode ? "YOU" : "CURRENT PLAYER";
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

            // Turn state
            int currentId = state.CurrentPlayerId;
            bool itsYourTurn = isNetworkMode && currentId == localSeatId;
            string activeName = state.GetCurrentPlayer()?.Name ?? $"Player {currentId}";
            _turnLabel.text = itsYourTurn ? "Your turn." : $"{activeName}'s turn.";
            _turnLabel.color = itsYourTurn ? LedgeUITokens.Accent : LedgeUITokens.Ink;

            // Compact mode hides the turn row, so fold the turn line into the
            // section caption instead. The full-mode caption set above is left
            // exactly as-is; this only overrides it when compact.
            if (_compact)
            {
                _sectionLabel.text = itsYourTurn
                    ? $"TURN {state.TurnNumber} · YOUR MOVE"
                    : $"TURN {state.TurnNumber} · {activeName.ToUpperInvariant()}'S MOVE";
            }

            // Status / phase guidance
            string statusStr;
            if (state.GameOver)
            {
                statusStr = state.WinnerId.HasValue
                    ? $"Game over — winner: Player {state.WinnerId.Value}"
                    : "Game over.";
            }
            else if (state.CurrentPhase == GamePhase.Placement)
            {
                statusStr = "Place one Light and one Dark. Same space stacks.";
            }
            else
            {
                statusStr = "Pick a stack, then a valid destination.";
            }
            _statusLabel.text = statusStr;
        }

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
