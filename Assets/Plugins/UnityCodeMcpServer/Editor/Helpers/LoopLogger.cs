using System;
using System.IO;
using UnityCodeMcpServer.Settings;
using UnityEngine;

namespace UnityCodeMcpServer.Helpers
{
    /// <summary>
    /// Centralized logger for UnityCodeMcpServer.
    /// Wraps Unity's Debug logging with level-based filtering controlled by
    /// <see cref="UnityCodeMcpServerSettings.MinLogLevel"/>.
    /// Optionally writes logs to file with timestamp and severity.
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

        private static readonly string _log_file_path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "UnityCodeMcpServerLog.log");
        private static readonly int _max_log_files = 5;
        private static readonly long _max_log_file_size = 10 * 1024 * 1024; // 10 MB

        private static LogLevel CurrentLevel =>
            UnityCodeMcpServerSettings.Instance != null
                ? UnityCodeMcpServerSettings.Instance.MinLogLevel
                : LogLevel.Warn;

        private static bool IsEnabled(LogLevel level) => level >= CurrentLevel;

        private static bool ShouldLogToFile() =>
            UnityCodeMcpServerSettings.Instance != null && UnityCodeMcpServerSettings.Instance.LogToFile;

        // ── public API ────────────────────────────────────────────────────────

        /// <summary>Very detailed tracing (Trace level).</summary>
        public static void Trace(string message)
        {
            message = $"[LoopLogger] {message}";
            if (IsEnabled(LogLevel.Trace))
            {
                UnityEngine.Debug.Log($"[TRACE] {message}");
                WriteToFile(LogLevel.Trace, message);
            }
        }

        /// <summary>Diagnostic / verbose information (Debug level).</summary>
        public static void Debug(string message)
        {
            message = $"[LoopLogger] {message}";
            if (IsEnabled(LogLevel.Debug))
            {
                UnityEngine.Debug.Log($"[DEBUG] {message}");
                WriteToFile(LogLevel.Debug, message);
            }
        }

        /// <summary>Normal operational messages (Info level).</summary>
        public static void Info(string message)
        {
            message = $"[LoopLogger] {message}";
            if (IsEnabled(LogLevel.Info))
            {
                UnityEngine.Debug.Log($"[INFO] {message}");
                WriteToFile(LogLevel.Info, message);
            }
        }

        /// <summary>Non-critical issues or unexpected conditions (Warn level).</summary>
        public static void Warn(string message)
        {
            message = $"[LoopLogger] {message}";
            if (IsEnabled(LogLevel.Warn))
            {
                UnityEngine.Debug.LogWarning($"[WARN] {message}");
                WriteToFile(LogLevel.Warn, message);
            }
        }

        /// <summary>Recoverable errors (Error level).</summary>
        public static void Error(string message)
        {
            message = $"[LoopLogger] {message}";
            if (IsEnabled(LogLevel.Error))
            {
                UnityEngine.Debug.LogError($"[ERROR] {message}");
                WriteToFile(LogLevel.Error, message, captureStackTrace: true);
            }
        }

        /// <summary>Critical / unrecoverable errors (Fatal level).</summary>
        public static void Fatal(string message)
        {
            message = $"[LoopLogger] {message}";
            if (IsEnabled(LogLevel.Fatal))
            {
                UnityEngine.Debug.LogError($"[FATAL] {message}");
                WriteToFile(LogLevel.Fatal, message, captureStackTrace: true);
            }
        }

        /// <summary>Log an exception with full stack trace (Error level).</summary>
        public static void Exception(string message, System.Exception ex)
        {
            message = $"[LoopLogger] {message}";
            if (IsEnabled(LogLevel.Error))
            {
                UnityEngine.Debug.LogException(ex);
                WriteToFile(LogLevel.Error, $"{message}\n{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ── File logging ────────────────────────────────────────────────────────

        private static void WriteToFile(LogLevel level, string message, bool captureStackTrace = false)
        {
            if (!ShouldLogToFile())
                return;

            try
            {
                RotateLogFileIfNeeded();

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string severity = level.ToString().ToUpper();
                string log_entry = $"[{timestamp}] [{severity}] {message}";

                if (captureStackTrace)
                {
                    var stack_trace = new System.Diagnostics.StackTrace(true);
                    log_entry += $"\n{stack_trace}";
                }

                lock (typeof(LoopLogger))
                {
                    File.AppendAllText(_log_file_path, log_entry + Environment.NewLine);
                }
            }
            catch (System.Exception ex)
            {
                // Prevent logging errors from crashing the application
                UnityEngine.Debug.LogError($"[LoopLogger] Failed to write to log file: {ex.Message}");
            }
        }

        private static void RotateLogFileIfNeeded()
        {
            try
            {
                // Check file size rotation
                if (File.Exists(_log_file_path))
                {
                    var file_info = new FileInfo(_log_file_path);
                    if (file_info.Length > _max_log_file_size)
                    {
                        RotateLogFiles();
                    }
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[LoopLogger] Failed to check log file size: {ex.Message}");
            }
        }

        private static void RotateLogFiles()
        {
            try
            {
                string log_directory = Path.GetDirectoryName(_log_file_path);
                string log_filename = Path.GetFileNameWithoutExtension(_log_file_path);
                string log_extension = Path.GetExtension(_log_file_path);

                // Shift existing files
                for (int i = _max_log_files - 1; i >= 1; i--)
                {
                    string old_file = Path.Combine(log_directory, $"{log_filename}.{i}{log_extension}");
                    string new_file = Path.Combine(log_directory, $"{log_filename}.{i + 1}{log_extension}");

                    if (File.Exists(old_file))
                    {
                        if (File.Exists(new_file))
                            File.Delete(new_file);
                        File.Move(old_file, new_file);
                    }
                }

                // Move current log to .1
                string current_log = _log_file_path;
                string backup_log = Path.Combine(log_directory, $"{log_filename}.1{log_extension}");
                if (File.Exists(backup_log))
                    File.Delete(backup_log);
                File.Move(current_log, backup_log);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[LoopLogger] Failed to rotate log files: {ex.Message}");
            }
        }
    }
}
