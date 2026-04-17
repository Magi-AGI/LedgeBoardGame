using System.Collections.Generic;
using UnityEngine;

namespace Magi.LedgeBoardGame.Board
{
    /// Runtime-generated UI sprites so Phase 1 visual polish ships without art-pipeline dependencies.
    /// Shapes are baked into 128x128 RGBA textures with 2x supersample AA. Caller-tintable sprites
    /// (Disc, Counter, HexFrame, BridgeFrame, WallFrame) are pure white on transparent; colored
    /// sprites (HexFill/HexSplit/BridgeFill/WallFill) bake pigment in so they can represent
    /// multi-color gradients and splits that a UI.Image tint can't reproduce.
    public static class LedgeSpriteFactory
    {
        private const int Size = 128;
        private const int Supersample = 2;
        public const float HexRadiusFraction = 0.95f;      // vertex distance as fraction of half-size

        /// RectTransform sizeDelta needed so a SpaceView's baked hex has the given circumradius R.
        /// The sprite inscribes its hex at HexRadiusFraction of the rect's half-size, so the rect
        /// must be larger than 2R by 1/HexRadiusFraction to compensate.
        public static float RectSizeForCircumradius(float R) => 2f * R / HexRadiusFraction;

        // Single pen-weight so hex, bridge, and wall outlines all read at the same screen
        // thickness when rendered in equally-sized rects. Expressed as a fraction of the
        // sprite's texel size so it scales with Size if tweaked.
        private const float FrameThicknessFraction = 0.037f; // ~4.7 texels on a 128 sprite
        private const float RingThicknessFraction = 0.14f; // legacy disc/ring (not used for spaces)
        private const float CounterRadiusFraction = 0.40f;
        private const int CounterShadowOffsetX = 2;
        private const int CounterShadowOffsetY = -3;
        private const float CounterShadowBlur = 3.5f;
        private const float CounterShadowStrength = 0.45f;
        private const float CounterRimThicknessTexels = 5f;

        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        // Legacy singletons preserved for InHandGhost, PlacementGhost, MovingCounter.
        public static Sprite Disc => GetOrBuild("disc", BuildDisc);
        public static Sprite Ring => GetOrBuild("ring", BuildRing);
        public static Sprite Counter => GetOrBuild("counter", BuildCounter);
        public static Sprite CounterRim => GetOrBuild("counterrim", BuildCounterRim);
        public static Sprite FrameGlow => GetOrBuild("frameglow", BuildFrameGlow);

        // Space-shape singletons (tintable white-on-transparent).
        public static Sprite HexFrame => GetOrBuild("hexframe", BuildHexFrame);
        public static Sprite HexFrameGlow => GetOrBuild("hexframeglow", BuildHexFrameGlow);
        public static Sprite BridgeFrame => GetOrBuild("bridgeframe", BuildBridgeFrame);
        public static Sprite BridgeFrameGlow => GetOrBuild("bridgeframeglow", BuildBridgeFrameGlow);
        public static Sprite WallFrame => GetOrBuild("wallframe", BuildWallFrame);
        public static Sprite WallFrameGlow => GetOrBuild("wallframeglow", BuildWallFrameGlow);

        public static Sprite GetHexFill(Color color)
        {
            string key = $"hexfill_{ColorKey(color)}";
            return GetOrBuild(key, () => BuildHexFill(color));
        }

        /// Hex filled with two colors split by a line through the center. SideA is rendered where
        /// the dot product of the pixel offset with the split-normal vector is positive.
        public static Sprite GetHexSplitFill(Color sideA, Color sideB, float splitNormalDeg)
        {
            int angKey = Mathf.RoundToInt(((splitNormalDeg % 360f) + 360f) % 360f);
            string key = $"hexsplit_{ColorKey(sideA)}_{ColorKey(sideB)}_{angKey}";
            return GetOrBuild(key, () => BuildHexSplitFill(sideA, sideB, splitNormalDeg));
        }

        /// Bridge shape in canonical orientation (+Y = outer end). Gradient runs along the shape's
        /// long axis: outerColor at +Y extent, innerColor at -Y extent.
        public static Sprite GetBridgeFill(Color outerColor, Color innerColor)
        {
            string key = $"bridgefill_{ColorKey(outerColor)}_{ColorKey(innerColor)}";
            return GetOrBuild(key, () => BuildBridgeFill(outerColor, innerColor));
        }

