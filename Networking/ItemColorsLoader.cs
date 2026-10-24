using System;
using System.Threading;
using System.Threading.Tasks;

namespace DefinitiveWeaponVariants.Client.Networking
{
    // Pure managed startup operation; it never touches Unity objects or active effect state.
    internal sealed class ItemColorsLoader
    {
        internal const string Endpoint = "/definitiveweaponvariants/item-colors";
        private readonly Func<string, Task<string>> _getJson;
        private readonly TimeSpan _timeout;
        private Task<ItemColorsPayload> _load;

        internal ItemColorsLoader(Func<string, Task<string>> getJson, TimeSpan? timeout = null)
        {
            _getJson = getJson ?? throw new ArgumentNullException(nameof(getJson));
            _timeout = timeout ?? TimeSpan.FromSeconds(15);
        }

        // Called on the Unity thread. Failure is cached too: no retries on later frames/raids.
        internal Task<ItemColorsPayload> LoadOnceAsync()
        {
            if (_load == null)
            {
                _load = LoadAsync();
                // The plugin/coroutine may be destroyed before completion.
                ObserveFault(_load);
            }
            return _load;
        }

        private async Task<ItemColorsPayload> LoadAsync()
        {
            // A synchronous transport/static-initializer exception also faults this Task.
            Task<string> request = _getJson(Endpoint);
            if (request == null) throw new InvalidOperationException("SPT returned no item-colors request Task.");
            using (var deadline = new CancellationTokenSource())
            {
                Task delay = Task.Delay(_timeout, deadline.Token);
                if (await Task.WhenAny(request, delay).ConfigureAwait(false) != request)
                {
                    // SPT's API has no cancellation token. Never apply a late result, and
                    // observe a late failure without touching its shared HttpClient settings.
                    ObserveFault(request);
                    throw new TimeoutException("Item colors did not arrive within the startup deadline.");
                }
                deadline.Cancel();
            }
            return ItemColorsPayload.Parse(await request.ConfigureAwait(false));
        }

        private static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(completed => { _ = completed.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}

