using UnityEngine;

namespace Magi.LedgeBoardGame.Board
{
    /// Runtime-generated UI sprites so Phase 1 visual polish ships without art-pipeline dependencies.
    /// Proper PNGs can replace any of these by assigning them on SpaceView / counter prefabs directly.
    public static class LedgeSpriteFactory
    {
        private const int Size = 128;
        private const float RingThicknessFraction = 0.14f;
        private const float CounterRadiusFraction = 0.40f;
        private const int CounterShadowOffsetX = 2;
        private const int CounterShadowOffsetY = -3;
        private const float CounterShadowBlur = 3.5f;
        private const float CounterShadowStrength = 0.45f;

        private static Sprite _disc;
        private static Sprite _ring;
        private static Sprite _counter;
        private static Sprite _frameGlow;

        public static Sprite Disc => _disc != null ? _disc : (_disc = BuildDisc());
        public static Sprite Ring => _ring != null ? _ring : (_ring = BuildRing());
        public static Sprite Counter => _counter != null ? _counter : (_counter = BuildCounter());

        /// Soft-edged disc used for the pulsing "valid target" frame glow — full disc with a radial falloff.
        public static Sprite FrameGlow => _frameGlow != null ? _frameGlow : (_frameGlow = BuildFrameGlow());

        private static Sprite BuildDisc()
        {
            var tex = NewTexture();
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
            return Finalize(tex, px, "LedgeDisc");
        }

        private static Sprite BuildRing()
        {
            var tex = NewTexture();
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
            return Finalize(tex, px, "LedgeRing");
        }

        private static Sprite BuildCounter()
        {
            var tex = NewTexture();
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

                    // Premultiplied-style composite so the shadow shows outside the disc silhouette.
                    float outA = mainA + shA * (1f - mainA);
                    float safeA = Mathf.Max(outA, 0.0001f);
                    // Foreground is pure white; shadow is pure black — equivalent to rgb = mainA / safeA.
                    float rgb = mainA / safeA;
                    byte rgbByte = (byte)(Mathf.Clamp01(rgb) * 255f);
                    byte aByte = (byte)(Mathf.Clamp01(outA) * 255f);
                    px[y * Size + x] = new Color32(rgbByte, rgbByte, rgbByte, aByte);
                }
            }
            return Finalize(tex, px, "LedgeCounter");
        }

        private static Sprite BuildFrameGlow()
        {
            var tex = NewTexture();
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
                    // Ease out so the glow concentrates near the frame rather than the center.
                    a = a * a;
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            return Finalize(tex, px, "LedgeFrameGlow");
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

        private static Sprite Finalize(Texture2D tex, Color32[] pixels, string name)
        {
            tex.name = name + "Tex";
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
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
