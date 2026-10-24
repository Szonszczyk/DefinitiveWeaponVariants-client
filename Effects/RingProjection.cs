using System;
using UnityEngine;

namespace DefinitiveWeaponVariants.Client.Effects
{
    internal static class RingProjection
    {
        // The caller supplies the current source-to-muzzle transform. In particular,
        // a prewarmed mesh must not retain coordinates from the initial equip pose.
        internal static void Project(Vector3[] vertices, int[] usedVertices, Matrix4x4 sourceToMuzzle,
            Vector3 stockwardAxis, float[] distances, out float minimum, out float maximum)
        {
            for (int i = 0; i < vertices.Length; i++)
                distances[i] = Vector3.Dot(sourceToMuzzle.MultiplyPoint3x4(vertices[i]), stockwardAxis);
            minimum = float.PositiveInfinity;
            maximum = float.NegativeInfinity;
            foreach (int i in usedVertices)
            {
                minimum = Math.Min(minimum, distances[i]);
                maximum = Math.Max(maximum, distances[i]);
            }
        }
    }
}
