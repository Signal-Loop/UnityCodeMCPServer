using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityCodeMcpServer.Helpers;
using UnityEditor;

namespace UnityCodeMcpServer.AsyncAwait
{
    /// <summary>
    /// Minimal Unity Editor main-thread dispatcher for async/await code.
    /// </summary>
    [InitializeOnLoad]
    public static class UnityMainThread
    {
        private static readonly object QueueLock = new();
        private static readonly Queue<Action> PendingActions = new();
        private static int _mainThreadId;
        private static bool _hasObservedEditorUpdate;

        static UnityMainThread()
        {
            EditorApplication.update -= DrainQueue;
            EditorApplication.update += DrainQueue;
        }

        public static bool IsMainThread =>
            HasUnitySynchronizationContext()
            || (_hasObservedEditorUpdate && Thread.CurrentThread.ManagedThreadId == _mainThreadId);

        public static Task SwitchAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            if (IsMainThread)
            {
                return Task.CompletedTask;
            }

            TaskCompletionSource<object> completionSource = new();
            CancellationTokenRegistration cancellationRegistration = default;

            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(
                    () => completionSource.TrySetCanceled(cancellationToken));
            }

            Post(() =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completionSource.TrySetCanceled(cancellationToken);
                    }
                    else
                    {
                        completionSource.TrySetResult(null);
                    }
                }
                finally
                {
                    cancellationRegistration.Dispose();
                }
            });

            return completionSource.Task;
        }

        public static Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            if (IsMainThread)
            {
                try
                {
                    return action();
                }
                catch (Exception ex)
                {
                    return Task.FromException<T>(ex);
                }
            }

            TaskCompletionSource<T> completionSource = new();
            CancellationTokenRegistration cancellationRegistration = default;

            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(
                    () => completionSource.TrySetCanceled(cancellationToken));
            }

            Post(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    T result = await action();
                    completionSource.TrySetResult(result);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completionSource.TrySetCanceled(cancellationToken);
                }
                catch (Exception ex)
                {
                    completionSource.TrySetException(ex);
                }
                finally
                {
                    cancellationRegistration.Dispose();
                }
            });

            return completionSource.Task;
        }

        public static void Post(Action action)
        {
            if (action == null)
            {
                return;
            }

            lock (QueueLock)
            {
                PendingActions.Enqueue(action);
            }
        }

        private static void DrainQueue()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _hasObservedEditorUpdate = true;

            while (true)
            {
                Action action;
                lock (QueueLock)
                {
                    if (PendingActions.Count == 0)
                    {
                        return;
                    }

                    action = PendingActions.Dequeue();
                }

                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    UnityCodeMcpServerLogger.Error($"[UnityMainThread] Queued action failed: {ex}");
                }
            }
        }

        private static bool HasUnitySynchronizationContext()
        {
            string contextName = SynchronizationContext.Current?.GetType().FullName;
            return string.Equals(contextName, "UnityEngine.UnitySynchronizationContext", StringComparison.Ordinal);
        }
    }
}