        public static Sprite GetWallFill()
        {
            return GetOrBuild("wallfill", () => BuildWallFill(LedgePalette.WallGray));
        }

        private static Sprite GetOrBuild(string key, System.Func<Sprite> factory)
        {
            if (_cache.TryGetValue(key, out var s) && s != null) return s;
            s = factory();
            _cache[key] = s;
            return s;
        }

        // ---------------- Hex geometry ----------------

        /// Signed distance from point (dx, dy) to pointy-top hex edge; positive = inside.
        /// Apothem = R * sqrt(3)/2, where R is the vertex distance.
        private static float HexSignedDist(float dx, float dy, float apothem)
        {
            // 6 edge normals at k*60° (k=0..5), edges at distance apothem.
            float worst = float.MaxValue;
            for (int k = 0; k < 6; k++)
            {
                float ang = k * 60f * Mathf.Deg2Rad;
                float d = apothem - (Mathf.Cos(ang) * dx + Mathf.Sin(ang) * dy);
                if (d < worst) worst = d;
            }
            return worst;
        }

        private static float HexAlpha(float dx, float dy, float apothem)
        {
            return Mathf.Clamp01(HexSignedDist(dx, dy, apothem));
        }

        private static float HexFrameAlpha(float dx, float dy, float outerApothem, float innerApothem)
        {
            float outsideOuter = HexSignedDist(dx, dy, outerApothem);
            float outsideInner = HexSignedDist(dx, dy, innerApothem);
            // Ring = inside outer AND outside inner.
            return Mathf.Clamp01(Mathf.Min(outsideOuter, -outsideInner));
        }

        // ---------------- Builders: Hex ----------------

