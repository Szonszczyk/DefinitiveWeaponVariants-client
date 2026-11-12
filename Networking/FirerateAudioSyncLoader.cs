using System;
using System.Threading;
using System.Threading.Tasks;

namespace DefinitiveWeaponVariants.Client.Networking
{
    internal sealed class FirerateAudioSyncLoader
    {
        internal const string Endpoint = "/definitiveweaponvariants/firerate-audio-sync";
        private readonly Func<string, Task<string>> _getJson;
        private readonly TimeSpan _timeout;
        private Task<FirerateAudioSyncPayload> _load;

        internal FirerateAudioSyncLoader(Func<string, Task<string>> getJson, TimeSpan? timeout = null)
        {
            _getJson = getJson ?? throw new ArgumentNullException(nameof(getJson));
            _timeout = timeout ?? TimeSpan.FromSeconds(15);
        }

        internal Task<FirerateAudioSyncPayload> LoadOnceAsync()
        {
            if (_load == null)
            {
                _load = LoadAsync();
                ObserveFault(_load);
            }
            return _load;
        }

        private async Task<FirerateAudioSyncPayload> LoadAsync()
        {
            Task<string> request = _getJson(Endpoint);
            if (request == null) throw new InvalidOperationException("SPT returned no FirerateAudioSync request Task.");
            ObserveFault(request);
            using (var deadline = new CancellationTokenSource())
            {
                if (await Task.WhenAny(request, Task.Delay(_timeout, deadline.Token)).ConfigureAwait(false) != request)
                    throw new TimeoutException("FirerateAudioSync did not arrive within the startup deadline.");
                deadline.Cancel();
            }
            return FirerateAudioSyncPayload.Parse(await request.ConfigureAwait(false));
        }

        private static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
