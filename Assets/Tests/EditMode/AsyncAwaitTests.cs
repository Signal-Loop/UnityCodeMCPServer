using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityCodeMcpServer.AsyncAwait;
using UnityEngine;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class AsyncAwaitTests
    {
        [Test]
        public async Task RunAsync_FromThreadPool_ExecutesUnityApiOnMainThread()
        {
            bool isPlaying = await Task.Run(async () =>
                await UnityMainThread.RunAsync(() => Task.FromResult(Application.isPlaying)));

            Assert.That(isPlaying, Is.EqualTo(Application.isPlaying));
        }

        [Test]
        public void RunAsync_WhenCanceledBeforeDispatch_ReturnsCanceledTask()
        {
            using CancellationTokenSource cts = new();
            cts.Cancel();

            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await UnityMainThread.RunAsync(() => Task.FromResult(1), cts.Token));
        }

        [Test]
        public void DelayRealtimeAsync_WhenCanceledBeforeDelay_ReturnsCanceledTask()
        {
            using CancellationTokenSource cts = new();
            cts.Cancel();

            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await UnityEditorAsync.DelayRealtimeAsync(10, cts.Token));
        }
    }
}
