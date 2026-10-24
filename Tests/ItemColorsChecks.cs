using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DefinitiveWeaponVariants.Client.Networking;

internal static class ItemColorsChecks
{
    private const string Id = "0123456789abcdef01234567";
    private const string Header = "{\"schemaVersion\":1,\"serverModVersion\":\"5.0.0\",\"itemColors\":";
    private const string SolidMap = "{\"" + Id + "\":[\"#2694da80\"]}";
    private const string GradientMap = "{\"" + Id + "\":[\"#ff0000\",\"#0000ff80\"]}";
    private const string Solid = Header + SolidMap + "}";
    private const string Gradient = Header + GradientMap + "}";
    private const string Empty = Header + "{}}";
    private static void Check(bool value) { if (!value) throw new Exception("Item colors assertion failed."); }
    private static void Equal(float expected, float actual) => Check(Math.Abs(expected - actual) < 0.0001f);
    private static void Reject(string json)
    {
        try { ItemColorsPayload.Parse(json); }
        catch { return; }
        throw new Exception("Invalid item colors accepted: " + json);
    }
    private static Action Async(Func<Task> test) => () => test().GetAwaiter().GetResult();
    private static async Task Fails<T>(Task task) where T : Exception
    {
        try { await task; }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    internal static Dictionary<string, Action> Cases => new Dictionary<string, Action>
    {
        ["Item endpoint uses the same versioned envelope as variant colors"] = () =>
        {
            var payload = ItemColorsPayload.Parse(Solid);
            var map = payload.Colors;
            Check(payload.ServerModVersion == "5.0.0");
            Check(map.Count == 1 && map.ContainsKey(Id) && !map.ContainsKey("5734779624597737e04bf329"));
            Check(map[Id].First.R == 0x26 && map[Id].First.A == 128);
            Check(ItemColorsPayload.Parse(Empty).Colors.Count == 0);
        },
        ["Item envelope accepts compatible extra fields"] = () =>
            Check(ItemColorsPayload.Parse(Solid.Insert(1, "\"futureEffect\":{\"enabled\":true},")).Colors.Count == 1),
        ["Item envelope rejects missing, unsupported and wrongly typed schemas"] = () =>
        {
            Reject(Solid.Replace("\"schemaVersion\":1,", ""));
            foreach (string schema in new[] { "2", "\"1\"", "1.0", "null", "true" })
                Reject(Solid.Replace("\"schemaVersion\":1", "\"schemaVersion\":" + schema));
        },
        ["Item envelope requires server version and an itemColors object"] = () =>
        {
            Reject(Solid.Replace("\"serverModVersion\":\"5.0.0\",", ""));
            foreach (string version in new[] { "null", "42", "\" \"" })
                Reject(Solid.Replace("\"5.0.0\"", version));
            foreach (string data in new[] { "null", "[]", "42", "\"colors\"" })
                Reject(Header + data + "}");
            Reject(Solid.Replace("itemColors", "variantColors"));
            Reject("{\"schemaVersion\":1,\"serverModVersion\":\"5.0.0\"}");
            Reject(SolidMap); Reject("{}");
        },
        ["Item envelope rejects duplicate metadata and itemColors fields"] = () =>
        {
            Reject(Solid.Insert(1, "\"schemaVersion\":1,"));
            Reject(Solid.Insert(1, "\"serverModVersion\":\"5.0.0\","));
            Reject(Solid.Insert(1, "\"itemColors\":{},"));
        },
        ["One item color stays uniform across the entire gradient"] = () =>
        {
            var palette = ItemColorsPayload.Parse(Solid).Colors[Id];
            foreach (float position in new[] { 0f, 0.5f, 1f })
            {
                palette.Sample(position, 0.3f, out float r, out float g, out float b, out float a);
                Equal(0x26 / 255f, r); Equal(0x94 / 255f, g); Equal(0xda / 255f, b); Equal(128 / 255f * 0.3f, a);
            }
        },
        ["Two item colors preserve endpoints and interpolate midpoint alpha"] = () =>
        {
            var palette = ItemColorsPayload.Parse(Gradient).Colors[Id];
            palette.Sample(0, 1, out float r, out float g, out float b, out float a);
            Equal(1, r); Equal(0, g); Equal(0, b); Equal(1, a);
            palette.Sample(1, 1, out r, out g, out b, out a);
            Equal(0, r); Equal(0, g); Equal(1, b); Equal(128 / 255f, a);
            palette.Sample(0.5f, 0.4f, out r, out g, out b, out a);
            Equal(0.5f, r); Equal(0, g); Equal(0.5f, b); Equal((255 + 128) / 510f * 0.4f, a);
            palette.Sample(0.5f, 0, out _, out _, out _, out a); Equal(0, a);
        },
        ["Item colors reject invalid counts and non-list values"] = () =>
        {
            foreach (string value in new[] { "[]", "[\"#ffffff\",\"#000000\",\"#ff0000\"]", "null", "42", "\"#ffffff\"", "{}" })
                Reject(Header + "{\"" + Id + "\":" + value + "}}");
        },
        ["Item colors reject a malformed second color rather than partially styling"] = () =>
        {
            Reject(Gradient.Replace("#0000ff80", "#bad"));
            Reject(Gradient.Replace("\"#0000ff80\"", "null"));
            Reject(Gradient.Replace("#ff0000", "#zz0000"));
        },
        ["Item colors reject duplicates, invalid IDs and transport error envelopes"] = () =>
        {
            Reject(Solid.Replace(Id, " ")); Reject(Solid.Replace(Id, " " + Id));
            Reject(Solid.Replace(Id, "short")); Reject(Solid.Replace(Id, "0123456789abcdef0123456z"));
            Reject(Solid.Replace("\"itemColors\":{", "\"itemColors\":{\"" + Id + "\":[\"#ffffff\"],"));
            Reject("{\"err\":404,\"errmsg\":\"Route missing\",\"data\":null}");
            Reject("{\"err\":0,\"errmsg\":null,\"data\":" + Solid + "}");
            Reject("[]"); Reject("null"); Reject("<html>Not found</html>"); Reject(Solid.Substring(0, Solid.Length - 1));
        },
        ["Item catalog loads once from the requested route and waits for completion"] = Async(async () =>
        {
            int calls = 0;
            var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loader = new ItemColorsLoader(path => { Check(path == "/definitiveweaponvariants/item-colors"); calls++; return response.Task; });
            var load = loader.LoadOnceAsync();
            Check(!load.IsCompleted && ReferenceEquals(load, loader.LoadOnceAsync()) && calls == 1);
            response.SetResult(Gradient);
            var payload = await load;
            Check(payload.ServerModVersion == "5.0.0" && payload.Colors.Count == 1 && ReferenceEquals(load, loader.LoadOnceAsync()) && calls == 1);
        }),
        ["Item catalog invalid envelope faults once without exposing colors"] = Async(async () =>
        {
            int calls = 0;
            var loader = new ItemColorsLoader(_ => { calls++; return Task.FromResult(Solid.Replace("\"schemaVersion\":1", "\"schemaVersion\":2")); });
            var load = loader.LoadOnceAsync();
            await Fails<FormatException>(load);
            Check(ReferenceEquals(load, loader.LoadOnceAsync()) && calls == 1);
        }),
        ["Item catalog transport failure does not retry"] = Async(async () =>
        {
            int calls = 0;
            var loader = new ItemColorsLoader(_ => { calls++; throw new InvalidOperationException("Offline"); });
            var load = loader.LoadOnceAsync();
            await Fails<InvalidOperationException>(load);
            Check(ReferenceEquals(load, loader.LoadOnceAsync()) && calls == 1);
        }),
        ["Item catalog times out and never applies a late response"] = Async(async () =>
        {
            var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loader = new ItemColorsLoader(_ => response.Task, TimeSpan.FromMilliseconds(20));
            var load = loader.LoadOnceAsync();
            await Fails<TimeoutException>(load);
            response.SetResult(Gradient);
            Check(ReferenceEquals(load, loader.LoadOnceAsync()) && load.IsFaulted);
        })
    };
}
