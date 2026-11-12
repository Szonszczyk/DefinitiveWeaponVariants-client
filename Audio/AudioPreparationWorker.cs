using System;
using System.Threading;
using System.Threading.Tasks;

namespace DefinitiveWeaponVariants.Client.Audio
{
    internal static class AudioPreparationWorker
    {
        internal static Task<T> Run<T>(Func<T> prepare, CancellationToken cancellation) => Task.Run(() =>
        {
            var thread = Thread.CurrentThread;
            ThreadPriority previous = thread.Priority;
            try
            {
                thread.Priority = ThreadPriority.BelowNormal;
                cancellation.ThrowIfCancellationRequested();
                return prepare();
            }
            finally { thread.Priority = previous; }
        }, cancellation);
    }
}