        private static Sprite BuildHexFill(Color color)
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float R = (Size * 0.5f - 0.5f) * HexRadiusFraction;
            float apothem = R * Mathf.Sqrt(3f) * 0.5f;
            Color32 fill = color;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float a = SampleAA(x, y, cx, (xs, ys) => HexAlpha(xs, ys, apothem));
                    var c = fill;
                    c.a = (byte)(a * 255f);
                    px[y * Size + x] = c;
                }
            }
            return Finalize(px, "HexFill");
        }

        private static Sprite BuildHexSplitFill(Color sideA, Color sideB, float splitNormalDeg)
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float R = (Size * 0.5f - 0.5f) * HexRadiusFraction;
            float apothem = R * Mathf.Sqrt(3f) * 0.5f;
            float nRad = splitNormalDeg * Mathf.Deg2Rad;
            float nx = Mathf.Cos(nRad);
            float ny = Mathf.Sin(nRad);
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float aSum = 0f, rSum = 0f, gSum = 0f, bSum = 0f;
                    int samples = 0;
                    for (int sy = 0; sy < Supersample; sy++)
                    {
                        for (int sx = 0; sx < Supersample; sx++)
                        {
                            float fx = x + (sx + 0.5f) / Supersample;
                            float fy = y + (sy + 0.5f) / Supersample;
                            float dx = fx - cx;
                            float dy = (Size - 1 - fy) - cx; // y-up
                            float hexA = Mathf.Clamp01(HexSignedDist(dx, dy, apothem));
                            if (hexA <= 0f) { samples++; continue; }
                            float dot = nx * dx + ny * dy;
                            // Soft edge on the split line for AA (±0.5 px).
                            float sideT = Mathf.Clamp01(dot + 0.5f);
                            Color c = Color.Lerp(sideB, sideA, sideT);
                            aSum += hexA;
                            rSum += c.r * hexA;
                            gSum += c.g * hexA;
                            bSum += c.b * hexA;
                            samples++;
                        }
                    }
                    if (samples == 0) { px[y * Size + x] = new Color32(0, 0, 0, 0); continue; }
                    float a = aSum / samples;
                    if (a <= 0f) { px[y * Size + x] = new Color32(0, 0, 0, 0); continue; }
                    byte rB = (byte)(Mathf.Clamp01(rSum / aSum) * 255f);
                    byte gB = (byte)(Mathf.Clamp01(gSum / aSum) * 255f);
                    byte bB = (byte)(Mathf.Clamp01(bSum / aSum) * 255f);
                    byte aB = (byte)(Mathf.Clamp01(a) * 255f);
                    px[y * Size + x] = new Color32(rB, gB, bB, aB);
                }
            }
            return Finalize(px, "HexSplit");
        }

        private static Sprite BuildHexFrame()
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float R = (Size * 0.5f - 0.5f) * HexRadiusFraction;
            float outerApothem = R * Mathf.Sqrt(3f) * 0.5f;
            float thickness = Size * FrameThicknessFraction;
            float innerApothem = outerApothem - thickness;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float a = SampleAA(x, y, cx, (xs, ys) => HexFrameAlpha(xs, ys, outerApothem, innerApothem));
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            return Finalize(px, "HexFrame");
        }

        private static Sprite BuildHexFrameGlow()
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float R = (Size * 0.5f - 0.5f) * HexRadiusFraction;
            float apothem = R * Mathf.Sqrt(3f) * 0.5f;
            // Soft bloom concentrated near the hex edge — signed distance from edge, eased.
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - cx;
                    float dy = (Size - 1 - y) - cx;
                    float sd = HexSignedDist(dx, dy, apothem); // positive inside, negative outside
                    float falloff = apothem * 0.35f;
                    float t = Mathf.Clamp01(1f - Mathf.Abs(sd) / falloff);
                    t = t * t;
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(t * 255f));
                }
            }
            return Finalize(px, "HexFrameGlow");
        }

        // ---------------- Builders: Bridge ----------------

        // Bridge silhouette is a 12-vertex polygon symmetric about the radial (+Y) axis,
        // canonical with +Y = outer (away from board center). Four notches encode where
        // the bridge steps back to accommodate its neighbors in the rosette tiling:
        //   - Outer V-notch (apex at B0):  wraps around the ring2 inner vertex on this axis.
        //   - Inner V-notch (apex at B6):  wraps around the center hex vertex on this axis.
        //   - Lateral flank notches (B2-B3 on right, B9-B10 on left): carve out space for
        //                                  the adjacent walls' shoulders.
        //   - Lateral bumps (B4 / B8):     short outward flares near the inner end where
        //                                  the bridge meets the wall's flat flank.
        // Values are normalized so ±1.0 = sprite half-size, derived from the authoritative
        // SVG (Ledge Wheel EN online 20 - Bridge Shape.svg) and symmetrized.
        private const float BridgeOuterApexY       =  0.495f;  // outer V-notch tip (B0)
        private const float BridgeOuterCornerX     =  0.815f;  // outer corner half-width (B1 / B11)
        private const float BridgeOuterCornerY     =  0.970f;  // outer corner Y
        private const float BridgeFlankUpperY      =  0.255f;  // upper end of lateral notch (B2 / B10)
        private const float BridgeFlankX           =  0.415f;  // x of lateral-notch corners
        private const float BridgeFlankLowerY      = -0.215f;  // lower end of lateral notch (B3 / B9)
        private const float BridgeBumpX            =  0.615f;  // lateral bump outward extent (B4 / B8)
        private const float BridgeBumpY            = -0.335f;  // lateral bump Y
        private const float BridgeLowerTransitionY = -0.695f;  // where flank meets inner-V-notch arm (B5 / B7)
        private const float BridgeInnerApexY       = -0.455f;  // inner V-notch tip (B6)

        private static readonly Vector2[] _bridgePolygon =
        {
            new Vector2( 0f,                   BridgeOuterApexY),         // 0  outer V-notch apex
            new Vector2( BridgeOuterCornerX,   BridgeOuterCornerY),       // 1  outer-right corner
            new Vector2( BridgeFlankX,         BridgeFlankUpperY),        // 2  right-flank outer
            new Vector2( BridgeFlankX,         BridgeFlankLowerY),        // 3  right-flank inner
            new Vector2( BridgeBumpX,          BridgeBumpY),              // 4  right bump
            new Vector2( BridgeFlankX,         BridgeLowerTransitionY),   // 5  right-lower transition
            new Vector2( 0f,                   BridgeInnerApexY),         // 6  inner V-notch apex
            new Vector2(-BridgeFlankX,         BridgeLowerTransitionY),   // 7
            new Vector2(-BridgeBumpX,          BridgeBumpY),              // 8
            new Vector2(-BridgeFlankX,         BridgeFlankLowerY),        // 9
            new Vector2(-BridgeFlankX,         BridgeFlankUpperY),        // 10
            new Vector2(-BridgeOuterCornerX,   BridgeOuterCornerY),       // 11 outer-left corner
        };

        private static float BridgeInside(float nx, float ny)
        {
            return PointInPolygon(nx, ny, _bridgePolygon) ? 1f : 0f;
        }

        private static float BridgeAlpha(float dx, float dy, float halfSize)
        {
            float nx = dx / halfSize;
            float ny = dy / halfSize;
            // For AA we rely on SampleAA supersampling; individual sample returns 0 or 1.
            return BridgeInside(nx, ny);
        }

        private static float BridgeFrameAlpha(float dx, float dy, float halfSize, float thicknessPx)
        {
            float nx = dx / halfSize;
            float ny = dy / halfSize;
            if (BridgeInside(nx, ny) < 0.5f) return 0f;
            // Frame-band: inside shape AND within `thickness` of the boundary. Cheap approximation:
            // check nearby samples in 8 directions; if any is outside, we're near the edge.
            float step = thicknessPx / halfSize;
            if (BridgeInside(nx + step, ny) < 0.5f) return 1f;
            if (BridgeInside(nx - step, ny) < 0.5f) return 1f;
            if (BridgeInside(nx, ny + step) < 0.5f) return 1f;
            if (BridgeInside(nx, ny - step) < 0.5f) return 1f;
            float s = step * 0.707f;
            if (BridgeInside(nx + s, ny + s) < 0.5f) return 1f;
            if (BridgeInside(nx - s, ny + s) < 0.5f) return 1f;
            if (BridgeInside(nx + s, ny - s) < 0.5f) return 1f;
            if (BridgeInside(nx - s, ny - s) < 0.5f) return 1f;
            return 0f;
        }

        private static Sprite BuildBridgeFill(Color outerColor, Color innerColor)
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float halfSize = cx;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float aSum = 0f, rSum = 0f, gSum = 0f, bSum = 0f;
                    int samples = 0;
                    for (int sy = 0; sy < Supersample; sy++)
                    {
                        for (int sx = 0; sx < Supersample; sx++)
                        {
                            float fx = x + (sx + 0.5f) / Supersample;
                            float fy = y + (sy + 0.5f) / Supersample;
                            float dx = fx - cx;
                            float dy = (Size - 1 - fy) - cx;
                            float ny = dy / halfSize;
                            float inside = BridgeAlpha(dx, dy, halfSize);
                            // Gradient: outerColor at top (ny=+1), innerColor at bottom (ny=-1).
                            float t = Mathf.Clamp01((ny + 1f) * 0.5f);
                            Color c = Color.Lerp(innerColor, outerColor, t);
                            aSum += inside;
                            rSum += c.r * inside;
                            gSum += c.g * inside;
                            bSum += c.b * inside;
                            samples++;
                        }
                    }
                    float a = aSum / samples;
                    if (a <= 0f) { px[y * Size + x] = new Color32(0, 0, 0, 0); continue; }
                    byte rB = (byte)(Mathf.Clamp01(rSum / aSum) * 255f);
                    byte gB = (byte)(Mathf.Clamp01(gSum / aSum) * 255f);
                    byte bB = (byte)(Mathf.Clamp01(bSum / aSum) * 255f);
                    byte aB = (byte)(Mathf.Clamp01(a) * 255f);
                    px[y * Size + x] = new Color32(rB, gB, bB, aB);
                }
            }
            return Finalize(px, "BridgeFill");
        }

        private static Sprite BuildBridgeFrame()
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float halfSize = cx;
            float thicknessPx = Size * FrameThicknessFraction;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float a = SampleAA(x, y, cx, (xs, ys) => BridgeFrameAlpha(xs, ys, halfSize, thicknessPx));
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            return Finalize(px, "BridgeFrame");
        }

        private static Sprite BuildBridgeFrameGlow()
        {
            // Glow is the full silhouette at uniform alpha — the space will render it behind
            // the fill/frame. Simpler than per-pixel SDF and reads fine on a small shape.
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float halfSize = cx;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float a = SampleAA(x, y, cx, (xs, ys) => BridgeAlpha(xs, ys, halfSize));
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            return Finalize(px, "BridgeFrameGlow");
        }

        // ---------------- Builders: Wall ----------------

        // Wall silhouette is a 6-vertex polygon symmetric about the radial (+Y) axis,
        // canonical with +Y = outer. Shape is a flat-outer hex with a trimmed inner end:
        //   - Outer edge:        flat, flush with the hex boundary (W0 → W1).
        //   - Straight flanks:   from outer corners down to the shoulders (W1 → W2, W5 → W0).
        //   - Tapered inner:     diagonals from the shoulders to a narrower inner edge.
        // The taper carves space for the two adjacent bridges' lateral bumps.
        // Values from the authoritative SVG (Ledge Wheel EN online 20 - Stop Shape.svg).
        private const float WallOuterCornerX =  0.50f;   // outer-corner half-width
        private const float WallOuterCornerY =  0.72f;   // outer corners Y (flat outer edge)
        private const float WallShoulderY    = -0.14f;   // where flanks start tapering inward
        private const float WallInnerCornerX =  0.25f;   // inner-corner half-width
        private const float WallInnerCornerY = -0.58f;   // inner corners Y (flat inner edge)

        private static readonly Vector2[] _wallPolygon =
        {
            new Vector2(-WallOuterCornerX, WallOuterCornerY),   // 0  outer-left
            new Vector2( WallOuterCornerX, WallOuterCornerY),   // 1  outer-right
            new Vector2( WallOuterCornerX, WallShoulderY),      // 2  right shoulder
            new Vector2( WallInnerCornerX, WallInnerCornerY),   // 3  right-inner
            new Vector2(-WallInnerCornerX, WallInnerCornerY),   // 4  left-inner
            new Vector2(-WallOuterCornerX, WallShoulderY),      // 5  left shoulder
        };

        private static float WallInside(float nx, float ny)
        {
            return PointInPolygon(nx, ny, _wallPolygon) ? 1f : 0f;
        }

        private static float WallFrameAlpha(float dx, float dy, float halfSize, float thicknessPx)
        {
            float nx = dx / halfSize;
            float ny = dy / halfSize;
            if (WallInside(nx, ny) < 0.5f) return 0f;
            float step = thicknessPx / halfSize;
            if (WallInside(nx + step, ny) < 0.5f) return 1f;
            if (WallInside(nx - step, ny) < 0.5f) return 1f;
            if (WallInside(nx, ny + step) < 0.5f) return 1f;
            if (WallInside(nx, ny - step) < 0.5f) return 1f;
            float s = step * 0.707f;
            if (WallInside(nx + s, ny + s) < 0.5f) return 1f;
            if (WallInside(nx - s, ny + s) < 0.5f) return 1f;
            if (WallInside(nx + s, ny - s) < 0.5f) return 1f;
            if (WallInside(nx - s, ny - s) < 0.5f) return 1f;
            return 0f;
        }

        private static Sprite BuildWallFill(Color color)
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float halfSize = cx;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float a = SampleAA(x, y, cx, (xs, ys) =>
                    {
                        float nx = xs / halfSize;
                        float ny = ys / halfSize;
                        return WallInside(nx, ny);
                    });
                    Color32 c = color;
                    c.a = (byte)(a * 255f);
                    px[y * Size + x] = c;
                }
            }
            return Finalize(px, "WallFill");
        }

        private static Sprite BuildWallFrame()
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float halfSize = cx;
            float thicknessPx = Size * FrameThicknessFraction;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float a = SampleAA(x, y, cx, (xs, ys) => WallFrameAlpha(xs, ys, halfSize, thicknessPx));
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            return Finalize(px, "WallFrame");
        }

        private static Sprite BuildWallFrameGlow()
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float halfSize = cx;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float a = SampleAA(x, y, cx, (xs, ys) =>
                    {
                        float nx = xs / halfSize;
                        float ny = ys / halfSize;
                        return WallInside(nx, ny);
                    });
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            return Finalize(px, "WallFrameGlow");
        }

        // ---------------- Legacy builders (Disc / Ring / Counter / FrameGlow) ----------------

        private static Sprite BuildDisc()
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float r = Size * 0.5f - 0.5f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - cx;
                    float dy = y - cx;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d);
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            return Finalize(px, "LedgeDisc");
        }

        private static Sprite BuildRing()
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float outer = Size * 0.5f - 0.5f;
            float inner = outer * (1f - RingThicknessFraction);
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - cx;
                    float dy = y - cx;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float outerEdge = Mathf.Clamp01(outer - d);
                    float innerEdge = Mathf.Clamp01(d - inner);
                    float a = Mathf.Clamp01(Mathf.Min(outerEdge, innerEdge));
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            return Finalize(px, "LedgeRing");
        }

        private static Sprite BuildCounter()
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float r = Size * CounterRadiusFraction;
            float shx = cx + CounterShadowOffsetX;
            float shy = cx + CounterShadowOffsetY;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dxMain = x - cx;
                    float dyMain = y - cx;
                    float dMain = Mathf.Sqrt(dxMain * dxMain + dyMain * dyMain);
                    float mainA = Mathf.Clamp01(r - dMain);

                    float dxSh = x - shx;
                    float dySh = y - shy;
                    float dSh = Mathf.Sqrt(dxSh * dxSh + dySh * dySh);
                    float shA = Mathf.Clamp01((r + CounterShadowBlur - dSh) / (CounterShadowBlur * 2f)) * CounterShadowStrength;

                    float outA = mainA + shA * (1f - mainA);
                    float safeA = Mathf.Max(outA, 0.0001f);
                    float rgb = mainA / safeA;
                    byte rgbByte = (byte)(Mathf.Clamp01(rgb) * 255f);
                    byte aByte = (byte)(Mathf.Clamp01(outA) * 255f);
                    px[y * Size + x] = new Color32(rgbByte, rgbByte, rgbByte, aByte);
                }
            }
            return Finalize(px, "LedgeCounter");
        }

        private static Sprite BuildCounterRim()
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float rOuter = Size * CounterRadiusFraction;
            float rInner = rOuter - CounterRimThicknessTexels;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - cx;
                    float dy = y - cx;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float outerEdge = Mathf.Clamp01(rOuter - d);
                    float innerEdge = Mathf.Clamp01(d - rInner);
                    float a = Mathf.Clamp01(Mathf.Min(outerEdge, innerEdge));
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            return Finalize(px, "LedgeCounterRim");
        }

        private static Sprite BuildFrameGlow()
        {
            var px = new Color32[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float outer = Size * 0.5f - 0.5f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - cx;
                    float dy = y - cx;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d / outer);
                    a = a * a;
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            return Finalize(px, "LedgeFrameGlow");
        }

        // ---------------- Utilities ----------------

        // Crossing-number point-in-polygon test. Handles non-convex shapes (bridge's notches).
        private static bool PointInPolygon(float px, float py, Vector2[] verts)
        {
            bool inside = false;
            int n = verts.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = verts[i];
                var b = verts[j];
                if ((a.y > py) != (b.y > py))
                {
                    float xAtY = (b.x - a.x) * (py - a.y) / (b.y - a.y) + a.x;
                    if (px < xAtY) inside = !inside;
                }
            }
            return inside;
        }

        private delegate float PixelFunc(float dx, float dy);

        private static float SampleAA(int x, int y, float cx, PixelFunc func)
        {
            float sum = 0f;
            for (int sy = 0; sy < Supersample; sy++)
            {
                for (int sx = 0; sx < Supersample; sx++)
                {
                    float fx = x + (sx + 0.5f) / Supersample;
                    float fy = y + (sy + 0.5f) / Supersample;
                    float dx = fx - cx;
                    float dy = (Size - 1 - fy) - cx;
                    sum += func(dx, dy);
                }
            }
            return sum / (Supersample * Supersample);
        }

        private static string ColorKey(Color c)
        {
            int r = Mathf.RoundToInt(c.r * 255f);
            int g = Mathf.RoundToInt(c.g * 255f);
            int b = Mathf.RoundToInt(c.b * 255f);
            int a = Mathf.RoundToInt(c.a * 255f);
            return $"{r:x2}{g:x2}{b:x2}{a:x2}";
        }

        private static Texture2D NewTexture()
        {
            return new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static Sprite Finalize(Color32[] pixels, string name)
        {
            var tex = NewTexture();
            tex.name = name + "Tex";
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, Size, Size),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f,
                extrude: 0,
                meshType: SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
