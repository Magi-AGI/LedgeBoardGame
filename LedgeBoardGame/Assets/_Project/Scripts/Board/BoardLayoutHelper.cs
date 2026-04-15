using UnityEngine;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Board
{
    public static class BoardLayoutHelper
    {
        public struct Radii
        {
            public float Center;
            public float Inner;
            public float Ring2;
            public float Ring3;
            public float Outer;
            public float Ledge;
        }

        public static readonly Radii DefaultRadii = new Radii
        {
            Center = 0f,
            Inner = 100f,
            Ring2 = 200f,
            Ring3 = 300f,
            Outer = 400f,
            Ledge = 450f
        };

        // Native distance (in SVG units) from the board center to the outermost ring (OuterAdded).
        // Used as the denominator when scaling the baked SVG positions to the caller's desired outer radius.
        private const float NativeOuterRadius = 1080f;

        // Baked positions extracted from the "Spaces" layer of Ledge Wheel EN 20 (Board Game).svg,
        // normalized around the board center (2811.033, 2671.5459) and y-flipped for Unity UI (y-up).
        // Index = SpaceId. See BoardGraphBuilder.CreateHexagonalBoard for the ID layout.
        private static readonly Vector2[] SvgPositions =
        {
            new Vector2(   0.000f,    0.000f),   // 0  Center
            new Vector2(   0.000f,  269.864f),   // 1  InnerBridge  @ 90°
            new Vector2( 234.860f,  134.932f),   // 2  InnerBridge  @ 30°
            new Vector2( 234.476f, -134.932f),   // 3  InnerBridge  @ -30°
            new Vector2(  -1.253f, -269.864f),   // 4  InnerBridge  @ -90°
            new Vector2(-233.709f, -134.932f),   // 5  InnerBridge  @ -150°
            new Vector2(-234.093f,  134.932f),   // 6  InnerBridge  @ 150°
            new Vector2( 157.538f,  269.876f),   // 7  InnerStop    @ 60°
            new Vector2( 312.448f,    0.000f),   // 8  InnerStop    @ 0°
            new Vector2( 156.574f, -269.864f),   // 9  InnerStop    @ -60°
            new Vector2(-159.079f, -272.983f),   // 10 InnerStop    @ -120°
            new Vector2(-311.935f,   -0.446f),   // 11 InnerStop    @ 180°
            new Vector2(-155.806f,  269.864f),   // 12 InnerStop    @ 120°
            new Vector2(   0.832f,  539.712f),   // 13 Ring2        @ 90°
            new Vector2( 312.380f,  540.233f),   // 14 Ring2        @ 60°
            new Vector2( 468.383f,  270.046f),   // 15 Ring2        @ 30°
            new Vector2( 625.143f,    0.000f),   // 16 Ring2        @ 0°
            new Vector2( 468.847f, -270.244f),   // 17 Ring2        @ -30°
            new Vector2( 312.833f, -539.939f),   // 18 Ring2        @ -60°
            new Vector2(   0.447f, -539.445f),   // 19 Ring2        @ -90°
            new Vector2(-312.380f, -539.223f),   // 20 Ring2        @ -120°
            new Vector2(-468.183f, -270.662f),   // 21 Ring2        @ -150°
            new Vector2(-624.376f,    0.000f),   // 22 Ring2        @ 180°
            new Vector2(-468.055f,  269.669f),   // 23 Ring2        @ 150°
            new Vector2(-311.996f,  539.728f),   // 24 Ring2        @ 120°
            new Vector2( 156.574f,  809.592f),   // 25 Ring3-off    @ 79°   (ccw of vertex 60°)
            new Vector2( 624.892f,  540.125f),   // 26 Ring3-off    @ 41°   (cw  of vertex 60°)
            new Vector2( 779.697f,  270.098f),   // 27 Ring3-off    @ 19°   (ccw of vertex 0°)
            new Vector2( 781.588f, -269.757f),   // 28 Ring3-off    @ -19°  (cw  of vertex 0°)
            new Vector2( 623.339f, -538.071f),   // 29 Ring3-off    @ -41°  (ccw of vertex -60°)
            new Vector2( 156.905f, -809.605f),   // 30 Ring3-off    @ -79°  (cw  of vertex -60°)
            new Vector2(-156.190f, -810.322f),   // 31 Ring3-off    @ -101° (ccw of vertex -120°)
            new Vector2(-626.663f, -539.496f),   // 32 Ring3-off    @ -139° (cw  of vertex -120°)
            new Vector2(-780.816f, -270.341f),   // 33 Ring3-off    @ -161° (ccw of vertex 180°)
            new Vector2(-780.565f,  269.864f),   // 34 Ring3-off    @ 161°  (cw  of vertex 180°)
            new Vector2(-624.615f,  540.242f),   // 35 Ring3-off    @ 139°  (ccw of vertex 120°)
            new Vector2(-156.057f,  809.516f),   // 36 Ring3-off    @ 101°  (cw  of vertex 120°)
            new Vector2( 468.953f,  809.292f),   // 37 Ring3-vertex @ 60°
            new Vector2( 935.886f,   -0.679f),   // 38 Ring3-vertex @ 0°
            new Vector2( 466.933f, -810.111f),   // 39 Ring3-vertex @ -60°
            new Vector2(-468.820f, -809.087f),   // 40 Ring3-vertex @ -120°
            new Vector2(-937.833f,   -0.477f),   // 41 Ring3-vertex @ 180°
            new Vector2(-468.570f,  809.592f),   // 42 Ring3-vertex @ 120°
            new Vector2(   0.384f, 1079.456f),   // 43 OuterAdded   @ 90°
            new Vector2( 937.523f,  539.728f),   // 44 OuterAdded   @ 30°
            new Vector2( 937.523f, -539.728f),   // 45 OuterAdded   @ -30°
            new Vector2(   0.384f,-1079.456f),   // 46 OuterAdded   @ -90°
            new Vector2(-936.755f, -539.728f),   // 47 OuterAdded   @ -150°
            new Vector2(-938.104f,  540.350f),   // 48 OuterAdded   @ 150°
        };

        public static Vector2 ComputePosition(int spaceId, SpaceMeta meta, Radii radii)
        {
            if (spaceId < 0 || spaceId >= SvgPositions.Length)
            {
                return Vector2.zero;
            }

            float scale = radii.Ledge > 0f ? radii.Ledge / NativeOuterRadius : 1f;
            return SvgPositions[spaceId] * scale;
        }
    }
}
