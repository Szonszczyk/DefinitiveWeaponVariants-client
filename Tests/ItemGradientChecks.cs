using System;
using System.Collections.Generic;
using DefinitiveWeaponVariants.Client.Effects;
using UnityEngine;

internal static class ItemGradientChecks
{
    private static void Equal(float expected, float actual)
    {
        if (float.IsNaN(actual) || Math.Abs(expected - actual) > 0.0001f)
            throw new Exception($"Expected gradient coordinate {expected}, got {actual}.");
    }

    internal static Dictionary<string, Action> Cases => new Dictionary<string, Action>
    {
        ["Inspect gradient varies vertically and stays constant horizontally"] = () =>
        {
            var view = Matrix4x4.identity;
            foreach (float x in new[] { -2f, 0f, 2f })
            {
                Equal(0, ItemGradientProjection.Coordinate(ItemGradientProjection.Height(new Vector3(x, 1, 0), view), -1, 1));
                Equal(0.5f, ItemGradientProjection.Coordinate(ItemGradientProjection.Height(new Vector3(x, 0, 0), view), -1, 1));
                Equal(1, ItemGradientProjection.Coordinate(ItemGradientProjection.Height(new Vector3(x, -1, 0), view), -1, 1));
            }
        },
        ["Quarter-turned icon pose keeps the same top-to-bottom colors as inspect"] = () =>
        {
            var iconPose = Matrix4x4.identity;
            iconPose.m00 = 0; iconPose.m01 = -1; iconPose.m10 = 1; iconPose.m11 = 0;
            // The icon turns local X into screen Y; inspect uses local Y as screen Y.
            foreach (float height in new[] { -1f, 0f, 1f })
            {
                float inspect = ItemGradientProjection.Height(new Vector3(0, height, 0), Matrix4x4.identity);
                foreach (float horizontal in new[] { -1f, 0f, 1f })
                {
                    float icon = ItemGradientProjection.Height(new Vector3(height, horizontal, 0), iconPose);
                    Equal(ItemGradientProjection.Coordinate(inspect, -1, 1), ItemGradientProjection.Coordinate(icon, -1, 1));
                }
            }
        },
        ["Perspective gradient uses projected height at different depths"] = () =>
        {
            var perspective = Matrix4x4.identity;
            perspective.m32 = 1; perspective.m33 = 0;
            Equal(0.5f, ItemGradientProjection.Height(new Vector3(-1, 1, 2), perspective));
            Equal(0.5f, ItemGradientProjection.Height(new Vector3(1, 2, 4), perspective));
            Equal(0.25f, ItemGradientProjection.Coordinate(0.5f, -1, 1));
        },
        ["Gradient spans shared model bounds instead of restarting on each surface"] = () =>
        {
            Equal(0, ItemGradientProjection.Coordinate(4, -4, 4));
            Equal(0.25f, ItemGradientProjection.Coordinate(2, -4, 4));
            Equal(0.75f, ItemGradientProjection.Coordinate(-2, -4, 4));
            Equal(1, ItemGradientProjection.Coordinate(-4, -4, 4));
        },
        ["Flat and invalid projections produce finite midpoint coordinates"] = () =>
        {
            Equal(0, ItemGradientProjection.Height(new Vector3(1, 2, 0), new Matrix4x4()));
            Equal(0.5f, ItemGradientProjection.Coordinate(2, 2, 2));
            Equal(0.5f, ItemGradientProjection.Coordinate(0, float.NegativeInfinity, float.PositiveInfinity));
            var invalid = Matrix4x4.identity; invalid.m11 = float.NaN;
            Equal(0, ItemGradientProjection.Height(new Vector3(0, 1, 0), invalid));
        }
    };
}
