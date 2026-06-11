using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace UnityCodeMcpServer.AsyncAwait
{
    /// <summary>
    /// Small player-loop awaiter for play-mode work that must resume before MonoBehaviour.Update.
    /// </summary>
    public static class UnityPlayerLoopAsync
    {
        private static readonly object PendingLock = new();
        private static readonly List<PlayerLoopDelaySource> PendingSources = new();
        private static readonly List<PlayerLoopDelaySource> RunningSources = new();

        public static PlayerLoopDelayAwaitable YieldAsync(CancellationToken cancellationToken = default)
        {
            return DelayFramesAsync(1, cancellationToken);
        }

        public static PlayerLoopDelayAwaitable DelayFramesAsync(int frameCount, CancellationToken cancellationToken = default)
        {
            return new PlayerLoopDelayAwaitable(PlayerLoopDelaySource.ForFrames(frameCount, cancellationToken));
        }

        public static PlayerLoopDelayAwaitable DelayRealtimeAsync(int millisecondsDelay, CancellationToken cancellationToken = default)
        {
            return DelayRealtimeAsync(TimeSpan.FromMilliseconds(millisecondsDelay), cancellationToken);
        }

        public static PlayerLoopDelayAwaitable DelayRealtimeAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            return new PlayerLoopDelayAwaitable(PlayerLoopDelaySource.ForRealtime(delay, cancellationToken));
        }

        internal static void Schedule(PlayerLoopDelaySource source)
        {
            if (source == null)
            {
                return;
            }

            if (!UnityMainThread.IsMainThread)
            {
                UnityMainThread.Post(() => Schedule(source));
                return;
            }

            source.PrepareForPlayerLoop();
            if (source.IsCompleted)
            {
                source.InvokeContinuation();
                return;
            }

            EnsurePlayerLoopInjected();
            lock (PendingLock)
            {
                PendingSources.Add(source);
            }
        }

        private static void Update()
        {
            lock (PendingLock)
            {
                if (PendingSources.Count > 0)
                {
                    RunningSources.AddRange(PendingSources);
                    PendingSources.Clear();
                }
            }

            for (int i = RunningSources.Count - 1; i >= 0; i--)
            {
                PlayerLoopDelaySource source = RunningSources[i];
                if (!source.MoveNext())
                {
                    RunningSources.RemoveAt(i);
                    source.InvokeContinuation();
                }
            }
        }

        private static void EnsurePlayerLoopInjected()
        {
            PlayerLoopSystem playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            if (HasPlayerLoopSystem(playerLoop, typeof(UnityPlayerLoopAsync)))
            {
                return;
            }

            if (!InsertIntoUpdateLoop(ref playerLoop))
            {
                return;
            }

            PlayerLoop.SetPlayerLoop(playerLoop);
        }

        private static bool InsertIntoUpdateLoop(ref PlayerLoopSystem playerLoop)
        {
            PlayerLoopSystem[] systems = playerLoop.subSystemList;
            if (systems == null)
            {
                return false;
            }

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i].type != typeof(UnityEngine.PlayerLoop.Update))
                {
                    continue;
                }

                List<PlayerLoopSystem> updateSystems = systems[i].subSystemList != null
                    ? new List<PlayerLoopSystem>(systems[i].subSystemList)
                    : new List<PlayerLoopSystem>();

                updateSystems.Insert(0, new PlayerLoopSystem
                {
                    type = typeof(UnityPlayerLoopAsync),
                    updateDelegate = Update
                });

                systems[i].subSystemList = updateSystems.ToArray();
                playerLoop.subSystemList = systems;
                return true;
            }

            return false;
        }

        private static bool HasPlayerLoopSystem(PlayerLoopSystem system, Type type)
        {
            if (system.type == type)
            {
                return true;
            }

            PlayerLoopSystem[] systems = system.subSystemList;
            if (systems == null)
            {
                return false;
            }

            foreach (PlayerLoopSystem child in systems)
            {
                if (HasPlayerLoopSystem(child, type))
                {
                    return true;
                }
            }

            return false;
        }

        public readonly struct PlayerLoopDelayAwaitable
        {
            private readonly PlayerLoopDelaySource source;

            internal PlayerLoopDelayAwaitable(PlayerLoopDelaySource source)
            {
                this.source = source;
            }

            public Awaiter GetAwaiter()
            {
                return new Awaiter(source);
            }
        }

        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            private readonly PlayerLoopDelaySource source;

            internal Awaiter(PlayerLoopDelaySource source)
            {
                this.source = source;
            }

            public bool IsCompleted => source.IsCompleted;

            public void GetResult()
            {
                source.GetResult();
            }

            public void OnCompleted(Action continuation)
            {
                UnsafeOnCompleted(continuation);
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                source.SetContinuation(continuation);
                Schedule(source);
            }
        }

        internal sealed class PlayerLoopDelaySource
        {
            private enum DelayKind
            {
                Completed,
                Frames,
                Realtime
            }

            private readonly CancellationToken cancellationToken;
            private readonly DelayKind kind;
            private readonly int frameCount;
            private readonly double delaySeconds;
            private CancellationTokenRegistration cancellationRegistration;
            private Action continuation;
            private Exception exception;
            private bool isCompleted;
            private int currentFrameCount;
            private int initialFrame;
            private float deadline;

            private PlayerLoopDelaySource(
                DelayKind kind,
                int frameCount,
                double delaySeconds,
                CancellationToken cancellationToken)
            {
                this.kind = kind;
                this.frameCount = frameCount;
                this.delaySeconds = delaySeconds;
                this.cancellationToken = cancellationToken;
                isCompleted = kind == DelayKind.Completed || cancellationToken.IsCancellationRequested;

                if (cancellationToken.IsCancellationRequested)
                {
                    exception = new OperationCanceledException(cancellationToken);
                }
            }

            public bool IsCompleted => isCompleted;

            public static PlayerLoopDelaySource ForFrames(int frameCount, CancellationToken cancellationToken)
            {
                return new PlayerLoopDelaySource(
                    frameCount <= 0 ? DelayKind.Completed : DelayKind.Frames,
                    frameCount,
                    0d,
                    cancellationToken);
            }

            public static PlayerLoopDelaySource ForRealtime(TimeSpan delay, CancellationToken cancellationToken)
            {
                return new PlayerLoopDelaySource(
                    delay <= TimeSpan.Zero ? DelayKind.Completed : DelayKind.Realtime,
                    0,
                    delay.TotalSeconds,
                    cancellationToken);
            }

            public void SetContinuation(Action value)
            {
                continuation = value;

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationRegistration = cancellationToken.Register(() =>
                    {
                        UnityMainThread.Post(() =>
                        {
                            if (TryComplete(new OperationCanceledException(cancellationToken)))
                            {
                                InvokeContinuation();
                            }
                        });
                    });
                }
            }

            public void PrepareForPlayerLoop()
            {
                if (isCompleted)
                {
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    TryComplete(new OperationCanceledException(cancellationToken));
                    return;
                }

                initialFrame = Time.frameCount;
                if (kind == DelayKind.Realtime)
                {
                    deadline = Time.realtimeSinceStartup + (float)delaySeconds;
                }
            }

            public bool MoveNext()
            {
                if (isCompleted)
                {
                    return false;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    TryComplete(new OperationCanceledException(cancellationToken));
                    return false;
                }

                if (kind == DelayKind.Realtime)
                {
                    if (Time.realtimeSinceStartup >= deadline)
                    {
                        TryComplete(null);
                        return false;
                    }

                    return true;
                }

                if (currentFrameCount == 0 && initialFrame == Time.frameCount)
                {
                    if (EditorApplication.isPlaying)
                    {
                        return true;
                    }
                }

                currentFrameCount++;
                if (currentFrameCount >= frameCount)
                {
                    TryComplete(null);
                    return false;
                }

                return true;
            }

            public void InvokeContinuation()
            {
                cancellationRegistration.Dispose();
                Action action = continuation;
                continuation = null;
                action?.Invoke();
            }

            public void GetResult()
            {
                if (exception != null)
                {
                    throw exception;
                }
            }

            private bool TryComplete(Exception completionException)
            {
                if (isCompleted)
                {
                    return false;
                }

                exception = completionException;
                isCompleted = true;
                return true;
            }
        }
    }
}
