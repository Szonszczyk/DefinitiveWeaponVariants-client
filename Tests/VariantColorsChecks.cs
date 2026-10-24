using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DefinitiveWeaponVariants.Client.Networking;

internal static class VariantColorsChecks
{
    private const string Id = "0123456789abcdef01234567";
    private const string Valid = "{\"schemaVersion\":1,\"serverModVersion\":\"5.0.0\",\"variantColors\":{\"" + Id + "\":\"#2694da80\"}}";
    private const string Empty = "{\"schemaVersion\":1,\"serverModVersion\":\"5.0.0\",\"variantColors\":{}}";

    private static void Check(bool value) { if (!value) throw new Exception("Variant colors assertion failed."); }
    private static void Reject(string json)
    {
        try { VariantColorsPayload.Parse(json); }
        catch (Exception) { return; }
        throw new Exception("Invalid server payload was accepted: " + json);
    }
    private static async Task Fails<T>(Task task) where T : Exception
    {
        try { await task; }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static Action Async(Func<Task> test) => () => test().GetAwaiter().GetResult();

    internal static Dictionary<string, Action> Cases => new Dictionary<string, Action>
    {
        ["Server variantColors contract preserves version and RGBA"] = () =>
        {
            var payload = VariantColorsPayload.Parse(Valid);
            Check(payload.ServerModVersion == "5.0.0" && payload.Colors.Count == 1);
            Check(payload.Colors[Id].R == 0x26 && payload.Colors[Id].B == 0xda && payload.Colors[Id].A == 0x80);
        },
        ["Server empty catalog is a successful response"] = () => Check(VariantColorsPayload.Parse(Empty).Colors.Count == 0),
        ["Future compatible server fields are ignored"] = () => Check(VariantColorsPayload.Parse(Valid.Insert(1, "\"futureEffect\":{\"enabled\":true},")).Colors.Count == 1),
        ["Missing or unsupported schema rejected"] = () =>
        {
            Reject(Valid.Replace("\"schemaVersion\":1,", ""));
            Reject(Valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"));
            Reject(Valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":\"1\""));
            Reject(Valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1.0"));
        },
        ["Missing or wrongly typed server fields rejected"] = () =>
        {
            Reject(Valid.Replace("\"serverModVersion\":\"5.0.0\",", ""));
            Reject(Valid.Replace("\"5.0.0\"", "null"));
            Reject(Valid.Replace("\"5.0.0\"", "\" \""));
            Reject(Empty.Replace("\"variantColors\":{}", "\"variantColors\":[]"));
            Reject(Empty.Replace("\"variantColors\":{}", "\"variantColors\":null"));
            Reject(Empty.Replace(",\"variantColors\":{}", ""));
        },
        ["SPT error envelopes and non-JSON responses rejected"] = () =>
        {
            Reject("{\"err\":404,\"errmsg\":\"Route missing\",\"data\":null}");
            Reject("{\"err\":0,\"errmsg\":null,\"data\":" + Valid + "}");
            Reject("<html>Not found</html>"); Reject("null"); Reject("[]"); Reject("");
        },
        ["Duplicate envelope fields and weapon IDs rejected"] = () =>
        {
            Reject(Valid.Insert(1, "\"schemaVersion\":1,"));
            Reject(Valid.Replace("\"#2694da80\"", "\"#2694da80\",\"" + Id + "\":\"#ffffff\""));
        },
        ["Malformed template IDs rejected"] = () =>
        {
            Reject(Valid.Replace(Id, "short"));
            Reject(Valid.Replace(Id, "0123456789abcdef0123456z"));
            Reject(Valid.Replace(Id, " " + Id));
        },
        ["One invalid server color rejects the complete response"] = () =>
        {
            Reject(Valid.Replace("\"#2694da80\"", "\"#2694da80\",\"89abcdef0123456789abcdef\":\"#bad\""));
            Reject(Valid.Replace("\"#2694da80\"", "123"));
        },
        ["Only one bootstrap request while pending and after success"] = Async(async () =>
        {
            int calls = 0;
            var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loader = new VariantColorsLoader(path => { Check(path == VariantColorsLoader.Endpoint); calls++; return response.Task; });
            var first = loader.LoadOnceAsync();
            Check(ReferenceEquals(first, loader.LoadOnceAsync()) && calls == 1 && !first.IsCompleted);
            response.SetResult(Valid);
            Check((await first).Colors.Count == 1);
            Check(ReferenceEquals(first, loader.LoadOnceAsync()) && calls == 1);
        }),
        ["Synchronous transport failure is contained and never retried"] = Async(async () =>
        {
            int calls = 0;
            var loader = new VariantColorsLoader(_ => { calls++; throw new InvalidOperationException("No launcher config"); });
            var load = loader.LoadOnceAsync();
            await Fails<InvalidOperationException>(load);
            Check(ReferenceEquals(load, loader.LoadOnceAsync()) && calls == 1);
        }),
        ["Asynchronous request failure is never retried"] = Async(async () =>
        {
            int calls = 0;
            var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loader = new VariantColorsLoader(_ => { calls++; return response.Task; });
            var load = loader.LoadOnceAsync();
            response.SetException(new InvalidOperationException("Unavailable server"));
            await Fails<InvalidOperationException>(load);
            Check(ReferenceEquals(load, loader.LoadOnceAsync()) && calls == 1);
        }),
        ["Cancelled transport is contained and never retried"] = Async(async () =>
        {
            var response = new TaskCompletionSource<string>();
            var loader = new VariantColorsLoader(_ => response.Task);
            var load = loader.LoadOnceAsync();
            response.SetCanceled();
            await Fails<OperationCanceledException>(load);
            Check(ReferenceEquals(load, loader.LoadOnceAsync()));
        }),
        ["Malformed response cannot turn a failed bootstrap into success"] = Async(async () =>
        {
            int calls = 0;
            var loader = new VariantColorsLoader(_ => { calls++; return Task.FromResult(Empty.Replace("\"schemaVersion\":1", "\"schemaVersion\":2")); });
            var load = loader.LoadOnceAsync();
            await Fails<FormatException>(load);
            Check(ReferenceEquals(load, loader.LoadOnceAsync()) && calls == 1);
        }),
        ["Startup deadline ignores a late successful response"] = Async(async () =>
        {
            int calls = 0;
            var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loader = new VariantColorsLoader(_ => { calls++; return response.Task; }, TimeSpan.FromMilliseconds(10));
            var load = loader.LoadOnceAsync();
            await Fails<TimeoutException>(load);
            response.SetResult(Valid);
            Check(load.IsFaulted && ReferenceEquals(load, loader.LoadOnceAsync()) && calls == 1);
        }),
        ["Late transport failure leaves the timed-out bootstrap failed"] = Async(async () =>
        {
            var response = new TaskCompletionSource<string>();
            var loader = new VariantColorsLoader(_ => response.Task, TimeSpan.FromMilliseconds(10));
            var load = loader.LoadOnceAsync();
            await Fails<TimeoutException>(load);
            response.SetException(new InvalidOperationException("Late request failure"));
            Check(load.IsFaulted && ReferenceEquals(load, loader.LoadOnceAsync()));
        })
    };
}
