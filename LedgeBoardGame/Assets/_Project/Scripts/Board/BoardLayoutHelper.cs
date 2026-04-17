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

        // The rosette has six-fold rotational symmetry and every space sits on a
        // pointy-top hex grid whose circumradius is R. Two neighbor distances follow:
        //   flat-edge-shared neighbors (even "outer-axis" wedges) → R·√3
        //   vertex-shared     neighbors (odd  "vertex"     wedges) → 2R
        // Wedge index 0..11 corresponds to angle (90 − 30·w) degrees (CW from +Y).
        // Even wedges 0,2,4,6,8,10 are the outer-axis directions (90°,30°,-30°,-90°,-150°,150°).
        // Odd  wedges 1,3,5,7,9,11 are the vertex      directions (60°, 0°,-60°,-120°,180°,120°).
        private const float Sqrt3   = 1.7320508f;
        private const float Sqrt28  = 5.2915026f;           // |Ring3-off| = R·√28
        private const float Ring3OffOffsetDeg = 19.106605f; // arctan(1/√27) off the vertex axis

        // InnerWall sits at 2.14·R, not the pure hex-grid 2·R. Walls must NOT touch the
        // Center space — that is the definitional property of a wall. When the Bridges
        // are oriented correctly their outer edges leave a gap at 2.14·R where the Walls
        // tessellate against them.
        private const float InnerWallRadiusFactor = 2.14f;

        // Ledge radius in Radii is interpreted as the OuterAdded circumscribed radius.
        // OuterAdded sits at 4·R·√3, so R = Ledge / (4·√3).
        private const float OuterAddedRadiusFactor = 4f * Sqrt3;

        public static Vector2 ComputePosition(int spaceId, SpaceMeta meta, Radii radii)
        {
            return ComputePositionForHexRadius(spaceId, ComputeHexLayoutUnit(radii));
        }

        /// Hex lattice unit used by ComputePositionForHexRadius. All center-to-center
        /// neighbor distances in the rosette are either R·√3 (Center ↔ InnerBridge) or 2·R
        /// (every pure-hex adjacency: Ring2, Ring3, OuterAdded).
        public static float ComputeHexLayoutUnit(Radii radii)
        {
            return radii.Ledge > 0f ? radii.Ledge / OuterAddedRadiusFactor : 1f;
        }

        /// Circumradius for the drawn hex sprite, sized so that hex-type adjacencies at
        /// center-distance 2·R_layout flat-edge-share (R_visual·√3 = 2·R_layout).
        /// Center / InnerBridge / InnerWall use custom shapes; their overlap with their
        /// larger-than-lattice-unit hex neighbors is intentional (bridge/wall geometry is
        /// still being refined separately).
        public static float ComputeHexVisualRadius(Radii radii)
        {
            return 2f * ComputeHexLayoutUnit(radii) / Sqrt3;
        }

        [System.Obsolete("Use ComputeHexLayoutUnit (for positions) or ComputeHexVisualRadius (for sprite sizing).")]
        public static float ComputeHexCircumradius(Radii radii) => ComputeHexLayoutUnit(radii);

        public static Vector2 ComputePositionForHexRadius(int spaceId, float R)
        {
            // Canonical space IDs (see BoardGraphBuilder.CreateHexagonalBoard):
            //   0         Center
            //   1  – 6    InnerBridge  on even wedges 0,2,4,6,8,10   at R·√3
            //   7  – 12   InnerWall    on odd  wedges 1,3,5,7,9,11   at 2.14·R (gap, not 2R)
            //   13 – 24   Ring2 per wedge, alternating (even: 2R·√3, odd: 4R)
            //   25 – 36   Ring3-off, 6 pairs flanking vertex axes by ±19.107°, at R·√28
            //   37 – 42   Ring3-vertex on odd wedges                 at 6R
            //   43 – 48   OuterAdded   on even wedges                at 4R·√3
            if (spaceId == 0) return Vector2.zero;

            if (spaceId >= 1 && spaceId <= 6)
            {
                int evenWedge = (spaceId - 1) * 2;
                return Polar(R * Sqrt3, WedgeAngleDeg(evenWedge));
            }

            if (spaceId >= 7 && spaceId <= 12)
            {
                int oddWedge = (spaceId - 7) * 2 + 1;
                return Polar(InnerWallRadiusFactor * R, WedgeAngleDeg(oddWedge));
            }

            if (spaceId >= 13 && spaceId <= 24)
            {
                int wedge = spaceId - 13;
                float rRing = (wedge & 1) == 0 ? 2f * R * Sqrt3 : 4f * R;
                return Polar(rRing, WedgeAngleDeg(wedge));
            }

            if (spaceId >= 25 && spaceId <= 36)
            {
                // Pairs traverse the 6 vertex wedges in order (1,3,5,7,9,11);
                // the even index in each pair is the CCW-of-vertex offset, odd is CW.
                int index  = spaceId - 25;
                int pairId = index / 2;
                bool ccw   = (index & 1) == 0;
                int oddWedge = pairId * 2 + 1;
                float offset = ccw ? +Ring3OffOffsetDeg : -Ring3OffOffsetDeg;
                return Polar(R * Sqrt28, WedgeAngleDeg(oddWedge) + offset);
            }

            if (spaceId >= 37 && spaceId <= 42)
            {
                int oddWedge = (spaceId - 37) * 2 + 1;
                return Polar(6f * R, WedgeAngleDeg(oddWedge));
            }

            if (spaceId >= 43 && spaceId <= 48)
            {
                int evenWedge = (spaceId - 43) * 2;
                return Polar(4f * R * Sqrt3, WedgeAngleDeg(evenWedge));
            }

            return Vector2.zero;
        }

        private static float WedgeAngleDeg(int wedge) => 90f - 30f * wedge;

        private static Vector2 Polar(float r, float angleDeg)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a));
        }
    }
}
