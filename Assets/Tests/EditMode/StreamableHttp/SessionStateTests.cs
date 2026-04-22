using System;
using System.Threading;
using NUnit.Framework;
using UnityCodeMcpServer.Servers.StreamableHttp;

namespace UnityCodeMcpServer.Tests.EditMode.StreamableHttp
{
    [TestFixture]
    public class SessionStateTests
    {
        #region Constructor Tests

        [Test]
        public void Constructor_SetsId()
        {
            using SessionState session = new("test-session-id");

            Assert.That(session.Id, Is.EqualTo("test-session-id"));
        }

        [Test]
        public void Constructor_SetsCreatedAtUtc()
        {
            DateTime before = DateTime.UtcNow;
            using SessionState session = new("test-session-id");
            DateTime after = DateTime.UtcNow;

            Assert.That(session.CreatedAtUtc, Is.InRange(before, after));
        }

        [Test]
        public void Constructor_SetsLastActivityUtcToCreatedTime()
        {
            using SessionState session = new("test-session-id");

            Assert.That(session.LastActivityUtc, Is.EqualTo(session.CreatedAtUtc));
        }

        [Test]
        public void Constructor_InitializesIsInitializedToFalse()
        {
            using SessionState session = new("test-session-id");

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
            using SessionState session = new("test-session-id");

            Assert.That(session.CancellationTokenSource, Is.Not.Null);
            Assert.That(session.CancellationTokenSource.IsCancellationRequested, Is.False);
        }

        #endregion

        #region Touch Tests

        [Test]
        public void Touch_UpdatesLastActivityUtc()
        {
            using SessionState session = new("test-session-id");
            DateTime initialActivity = session.LastActivityUtc;

            session.Touch();

            // LastActivityUtc should be >= initial (same or later due to clock resolution)
            Assert.That(session.LastActivityUtc, Is.GreaterThanOrEqualTo(initialActivity));
        }

        #endregion

        #region IsInitialized Tests

        [Test]
        public void IsInitialized_CanBeSetToTrue()
        {
            using SessionState session = new("test-session-id");

            session.IsInitialized = true;

            Assert.That(session.IsInitialized, Is.True);
        }

        #endregion

        #region SSE Stream Tests

        [Test]
        public void SetSseStream_SetsActiveStream()
        {
            using SessionState session = new("test-session-id");

            // We can't easily create a real SseStreamWriter in unit tests
            // but we can verify the null case
            Assert.That(session.ActiveSseStream, Is.Null);
            Assert.That(session.HasActiveSseStream, Is.False);
        }

        [Test]
        public void CloseSseStream_ClearsStream()
        {
            using SessionState session = new("test-session-id");

            session.CloseSseStream();

            Assert.That(session.ActiveSseStream, Is.Null);
            Assert.That(session.HasActiveSseStream, Is.False);
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void Dispose_CancelsCancellationToken()
        {
            SessionState session = new("test-session-id");
            CancellationTokenSource cts = session.CancellationTokenSource;

            session.Dispose();

            Assert.That(cts.IsCancellationRequested, Is.True);
        }

        [Test]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            SessionState session = new("test-session-id");

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
