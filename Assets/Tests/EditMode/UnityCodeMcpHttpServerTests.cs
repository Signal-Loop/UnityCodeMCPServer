using System.Reflection;
using NUnit.Framework;
using UnityCodeMcpServer.Servers.StreamableHttp;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class UnityCodeMcpHttpServerTests
    {
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
