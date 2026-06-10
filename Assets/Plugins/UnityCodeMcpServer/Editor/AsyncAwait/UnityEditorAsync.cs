using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace UnityCodeMcpServer.AsyncAwait
{
    /// <summary>
    /// Focused editor await helpers for realtime delays and frame/update waits.
    /// </summary>
    public static class UnityEditorAsync
    {
        public static Task DelayRealtimeAsync(int millisecondsDelay, CancellationToken cancellationToken = default)
        {
            return DelayRealtimeAsync(TimeSpan.FromMilliseconds(millisecondsDelay), cancellationToken);
        }

        public static async Task DelayRealtimeAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            if (delay <= TimeSpan.Zero)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            await UnityMainThread.SwitchAsync(cancellationToken);
            double deadline = EditorApplication.timeSinceStartup + delay.TotalSeconds;
            await WaitForEditorUpdateAsync(
                () => EditorApplication.timeSinceStartup >= deadline,
                cancellationToken);
        }

        public static Task YieldAsync(CancellationToken cancellationToken = default)
        {
            return DelayFramesAsync(1, cancellationToken);
        }

        public static async Task DelayFramesAsync(int frameCount, CancellationToken cancellationToken = default)
        {
            if (frameCount <= 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            await UnityMainThread.SwitchAsync(cancellationToken);

            int remainingFrames = frameCount;
            await WaitForEditorUpdateAsync(
                () =>
                {
                    remainingFrames--;
                    return remainingFrames <= 0;
                },
                cancellationToken);
        }

        private static Task WaitForEditorUpdateAsync(Func<bool> shouldComplete, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            TaskCompletionSource<object> completionSource = new();
            CancellationTokenRegistration cancellationRegistration = default;
            EditorApplication.CallbackFunction tick = null;

            void Complete(Action complete)
            {
                EditorApplication.update -= tick;
                cancellationRegistration.Dispose();
                complete();
            }

            tick = () =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Complete(() => completionSource.TrySetCanceled(cancellationToken));
                    return;
                }

                if (shouldComplete())
                {
                    Complete(() => completionSource.TrySetResult(null));
                }
            };

            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(() =>
                {
                    UnityMainThread.Post(() => Complete(() => completionSource.TrySetCanceled(cancellationToken)));
                });
            }

            EditorApplication.update += tick;
            return completionSource.Task;
        }
    }
}
