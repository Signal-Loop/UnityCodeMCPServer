using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityCodeMcpServer.AsyncAwait
{
    public static class UnityPlayerLoopAsync
    {
        private static Runner _runner;

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
            await RunCoroutine(DelayFramesCoroutine(frameCount, cancellationToken), cancellationToken);
        }

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
            float deadline = Time.realtimeSinceStartup + (float)delay.TotalSeconds;
            await RunCoroutine(DelayRealtimeCoroutine(deadline, cancellationToken), cancellationToken);
        }

        private static Task RunCoroutine(IEnumerator coroutine, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            TaskCompletionSource<object> completionSource = new();
            EnsureRunner().StartCoroutine(CompleteWhenDone(coroutine, completionSource, cancellationToken));
            return completionSource.Task;
        }

        private static IEnumerator DelayFramesCoroutine(int frameCount, CancellationToken cancellationToken)
        {
            int remainingFrames = frameCount;
            while (remainingFrames > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                remainingFrames--;
                yield return null;
            }
        }

        private static IEnumerator DelayRealtimeCoroutine(float deadline, CancellationToken cancellationToken)
        {
            while (Time.realtimeSinceStartup < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return null;
            }
        }

        private static IEnumerator CompleteWhenDone(
            IEnumerator coroutine,
            TaskCompletionSource<object> completionSource,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                object current;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!coroutine.MoveNext())
                    {
                        completionSource.TrySetResult(null);
                        yield break;
                    }

                    current = coroutine.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completionSource.TrySetCanceled(cancellationToken);
                    yield break;
                }
                catch (Exception ex)
                {
                    completionSource.TrySetException(ex);
                    yield break;
                }

                yield return current;
            }
        }

        private static Runner EnsureRunner()
        {
            if (_runner != null)
            {
                return _runner;
            }

            GameObject gameObject = new("UnityPlayerLoopAsyncRunner")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            _runner = gameObject.AddComponent<Runner>();
            return _runner;
        }

        private sealed class Runner : MonoBehaviour
        {
        }
    }
}

