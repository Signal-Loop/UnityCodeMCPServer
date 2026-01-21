using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Settings;
using UnityEngine;

namespace UnityCodeMcpServer.Servers.StreamableHttp
{
    /// <summary>
    /// Represents the state of an active MCP session
    /// </summary>
    public sealed class SessionState : IDisposable
    {
        /// <summary>
        /// Unique session identifier
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// UTC timestamp when session was created
        /// </summary>
        public DateTime CreatedAtUtc { get; }

        /// <summary>
        /// UTC timestamp of last activity
        /// </summary>
        public DateTime LastActivityUtc { get; private set; }

        /// <summary>
        /// Whether the session has been initialized (received initialize request)
        /// </summary>
        public bool IsInitialized { get; set; }

        /// <summary>
        /// Active SSE stream writer for GET connections (may be null)
        /// </summary>
        public SseStreamWriter ActiveSseStream { get; private set; }

        /// <summary>
        /// Lock object for thread-safe SSE stream access
        /// </summary>
        private readonly object _streamLock = new object();

        /// <summary>
        /// Cancellation token source for session-scoped operations
        /// </summary>
        public CancellationTokenSource CancellationTokenSource { get; }

        private bool _disposed;

        public SessionState(string id)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            CreatedAtUtc = DateTime.UtcNow;
            LastActivityUtc = CreatedAtUtc;
            CancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Update the last activity timestamp
        /// </summary>
        public void Touch()
        {
            LastActivityUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Set the active SSE stream for this session
        /// </summary>
        public void SetSseStream(SseStreamWriter stream)
        {
            lock (_streamLock)
            {
                // Close existing stream if any
                ActiveSseStream?.Dispose();
                ActiveSseStream = stream;
            }
        }

        /// <summary>
        /// Close and clear the active SSE stream
        /// </summary>
        public void CloseSseStream()
        {
            lock (_streamLock)
            {
                ActiveSseStream?.Dispose();
                ActiveSseStream = null;
            }
        }

        /// <summary>
        /// Check if session has an active SSE stream
        /// </summary>
        public bool HasActiveSseStream
        {
            get
            {
                lock (_streamLock)
                {
                    return ActiveSseStream != null && !ActiveSseStream.IsDisposed;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            CancellationTokenSource.Cancel();
            CancellationTokenSource.Dispose();
            CloseSseStream();
        }
    }

    /// <summary>
    /// Manages MCP session lifecycle with thread-safe operations.
    /// Handles session creation, validation, timeout expiration, and cleanup.
    /// </summary>
    public sealed class SessionManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, SessionState> _sessions;
        private readonly CancellationTokenSource _cleanupCts;
        private readonly int _sessionTimeoutSeconds;
        private readonly int _cleanupIntervalSeconds;
        private bool _disposed;

        /// <summary>
        /// Event raised when a session is terminated
        /// </summary>
        public event Action<string> SessionTerminated;

        /// <summary>
        /// Creates a new SessionManager with configurable timeout
        /// </summary>
        /// <param name="sessionTimeoutSeconds">Session timeout in seconds (0 = no timeout)</param>
        /// <param name="cleanupIntervalSeconds">Interval for cleanup task in seconds</param>
        public SessionManager(int sessionTimeoutSeconds = 3600, int cleanupIntervalSeconds = 60)
        {
            _sessions = new ConcurrentDictionary<string, SessionState>();
            _cleanupCts = new CancellationTokenSource();
            _sessionTimeoutSeconds = sessionTimeoutSeconds;
            _cleanupIntervalSeconds = cleanupIntervalSeconds;
        }

        /// <summary>
        /// Start the cleanup loop. Call this after construction to begin background cleanup.
        /// Separated from constructor to allow deferred startup.
        /// </summary>
        public void StartCleanupLoop()
        {
            if (_sessionTimeoutSeconds > 0 && !_disposed)
            {
                RunCleanupLoopAsync(_cleanupCts.Token).Forget();
            }
        }

        /// <summary>
        /// Create a new session and return its ID
        /// </summary>
        /// <returns>The new session ID</returns>
        public string CreateSession()
        {
            var sessionId = GenerateSessionId();
            var session = new SessionState(sessionId);

            if (!_sessions.TryAdd(sessionId, session))
            {
                // Extremely unlikely with GUIDs, but handle gracefully
                session.Dispose();
                throw new InvalidOperationException("Failed to create session - ID collision");
            }

            if (UnityCodeMcpServerSettings.Instance.VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} [HTTP] Session created: {sessionId}");
            }

            return sessionId;
        }

        /// <summary>
        /// Validate that a session exists and is not expired
        /// </summary>
        /// <param name="sessionId">The session ID to validate</param>
        /// <returns>True if session is valid</returns>
        public bool ValidateSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;

            if (!_sessions.TryGetValue(sessionId, out var session))
                return false;

            // Check if expired
            if (_sessionTimeoutSeconds > 0)
            {
                var elapsed = DateTime.UtcNow - session.LastActivityUtc;
                if (elapsed.TotalSeconds > _sessionTimeoutSeconds)
                {
                    TerminateSession(sessionId);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Get a session by ID
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        /// <returns>The session state, or null if not found</returns>
        public SessionState GetSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return null;

            _sessions.TryGetValue(sessionId, out var session);
            return session;
        }

        /// <summary>
        /// Update session activity timestamp
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        public void TouchSession(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Touch();
            }
        }

        /// <summary>
        /// Terminate a specific session
        /// </summary>
        /// <param name="sessionId">The session ID to terminate</param>
        /// <returns>True if session was found and terminated</returns>
        public bool TerminateSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;

            if (_sessions.TryRemove(sessionId, out var session))
            {
                if (UnityCodeMcpServerSettings.Instance.VerboseLogging)
                {
                    Debug.Log($"{McpProtocol.LogPrefix} [HTTP] Session terminated: {sessionId}");
                }

                session.Dispose();
                SessionTerminated?.Invoke(sessionId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Terminate all active sessions (used during domain reload)
        /// </summary>
        public void TerminateAllSessions()
        {
            var sessionIds = new List<string>(_sessions.Keys);

            foreach (var sessionId in sessionIds)
            {
                TerminateSession(sessionId);
            }

            if (sessionIds.Count > 0 && UnityCodeMcpServerSettings.Instance.VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} [HTTP] All sessions terminated ({sessionIds.Count} total)");
            }
        }

        /// <summary>
        /// Get the count of active sessions
        /// </summary>
        public int ActiveSessionCount => _sessions.Count;

        /// <summary>
        /// Get all active session IDs (for diagnostics)
        /// </summary>
        public ICollection<string> GetActiveSessionIds() => _sessions.Keys;

        /// <summary>
        /// Generate a cryptographically secure session ID
        /// </summary>
        private static string GenerateSessionId()
        {
            // Use two GUIDs for extra uniqueness and security
            return $"{Guid.NewGuid():N}{Guid.NewGuid():N}";
        }

        /// <summary>
        /// Background task to clean up expired sessions
        /// </summary>
        private async UniTaskVoid RunCleanupLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(_cleanupIntervalSeconds), cancellationToken: ct);

                    CleanupExpiredSessions();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{McpProtocol.LogPrefix} [HTTP] Session cleanup error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Remove all expired sessions
        /// </summary>
        private void CleanupExpiredSessions()
        {
            if (_sessionTimeoutSeconds <= 0)
                return;

            var now = DateTime.UtcNow;
            var expiredSessions = new List<string>();

            foreach (var kvp in _sessions)
            {
                var elapsed = now - kvp.Value.LastActivityUtc;
                if (elapsed.TotalSeconds > _sessionTimeoutSeconds)
                {
                    expiredSessions.Add(kvp.Key);
                }
            }

            foreach (var sessionId in expiredSessions)
            {
                if (UnityCodeMcpServerSettings.Instance.VerboseLogging)
                {
                    Debug.Log($"{McpProtocol.LogPrefix} [HTTP] Session expired: {sessionId}");
                }
                TerminateSession(sessionId);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cleanupCts.Cancel();
            _cleanupCts.Dispose();

            TerminateAllSessions();
        }
    }
}
