using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityCodeMcpServer.Servers.StreamableHttp;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class UnityCodeMcpHttpServerTests
    {
        private static int GetEphemeralPort()
        {
            TcpListener probe = new(IPAddress.Loopback, 0);
            probe.Start();

            try
            {
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        [Test]
        public void StopServer_AcceptsShutdownReasonParameter()
        {
            MethodInfo method = typeof(UnityCodeMcpHttpServer).GetMethod(
                "StopServer",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            Assert.That(method, Is.Not.Null, "StopServer(string reason) overload should exist");
        }

        [Test]
        public void RestartServer_PublicMethodStillExists()
        {
            MethodInfo method = typeof(UnityCodeMcpHttpServer).GetMethod(
                "RestartServer",
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(method, Is.Not.Null, "RestartServer() should still exist");
        }

        [Test]
        public void UsesLoopbackTransportField_InsteadOfHttpListenerField()
        {
            FieldInfo transportField = typeof(UnityCodeMcpHttpServer).GetField(
                "_transport",
                BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo listenerField = typeof(UnityCodeMcpHttpServer).GetField(
                "_listener",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(transportField, Is.Not.Null);
            Assert.That(transportField.FieldType, Is.EqualTo(typeof(LoopbackHttpServerTransport)));
            Assert.That(listenerField, Is.Null);
        }

        [Test]
        public void LoopbackTransport_StartStopStart_ReusesSamePort()
        {
            int port = GetEphemeralPort();
            LoopbackHttpServerTransport transport = new(
                IPAddress.Loopback,
                port,
                (_, _) => UniTask.CompletedTask);

            try
            {
                transport.Start();

                Assert.That(transport.IsListening, Is.True);

                transport.Stop();

                Assert.That(transport.IsListening, Is.False);

                transport.Start();

                Assert.That(transport.IsListening, Is.True);
                Assert.That(transport.Port, Is.EqualTo(port));
            }
            finally
            {
                transport.Dispose();
            }
        }

        [Test]
        public void StartTransportWithRetry_SucceedsWhenPortIsReleasedDuringRetryWindow()
        {
            int port = GetEphemeralPort();
            MethodInfo method = typeof(UnityCodeMcpHttpServer).GetMethod(
                "StartTransportWithRetry",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            TcpListener blocker = new(IPAddress.Loopback, port);
            blocker.Start();

            LoopbackHttpServerTransport transport = null;
            Thread releaseThread = new(() =>
            {
                Thread.Sleep(75);
                blocker.Stop();
            });

            releaseThread.Start();

            try
            {
                transport = (LoopbackHttpServerTransport)method.Invoke(null, new object[] { port, 1 });

                Assert.That(transport, Is.Not.Null);
                Assert.That(transport.IsListening, Is.True);
                Assert.That(transport.Port, Is.EqualTo(port));
            }
            finally
            {
                transport?.Dispose();
                blocker.Stop();
                releaseThread.Join();
            }
        }

        [Test]
        public void RemovedLifecycleStateFields_DoNotExist()
        {
            FieldInfo restartScheduledField = typeof(UnityCodeMcpHttpServer).GetField(
                "_restartScheduled",
                BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo restartGenerationField = typeof(UnityCodeMcpHttpServer).GetField(
                "_restartGeneration",
                BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo startupRetryScheduledField = typeof(UnityCodeMcpHttpServer).GetField(
                "_startupRetryScheduled",
                BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo startupRetryAttemptField = typeof(UnityCodeMcpHttpServer).GetField(
                "_startupRetryAttempt",
                BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo isRunningField = typeof(UnityCodeMcpHttpServer).GetField(
                "_isRunning",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(restartScheduledField, Is.Null);
            Assert.That(restartGenerationField, Is.Null);
            Assert.That(startupRetryScheduledField, Is.Null);
            Assert.That(startupRetryAttemptField, Is.Null);
            Assert.That(isRunningField, Is.Null);
        }

        [Test]
        public void RemovedLifecycleHelperMethods_DoNotExist()
        {
            MethodInfo tryBeginStartMethod = typeof(UnityCodeMcpHttpServer).GetMethod(
                "TryBeginStart",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo shouldRetryBindConflictMethod = typeof(UnityCodeMcpHttpServer).GetMethod(
                "ShouldRetryBindConflict",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo tryScheduleStartupRetryMethod = typeof(UnityCodeMcpHttpServer).GetMethod(
                "TryScheduleStartupRetry",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo retryStartupAfterBindConflictAsyncMethod = typeof(UnityCodeMcpHttpServer).GetMethod(
                "RetryStartupAfterBindConflictAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(tryBeginStartMethod, Is.Null);
            Assert.That(shouldRetryBindConflictMethod, Is.Null);
            Assert.That(tryScheduleStartupRetryMethod, Is.Null);
            Assert.That(retryStartupAfterBindConflictAsyncMethod, Is.Null);
        }
    }
}
