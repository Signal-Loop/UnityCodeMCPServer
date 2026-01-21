using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityEditor;
using UnityEngine;

namespace UnityCodeMcpServer.Resources
{
    /// <summary>
    /// Resource that exposes Unity Editor Console logs.
    /// 
/// This resource uses reflection to accessUnity's internal LogEntries API,
    /// extracting console logs and providing them as text content. It implements
    /// protective measures including:
    /// - Safe reflection with null-checks and error handling
    /// - Log truncation to prevent excessive payloads (max 1000 entries)
    /// - Proper resource cleanup via finally blocks
    /// - Comprehensive error reporting
    /// </summary>
    public class UnityConsoleLogsResource : IResource
    {
        /// <summary>The maximum number of log entries to retrieve (prevents memory issues).</summary>
        private const int MaxLogEntries = 1000;

        /// <summary>The URI identifier for this resource.</summary>
        private const string ResourceUri = "unity://console/logs";

        /// <summary>The human-readable name of this resource.</summary>
        private const string ResourceName = "Unity Console Logs";

        /// <summary>The MIME type for this resource content.</summary>
        private const string ResourceMimeType = "text/plain";

        /// <summary>Description of what this resource provides.</summary>
        private const string ResourceDescription = "Reads the Unity Editor Console logs.";

        public string Uri => ResourceUri;

        public string Name => ResourceName;

        public string Description => ResourceDescription;

        public string MimeType => ResourceMimeType;

        /// <summary>
        /// Reads console logs from the Unity Editor.
        /// 
        /// Uses reflection to safely access internal LogEntries. Handles missing types/methods
        /// gracefully and implements proper error handling with resource cleanup.
        /// </summary>
        /// <returns>A ResourcesReadResult containing the log content or error message.</returns>
        public ResourcesReadResult Read()
        {
            var sb = new StringBuilder();

            // Validate that required reflection types exist
            if (!TryGetLogEntryTypes(out var logEntriesType, out var logEntryType))
            {
                return CreateErrorResult("Error: Could not find UnityEditor.LogEntries or LogEntry types.");
            }

            // Validate that required reflection methods exist
            if (!TryGetLogEntryMethods(logEntriesType, out var startGettingEntries, out var getCount,
                                       out var getEntryInternal, out var endGettingEntries))
            {
                return CreateErrorResult("Error: Could not find necessary methods on UnityEditor.LogEntries.");
            }

            try
            {
                return RetrieveAndFormatLogs(logEntriesType, logEntryType, startGettingEntries, getCount,
                                            getEntryInternal);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Error reading logs: {ex.Message}");
            }
            finally
            {
                // Ensure cleanup is always performed
                SafeInvokeMethod(endGettingEntries, "EndGettingEntries");
            }
        }

        /// <summary>
        /// Attempts to retrieve the LogEntries and LogEntry types via reflection.
        /// </summary>
        private bool TryGetLogEntryTypes(out Type logEntriesType, out Type logEntryType)
        {
            logEntriesType = System.Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
            logEntryType = System.Type.GetType("UnityEditor.LogEntry, UnityEditor.dll");
            return logEntriesType != null && logEntryType != null;
        }

        /// <summary>
        /// Attempts to retrieve the required methods from LogEntries type via reflection.
        /// </summary>
        private bool TryGetLogEntryMethods(Type logEntriesType, out MethodInfo startGettingEntries,
                                         out MethodInfo getCount, out MethodInfo getEntryInternal,
                                         out MethodInfo endGettingEntries)
        {
            startGettingEntries = logEntriesType.GetMethod("StartGettingEntries");
            getCount = logEntriesType.GetMethod("GetCount");
            getEntryInternal = logEntriesType.GetMethod("GetEntryInternal");
            endGettingEntries = logEntriesType.GetMethod("EndGettingEntries");

            return startGettingEntries != null && getCount != null &&
                   getEntryInternal != null && endGettingEntries != null;
        }

        /// <summary>
        /// Safely retrieves and formats log entries.
        /// </summary>
        private ResourcesReadResult RetrieveAndFormatLogs(Type logEntriesType, Type logEntryType,
                                                         MethodInfo startGettingEntries, MethodInfo getCount,
                                                         MethodInfo getEntryInternal)
        {
            var sb = new StringBuilder();

            SafeInvokeMethod(startGettingEntries, "StartGettingEntries");

            int count = SafeGetLogCount(getCount);
            if (count == 0)
            {
                return CreateSuccessResult("(No console logs available)");
            }

            int logStart = Math.Max(0, count - MaxLogEntries);
            if (count > MaxLogEntries)
            {
                sb.AppendLine($"--- Showing last {MaxLogEntries} logs (Total: {count}) ---");
            }

            ExtractLogMessages(logEntryType, getEntryInternal, logStart, count, sb);

            return CreateSuccessResult(sb.ToString());
        }

        /// <summary>
        /// Extracts individual log messages from the console.
        /// </summary>
        private void ExtractLogMessages(Type logEntryType, MethodInfo getEntryInternal, int start, int count, StringBuilder sb)
        {
            var messageField = logEntryType.GetField("message");
            if (messageField == null)
            {
                sb.AppendLine("Error: Could not find 'message' field on LogEntry type.");
                return;
            }

            try
            {
                for (int i = start; i < count; i++)
                {
                    object logEntry = Activator.CreateInstance(logEntryType);
                    getEntryInternal.Invoke(null, new object[] { i, logEntry });

                    string message = (string)messageField.GetValue(logEntry);
                    if (message != null)
                    {
                        sb.AppendLine(message);
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error extracting log messages: {ex.Message}");
            }
        }

        /// <summary>
        /// Safely invokes a method, catching any exceptions.
        /// </summary>
        private void SafeInvokeMethod(MethodInfo method, string methodName)
        {
            try
            {
                method?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error invoking {methodName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Safely retrieves the log count from Unity's LogEntries.
        /// </summary>
        private int SafeGetLogCount(MethodInfo getCount)
        {
            try
            {
                object result = getCount.Invoke(null, null);
                return result is int count ? count : 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error getting log count: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Creates a success result with the provided content.
        /// </summary>
        private ResourcesReadResult CreateSuccessResult(string content)
        {
            return new ResourcesReadResult
            {
                Contents = new List<ResourceContent>
                {
                    new ResourceContent
                    {
                        Uri = Uri,
                        MimeType = MimeType,
                        Text = content
                    }
                }
            };
        }

        /// <summary>
        /// Creates an error result with the specified error message.
        /// </summary>
        private ResourcesReadResult CreateErrorResult(string message)
        {
            return CreateSuccessResult(message);
        }
    }
}
