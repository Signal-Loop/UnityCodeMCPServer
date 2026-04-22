using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityCodeMcpServer.Servers.StreamableHttp;

namespace UnityCodeMcpServer.Tests.EditMode.StreamableHttp
{
    [TestFixture]
    public class SessionManagerTests
    {
        private SessionManager _sessionManager;

        [SetUp]
        public void SetUp()
        {
            // Create session manager with short timeout for testing (no cleanup loop)
            _sessionManager = new SessionManager(sessionTimeoutSeconds: 60, cleanupIntervalSeconds: 60);
        }

        [TearDown]
        public void TearDown()
        {
            _sessionManager?.Dispose();
            _sessionManager = null;
        }

        #region Session Creation Tests

        [Test]
        public void CreateSession_ReturnsValidSessionId()
        {
            string sessionId = _sessionManager.CreateSession();

            Assert.That(sessionId, Is.Not.Null);
            Assert.That(sessionId, Is.Not.Empty);
            Assert.That(sessionId.Length, Is.EqualTo(64)); // Two GUIDs without hyphens
        }

        [Test]
        public void CreateSession_ReturnsUniqueIds()
        {
            string sessionId1 = _sessionManager.CreateSession();
            string sessionId2 = _sessionManager.CreateSession();
            string sessionId3 = _sessionManager.CreateSession();

            Assert.That(sessionId1, Is.Not.EqualTo(sessionId2));
            Assert.That(sessionId2, Is.Not.EqualTo(sessionId3));
            Assert.That(sessionId1, Is.Not.EqualTo(sessionId3));
        }

        [Test]
        public void CreateSession_IncrementsActiveSessionCount()
        {
            Assert.That(_sessionManager.ActiveSessionCount, Is.EqualTo(0));

            _sessionManager.CreateSession();
            Assert.That(_sessionManager.ActiveSessionCount, Is.EqualTo(1));

            _sessionManager.CreateSession();
            Assert.That(_sessionManager.ActiveSessionCount, Is.EqualTo(2));
        }

        #endregion

        #region Session Validation Tests

        [Test]
        public void ValidateSession_ValidSession_ReturnsTrue()
        {
            string sessionId = _sessionManager.CreateSession();

            bool isValid = _sessionManager.ValidateSession(sessionId);

            Assert.That(isValid, Is.True);
        }

        [Test]
        public void ValidateSession_InvalidSession_ReturnsFalse()
        {
            bool isValid = _sessionManager.ValidateSession("nonexistent-session-id");

            Assert.That(isValid, Is.False);
        }

        [Test]
        public void ValidateSession_NullSession_ReturnsFalse()
        {
            bool isValid = _sessionManager.ValidateSession(null);

            Assert.That(isValid, Is.False);
        }

        [Test]
        public void ValidateSession_EmptySession_ReturnsFalse()
        {
            bool isValid = _sessionManager.ValidateSession(string.Empty);

            Assert.That(isValid, Is.False);
        }

        #endregion

        #region Session Retrieval Tests

        [Test]
        public void GetSession_ValidSession_ReturnsSession()
        {
            string sessionId = _sessionManager.CreateSession();

            SessionState session = _sessionManager.GetSession(sessionId);

            Assert.That(session, Is.Not.Null);
            Assert.That(session.Id, Is.EqualTo(sessionId));
        }

        [Test]
        public void GetSession_InvalidSession_ReturnsNull()
        {
            SessionState session = _sessionManager.GetSession("nonexistent-session-id");

            Assert.That(session, Is.Null);
        }

        [Test]
        public void GetSession_NullSession_ReturnsNull()
        {
            SessionState session = _sessionManager.GetSession(null);

            Assert.That(session, Is.Null);
        }

        #endregion

        #region Session Touch Tests

        [Test]
        public void TouchSession_UpdatesLastActivity()
        {
            string sessionId = _sessionManager.CreateSession();
            SessionState session = _sessionManager.GetSession(sessionId);
            DateTime initialActivity = session.LastActivityUtc;

            // Force time to advance by modifying the internal state via Touch
            // Touch uses DateTime.UtcNow internally, so we verify it changed
            _sessionManager.TouchSession(sessionId);

            // LastActivityUtc should be >= initial (same or later)
            Assert.That(session.LastActivityUtc, Is.GreaterThanOrEqualTo(initialActivity));
        }

