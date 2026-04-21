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
            var method = typeof(UnityCodeMcpHttpServer).GetMethod(
                "StopServer",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            Assert.That(method, Is.Not.Null, "StopServer(string reason) overload should exist");
        }

        [Test]
        public void TryBeginStart_ReturnsFalseWhenRestartIsPending()
        {
            var restartScheduledField = typeof(UnityCodeMcpHttpServer).GetField(
                "_restartScheduled",
                BindingFlags.NonPublic | BindingFlags.Static);
            var isRunningField = typeof(UnityCodeMcpHttpServer).GetField(
                "_isRunning",
                BindingFlags.NonPublic | BindingFlags.Static);
            var method = typeof(UnityCodeMcpHttpServer).GetMethod(
                "TryBeginStart",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(restartScheduledField, Is.Not.Null, "_restartScheduled field should exist");
            Assert.That(isRunningField, Is.Not.Null, "_isRunning field should exist");
            Assert.That(method, Is.Not.Null, "TryBeginStart should exist");

            var originalRestartScheduled = (bool)restartScheduledField.GetValue(null);
            var originalIsRunning = (bool)isRunningField.GetValue(null);

            try
            {
                restartScheduledField.SetValue(null, true);
                isRunningField.SetValue(null, false);

                var result = (bool)method.Invoke(null, new object[] { "test", false });

                Assert.That(result, Is.False);
            }
            finally
            {
                restartScheduledField.SetValue(null, originalRestartScheduled);
                isRunningField.SetValue(null, originalIsRunning);
            }
        }

        [Test]
        public void ShouldRetryBindConflict_ReturnsTrueForDelayedStartBelowRetryLimit()
        {
            var retryAttemptField = typeof(UnityCodeMcpHttpServer).GetField(
                "_startupRetryAttempt",
                BindingFlags.NonPublic | BindingFlags.Static);
            var method = typeof(UnityCodeMcpHttpServer).GetMethod(
                "ShouldRetryBindConflict",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(retryAttemptField, Is.Not.Null, "_startupRetryAttempt field should exist");
            Assert.That(method, Is.Not.Null, "ShouldRetryBindConflict should exist");

            var originalRetryAttempt = (int)retryAttemptField.GetValue(null);

            try
            {
                retryAttemptField.SetValue(null, 0);

                var result = (bool)method.Invoke(null, new object[] { "delayed-start" });

                Assert.That(result, Is.True);
            }
            finally
            {
                retryAttemptField.SetValue(null, originalRetryAttempt);
            }
        }

        [Test]
        public void ShouldRetryBindConflict_ReturnsFalseForRequestedStartAndAtRetryLimit()
        {
            var retryAttemptField = typeof(UnityCodeMcpHttpServer).GetField(
                "_startupRetryAttempt",
                BindingFlags.NonPublic | BindingFlags.Static);
            var maxRetryField = typeof(UnityCodeMcpHttpServer).GetField(
                "MaxStartupRetryAttempts",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            var method = typeof(UnityCodeMcpHttpServer).GetMethod(
                "ShouldRetryBindConflict",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(retryAttemptField, Is.Not.Null, "_startupRetryAttempt field should exist");
            Assert.That(maxRetryField, Is.Not.Null, "MaxStartupRetryAttempts constant should exist");
            Assert.That(method, Is.Not.Null, "ShouldRetryBindConflict should exist");

            var originalRetryAttempt = (int)retryAttemptField.GetValue(null);
            var maxRetryAttempts = (int)maxRetryField.GetRawConstantValue();

            try
            {
                retryAttemptField.SetValue(null, 0);
                var requestedResult = (bool)method.Invoke(null, new object[] { "requested" });

                retryAttemptField.SetValue(null, maxRetryAttempts);
                var exhaustedResult = (bool)method.Invoke(null, new object[] { "delayed-start" });

                Assert.That(requestedResult, Is.False);
                Assert.That(exhaustedResult, Is.False);
            }
            finally
            {
                retryAttemptField.SetValue(null, originalRetryAttempt);
            }
        }
    }
}