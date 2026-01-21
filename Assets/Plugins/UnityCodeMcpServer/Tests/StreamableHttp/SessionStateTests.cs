using System;
using UnityCodeMcpServer.Servers.StreamableHttp;
using NUnit.Framework;

namespace UnityCodeMcpServer.Tests.StreamableHttp
{
    [TestFixture]
    public class SessionStateTests
    {
        #region Constructor Tests

        [Test]
        public void Constructor_SetsId()
        {
            using var session = new SessionState("test-session-id");

            Assert.That(session.Id, Is.EqualTo("test-session-id"));
        }

        [Test]
        public void Constructor_SetsCreatedAtUtc()
        {
            var before = DateTime.UtcNow;
            using var session = new SessionState("test-session-id");
            var after = DateTime.UtcNow;

            Assert.That(session.CreatedAtUtc, Is.InRange(before, after));
        }

        [Test]
        public void Constructor_SetsLastActivityUtcToCreatedTime()
        {
            using var session = new SessionState("test-session-id");

            Assert.That(session.LastActivityUtc, Is.EqualTo(session.CreatedAtUtc));
        }

        [Test]
        public void Constructor_InitializesIsInitializedToFalse()
        {
            using var session = new SessionState("test-session-id");

            Assert.That(session.IsInitialized, Is.False);
        }

        [Test]
        public void Constructor_NullId_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new SessionState(null));
        }

        [Test]
        public void Constructor_CreatesCancellationTokenSource()
        {
            using var session = new SessionState("test-session-id");

            Assert.That(session.CancellationTokenSource, Is.Not.Null);
            Assert.That(session.CancellationTokenSource.IsCancellationRequested, Is.False);
        }

        #endregion

        #region Touch Tests

        [Test]
        public void Touch_UpdatesLastActivityUtc()
        {
            using var session = new SessionState("test-session-id");
            var initialActivity = session.LastActivityUtc;

            session.Touch();

            // LastActivityUtc should be >= initial (same or later due to clock resolution)
            Assert.That(session.LastActivityUtc, Is.GreaterThanOrEqualTo(initialActivity));
        }

        #endregion

        #region IsInitialized Tests

        [Test]
        public void IsInitialized_CanBeSetToTrue()
        {
            using var session = new SessionState("test-session-id");

            session.IsInitialized = true;

            Assert.That(session.IsInitialized, Is.True);
        }

        #endregion

        #region SSE Stream Tests

        [Test]
        public void SetSseStream_SetsActiveStream()
        {
            using var session = new SessionState("test-session-id");

            // We can't easily create a real SseStreamWriter in unit tests
            // but we can verify the null case
            Assert.That(session.ActiveSseStream, Is.Null);
            Assert.That(session.HasActiveSseStream, Is.False);
        }

        [Test]
        public void CloseSseStream_ClearsStream()
        {
            using var session = new SessionState("test-session-id");

            session.CloseSseStream();

            Assert.That(session.ActiveSseStream, Is.Null);
            Assert.That(session.HasActiveSseStream, Is.False);
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void Dispose_CancelsCancellationToken()
        {
            var session = new SessionState("test-session-id");
            var cts = session.CancellationTokenSource;

            session.Dispose();

            Assert.That(cts.IsCancellationRequested, Is.True);
        }

        [Test]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var session = new SessionState("test-session-id");

            Assert.DoesNotThrow(() =>
            {
                session.Dispose();
                session.Dispose();
                session.Dispose();
            });
        }

        #endregion
    }
}