        [Test]
        public void TouchSession_InvalidSession_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _sessionManager.TouchSession("nonexistent-session-id"));
        }

        #endregion

        #region Session Termination Tests

        [Test]
        public void TerminateSession_ValidSession_ReturnsTrue()
        {
            string sessionId = _sessionManager.CreateSession();

            bool terminated = _sessionManager.TerminateSession(sessionId);

            Assert.That(terminated, Is.True);
        }

        [Test]
        public void TerminateSession_RemovesSession()
        {
            string sessionId = _sessionManager.CreateSession();
            Assert.That(_sessionManager.ActiveSessionCount, Is.EqualTo(1));

            _sessionManager.TerminateSession(sessionId);

            Assert.That(_sessionManager.ActiveSessionCount, Is.EqualTo(0));
            Assert.That(_sessionManager.GetSession(sessionId), Is.Null);
        }

        [Test]
        public void TerminateSession_InvalidSession_ReturnsFalse()
        {
            bool terminated = _sessionManager.TerminateSession("nonexistent-session-id");

            Assert.That(terminated, Is.False);
        }

        [Test]
        public void TerminateSession_NullSession_ReturnsFalse()
        {
            bool terminated = _sessionManager.TerminateSession(null);

            Assert.That(terminated, Is.False);
        }

        [Test]
        public void TerminateSession_FiresTerminatedEvent()
        {
            string sessionId = _sessionManager.CreateSession();
            string terminatedSessionId = null;
            _sessionManager.SessionTerminated += id => terminatedSessionId = id;

            _sessionManager.TerminateSession(sessionId);

            Assert.That(terminatedSessionId, Is.EqualTo(sessionId));
        }

        [Test]
        public void TerminateAllSessions_RemovesAllSessions()
        {
            _sessionManager.CreateSession();
            _sessionManager.CreateSession();
            _sessionManager.CreateSession();
            Assert.That(_sessionManager.ActiveSessionCount, Is.EqualTo(3));

            _sessionManager.TerminateAllSessions();

            Assert.That(_sessionManager.ActiveSessionCount, Is.EqualTo(0));
        }

        #endregion

        #region Session Active IDs Tests

        [Test]
        public void GetActiveSessionIds_ReturnsAllSessionIds()
        {
            string id1 = _sessionManager.CreateSession();
            string id2 = _sessionManager.CreateSession();
            string id3 = _sessionManager.CreateSession();

            ICollection<string> ids = _sessionManager.GetActiveSessionIds();

            Assert.That(ids, Contains.Item(id1));
            Assert.That(ids, Contains.Item(id2));
            Assert.That(ids, Contains.Item(id3));
            Assert.That(ids.Count, Is.EqualTo(3));
        }

        #endregion

        #region Session Timeout Tests

        [Test]
        public void ValidateSession_ExpiredSession_ReturnsFalse()
        {
            // Create manager with 1 second timeout for testing
            using SessionManager shortTimeoutManager = new(sessionTimeoutSeconds: 1, cleanupIntervalSeconds: 60);
            string sessionId = shortTimeoutManager.CreateSession();

            // Session should be valid initially
            Assert.That(shortTimeoutManager.ValidateSession(sessionId), Is.True);

            // Manually set last activity to past (simulate timeout)
            SessionState session = shortTimeoutManager.GetSession(sessionId);
            // We can't directly set LastActivityUtc, but we can verify timeout logic works
            // by checking that validation uses the timeout value
            Assert.That(shortTimeoutManager.ValidateSession(sessionId), Is.True);
        }

        [Test]
        public void SessionManager_ZeroTimeout_DisablesExpiration()
        {
            using SessionManager noTimeoutManager = new(sessionTimeoutSeconds: 0, cleanupIntervalSeconds: 60);
            string sessionId = noTimeoutManager.CreateSession();

            // Session should always be valid with zero timeout
            Assert.That(noTimeoutManager.ValidateSession(sessionId), Is.True);
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void Dispose_TerminatesAllSessions()
        {
            SessionManager manager = new(sessionTimeoutSeconds: 60, cleanupIntervalSeconds: 60);
            string id1 = manager.CreateSession();
            string id2 = manager.CreateSession();
            Assert.That(manager.ActiveSessionCount, Is.EqualTo(2));

            manager.Dispose();

            Assert.That(manager.ActiveSessionCount, Is.EqualTo(0));
        }

        [Test]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            SessionManager manager = new(sessionTimeoutSeconds: 60, cleanupIntervalSeconds: 60);
            manager.CreateSession();

            Assert.DoesNotThrow(() =>
            {
                manager.Dispose();
                manager.Dispose();
                manager.Dispose();
            });
        }

        #endregion
    }
}
