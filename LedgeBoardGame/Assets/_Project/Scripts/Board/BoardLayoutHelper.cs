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

        public static Vector2 ComputePosition(int spaceId, SpaceMeta meta, Radii radii)
        {
            if (meta.Type == SpaceType.Center)
            {
                return Vector2.zero;
            }

            float radius = meta.Type switch
            {
                SpaceType.InnerBridge => radii.Inner,
                SpaceType.InnerStop => radii.Inner,
                SpaceType.Ring2 => radii.Ring2,
                SpaceType.Ring3 => radii.Ring3,
                SpaceType.OuterAdded => radii.Outer,
                SpaceType.Ledge => radii.Ledge,
                _ => radii.Ring2
            };

            float angle = meta.WedgeIndex * 30f;

            if (meta.Type == SpaceType.InnerStop)
            {
                angle += 15f;
            }

            if (meta.Type == SpaceType.Ring3)
            {
                // Ring3 contains 18 spaces; derive a unique index from the spaceId to spread them evenly.
                var ring3Index = (spaceId - 25 + 18) % 18;
                angle = ring3Index * 20f;
            }

            float x = radius * Mathf.Cos(angle * Mathf.Deg2Rad);
            float y = radius * Mathf.Sin(angle * Mathf.Deg2Rad);
            return new Vector2(x, y);
        }
    }
}
