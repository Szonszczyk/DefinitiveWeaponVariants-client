using System;
using UnityEngine;

namespace DefinitiveWeaponVariants.Client.Effects
{
    internal static class ItemGradientProjection
    {
        internal static float Height(Vector3 vertex, Matrix4x4 localToClip)
        {
            float y = localToClip.m10 * vertex.x + localToClip.m11 * vertex.y + localToClip.m12 * vertex.z + localToClip.m13;
            float w = localToClip.m30 * vertex.x + localToClip.m31 * vertex.y + localToClip.m32 * vertex.z + localToClip.m33;
            float height = w > 0.000001f ? y / w : 0f;
            return float.IsNaN(height) || float.IsInfinity(height) ? 0f : height;
        }

        // First server color at the top, second at the bottom, across the entire item.
        internal static float Coordinate(float height, float minimum, float maximum)
        {
            float span = maximum - minimum;
            if (span <= 0.000001f || float.IsInfinity(span) || float.IsNaN(span)) return 0.5f;
            return Math.Max(0f, Math.Min(1f, (maximum - height) / span));
        }
    }
}
