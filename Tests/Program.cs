using System;
using System.Collections.Generic;
using DefinitiveWeaponVariants.Client.Effects;
using UnityEngine;

internal static class Program
{
    private static void Equal(float expected, float actual)
    {
        if (Math.Abs(expected - actual) > 0.0001f) throw new Exception($"Expected {expected}, got {actual}.");
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Reject(string json)
    {
        try { RingColorMap.Parse(json); }
        catch { return; }
        throw new Exception("Invalid map was accepted: " + json);
    }
    private static int Main()
    {
        var tests = new Dictionary<string, Action>
        {
            ["Projection uses settled pose after early preparation"] = () =>
            {
                var vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 1) };
                var distances = new float[2];
                RingProjection.Project(vertices, new[] { 0, 1 }, Matrix4x4.identity, new Vector3(0, 0, 1), distances, out _, out _);
                var currentPose = Matrix4x4.identity;
                currentPose.m23 = 2f;
                RingProjection.Project(vertices, new[] { 0, 1 }, currentPose, new Vector3(0, 0, 1), distances, out float min, out float max);
                Equal(2, min); Equal(3, max); Equal(2, distances[0]); Equal(3, distances[1]);
            },
            ["Projection refreshes muzzle direction with unchanged source pose"] = () =>
            {
                var vertices = new[] { new Vector3(1, 0, 2), new Vector3(2, 0, 4) };
                var distances = new float[2];
                RingProjection.Project(vertices, new[] { 0, 1 }, Matrix4x4.identity, new Vector3(1, 0, 0), distances, out _, out _);
                RingProjection.Project(vertices, new[] { 0, 1 }, Matrix4x4.identity, new Vector3(0, 0, -1), distances, out float min, out float max);
                Equal(-4, min); Equal(-2, max); Equal(-2, distances[0]); Equal(-4, distances[1]);
            },
            ["Excluded submeshes cannot enlarge band travel"] = () =>
            {
                var vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 0, 1000) };
                RingProjection.Project(vertices, new[] { 0, 1 }, Matrix4x4.identity, new Vector3(0, 0, 1), new float[3], out float min, out float max);
                Equal(0, min); Equal(1, max);
            },
            ["Animated mesh positions update without reload"] = () =>
            {
                var vertices = new[] { new Vector3(0, 0, 1) };
                var distances = new float[1];
                RingProjection.Project(vertices, new[] { 0 }, Matrix4x4.identity, new Vector3(0, 0, 1), distances, out _, out _);
                vertices[0] = new Vector3(0, 0, 2);
                RingProjection.Project(vertices, new[] { 0 }, Matrix4x4.identity, new Vector3(0, 0, 1), distances, out float min, out float max);
                Equal(2, min); Equal(2, max); Equal(2, distances[0]);
            },
            ["No idle ring"] = () => { var r = new RingMotion(); r.Advance(10f, 1f, 6f); Check(!r.Active); Equal(0, r.Progress); },
            ["First shot starts at muzzle"] = () => { var r = new RingMotion(); r.Fire(); Check(r.Active && !r.Returning); Equal(0, r.Progress); },
            ["Normal sweep follows duration"] = () => { var r = new RingMotion(); r.Fire(); r.Advance(0.25f, 1f, 6f); Equal(0.25f, r.Progress); },
            ["Second shot returns same ring faster"] = () => { var r = new RingMotion(); r.Fire(); r.Advance(0.6f, 1f, 4f); r.Fire(); Check(r.Returning); r.Advance(0.05f, 1f, 4f); Equal(0.4f, r.Progress); },
            ["Repeated shots do not restart return"] = () => { var r = new RingMotion(); r.Fire(); r.Advance(0.6f, 1f, 4f); r.Fire(); r.Advance(0.05f, 1f, 4f); r.Fire(); r.Advance(0.05f, 1f, 4f); Check(r.Returning); Equal(0.2f, r.Progress); },
            ["Return reaches muzzle then sweeps forward"] = () => { var r = new RingMotion(); r.Fire(); r.Advance(0.6f, 1f, 4f); r.Fire(); r.Advance(0.2f, 1f, 4f); Check(r.Active && !r.Returning); Equal(0.05f, r.Progress); },
            ["Simultaneous multibarrel shots stay one sweep"] = () => { var r = new RingMotion(); r.Fire(); r.Fire(); r.Fire(); Check(r.Active && !r.Returning); r.Advance(0.1f, 1f, 4f); Equal(0.1f, r.Progress); },
            ["Finished ring disappears and next shot restarts"] = () => { var r = new RingMotion(); r.Fire(); r.Advance(1f, 1f, 6f); Check(!r.Active); r.Fire(); Check(r.Active && !r.Returning); Equal(0, r.Progress); },
            ["Long frame consumes return and entire sweep"] = () => { var r = new RingMotion(); r.Fire(); r.Advance(0.5f, 1f, 6f); r.Fire(); r.Advance(3f, 1f, 6f); Check(!r.Active); },
            ["Live timing changes take effect"] = () => { var r = new RingMotion(); r.Fire(); r.Advance(0.1f, 1f, 6f); r.Advance(0.1f, 0.5f, 6f); Equal(0.3f, r.Progress); },
            ["Disabled or switched weapon stops cleanly"] = () => { var r = new RingMotion(); r.Fire(); r.Advance(0.4f, 1f, 6f); r.Fire(); r.Stop(); Check(!r.Active && !r.Returning); Equal(0, r.Progress); },
            ["Paused or invalid delta does not advance"] = () => { var r = new RingMotion(); r.Fire(); r.Advance(0, 1, 6); r.Advance(-1, 1, 6); r.Advance(float.NaN, 1, 6); r.Advance(float.PositiveInfinity, 1, 6); Equal(0, r.Progress); },
            ["RGB and RGBA maps"] = () => { var c = RingColorMap.Parse("{\"tpl1\":\"#2694dA\",\"tpl2\":\"#ca1f2b80\"}"); Check(c.Count == 2); Check(c["tpl1"].R == 0x26 && c["tpl1"].B == 0xda && c["tpl1"].A == 255); Check(c["tpl2"].A == 128); },
            ["Empty map enables no weapons"] = () => Check(RingColorMap.Parse("{}").Count == 0),
            ["Duplicate weapon IDs rejected"] = () => Reject("{\"tpl\":\"#ffffff\",\"tpl\":\"#000000\"}"),
            ["Malformed partial document rejected"] = () => Reject("{\"tpl\":\"#ffffff\""),
            ["Non-map JSON rejected"] = () => Reject("[]"),
            ["Non-string colors rejected"] = () => Reject("{\"tpl\":123}"),
            ["Bad hex rejected"] = () => Reject("{\"tpl\":\"#gg0000\"}"),
            ["Short hex rejected"] = () => Reject("{\"tpl\":\"#fff\"}"),
            ["Whitespace IDs rejected"] = () => Reject("{\" tpl \":\"#ffffff\"}"),
            ["Transparent colors supported"] = () => Check(RingColorMap.Parse("{\"tpl\":\"#ffffff00\"}")["tpl"].A == 0)
        };
        foreach (var test in VariantColorsChecks.Cases) tests.Add(test.Key, test.Value);
        foreach (var test in IdleRingChecks.Cases) tests.Add(test.Key, test.Value);
        foreach (var test in ItemColorsChecks.Cases) tests.Add(test.Key, test.Value);
        foreach (var test in ItemGradientChecks.Cases) tests.Add(test.Key, test.Value);
        int failures = 0;
        foreach (var test in tests)
        {
            try { test.Value(); Console.WriteLine("PASS " + test.Key); }
            catch (Exception e) { failures++; Console.WriteLine("FAIL " + test.Key + ": " + e.Message); }
        }
        Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed.");
        return failures == 0 ? 0 : 1;
    }
}
