using UnityCodeMcpServer.Settings;
using UnityEngine;

namespace UnityCodeMcpServer.Helpers
{
    /// <summary>
    /// Centralized logger for UnityCodeMcpServer.
    /// Wraps Unity's Debug logging with level-based filtering controlled by
    /// <see cref="UnityCodeMcpServerSettings.MinLogLevel"/>.
    /// </summary>
    public static class LoopLogger
    {
        /// <summary>
        /// Log levels ordered from most verbose to most severe.
        /// </summary>
        public enum LogLevel
        {
            Trace = 0,
            Debug = 1,
            Info = 2,
            Warn = 3,
            Error = 4,
            Fatal = 5,
            Off = 6
        }

        private static LogLevel CurrentLevel =>
            UnityCodeMcpServerSettings.Instance != null
                ? UnityCodeMcpServerSettings.Instance.MinLogLevel
                : LogLevel.Warn;

        private static bool IsEnabled(LogLevel level) => level >= CurrentLevel;

        // ── public API ────────────────────────────────────────────────────────

        /// <summary>Very detailed tracing (Trace level).</summary>
        public static void Trace(string message)
        {
            if (IsEnabled(LogLevel.Trace))
                UnityEngine.Debug.Log($"[TRACE] {message}");
        }

        /// <summary>Diagnostic / verbose information (Debug level).</summary>
        public static void Debug(string message)
        {
            if (IsEnabled(LogLevel.Debug))
                UnityEngine.Debug.Log($"[DEBUG] {message}");
        }

        /// <summary>Normal operational messages (Info level).</summary>
        public static void Info(string message)
        {
            if (IsEnabled(LogLevel.Info))
                UnityEngine.Debug.Log($"[INFO] {message}");
        }

        /// <summary>Non-critical issues or unexpected conditions (Warn level).</summary>
        public static void Warn(string message)
        {
            if (IsEnabled(LogLevel.Warn))
                UnityEngine.Debug.LogWarning($"[WARN] {message}");
        }

        /// <summary>Recoverable errors (Error level).</summary>
        public static void Error(string message)
        {
            if (IsEnabled(LogLevel.Error))
                UnityEngine.Debug.LogError($"[ERROR] {message}");
        }

        /// <summary>Critical / unrecoverable errors (Fatal level).</summary>
        public static void Fatal(string message)
        {
            if (IsEnabled(LogLevel.Fatal))
                UnityEngine.Debug.LogError($"[FATAL] {message}");
        }
    }
}
