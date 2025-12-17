using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json;
using LoopMcpServer.Interfaces;
using LoopMcpServer.Protocol;
using UnityEngine;

namespace LoopMcpServer.Tools
{
    /// <summary>
    /// Tool that reads Unity Editor console logs via reflection.
    /// Provides recent log entries as text while guarding against reflection failures.
    /// </summary>
    public class ReadUnityConsoleLogsTool : ITool
    {
        private const int DefaultMaxEntries = 200;
        private const int MaxEntriesLimit = 1000;

        private readonly Func<int, (string text, bool isError)> _logReader;

        public ReadUnityConsoleLogsTool()
            : this(null)
        {
        }

        public ReadUnityConsoleLogsTool(Func<int, (string text, bool isError)> logReader)
        {
            _logReader = logReader ?? ReadLogsFromUnity;
        }

        public string Name => "read_unity_console_logs";

        public string Description =>
            "Reads Unity Editor Console logs. Returns recent log entries as text with an optional max_entries limit.";

        public JsonElement InputSchema => JsonHelper.ParseElement(@"
        {
            ""type"": ""object"",
            ""properties"": {
                ""max_entries"": {
                    ""type"": ""integer"",
                    ""minimum"": 1,
                    ""maximum"": 1000,
                    ""description"": ""Maximum number of log entries to return. Defaults to 200.""
                }
            }
        }
        ");

        public ToolsCallResult Execute(JsonElement arguments)
        {
            int requested = arguments.GetIntOrDefault("max_entries", DefaultMaxEntries);
            int maxEntries = NormalizeMaxEntries(requested);

            var (text, isError) = _logReader(maxEntries);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "(No console logs available)";
            }

            return new ToolsCallResult
            {
                IsError = isError,
                Content = new List<ContentItem>
                {
                    ContentItem.TextContent(text)
                }
            };
        }

        private static int NormalizeMaxEntries(int requested)
        {
            if (requested < 1)
            {
                return DefaultMaxEntries;
            }

            return Math.Min(requested, MaxEntriesLimit);
        }

        private static (string text, bool isError) ReadLogsFromUnity(int maxEntries)
        {
            if (!TryGetLogEntryTypes(out var logEntriesType, out var logEntryType))
            {
                return ("Error: Could not find UnityEditor.LogEntries or LogEntry types.", true);
            }

            if (!TryGetLogEntryMethods(logEntriesType, out var startGettingEntries, out var getCount,
                                       out var getEntryInternal, out var endGettingEntries))
            {
                return ("Error: Could not find necessary methods on UnityEditor.LogEntries.", true);
            }

            try
            {
                return RetrieveLogs(logEntryType, startGettingEntries, getCount, getEntryInternal, maxEntries);
            }
            catch (Exception ex)
            {
                Debug.LogError($"ReadUnityConsoleLogsTool: error reading logs: {ex.Message}");
                return ($"Error reading logs: {ex.Message}", true);
            }
            finally
            {
                SafeInvokeMethod(endGettingEntries, "EndGettingEntries");
            }
        }

        private static (string text, bool isError) RetrieveLogs(Type logEntryType, MethodInfo startGettingEntries,
                                                                MethodInfo getCount, MethodInfo getEntryInternal,
                                                                int maxEntries)
        {
            var sb = new StringBuilder();

            SafeInvokeMethod(startGettingEntries, "StartGettingEntries");

            int count = SafeGetLogCount(getCount);
            if (count <= 0)
            {
                return ("(No console logs available)", false);
            }

            int effectiveLimit = Math.Max(1, Math.Min(maxEntries, MaxEntriesLimit));
            int startIndex = Math.Max(0, count - effectiveLimit);
            if (count > effectiveLimit)
            {
                sb.AppendLine($"--- Showing last {effectiveLimit} logs (Total: {count}) ---");
            }

            var messageField = logEntryType.GetField("message");
            if (messageField == null)
            {
                return ("Error: Could not find 'message' field on LogEntry type.", true);
            }

            try
            {
                for (int i = startIndex; i < count; i++)
                {
                    object logEntry = Activator.CreateInstance(logEntryType);
                    getEntryInternal.Invoke(null, new object[] { i, logEntry });

                    string message = messageField.GetValue(logEntry) as string;
                    if (message != null)
                    {
                        sb.AppendLine(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"ReadUnityConsoleLogsTool: error extracting log messages: {ex.Message}");
                return ($"Error extracting log messages: {ex.Message}", true);
            }

            return (sb.ToString(), false);
        }

        private static void SafeInvokeMethod(MethodInfo method, string methodName)
        {
            try
            {
                method?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"ReadUnityConsoleLogsTool: error invoking {methodName}: {ex.Message}");
            }
        }

        private static int SafeGetLogCount(MethodInfo getCount)
        {
            try
            {
                object result = getCount.Invoke(null, null);
                return result is int count ? count : 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"ReadUnityConsoleLogsTool: error getting log count: {ex.Message}");
                return 0;
            }
        }

        private static bool TryGetLogEntryTypes(out Type logEntriesType, out Type logEntryType)
        {
            logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
            logEntryType = Type.GetType("UnityEditor.LogEntry, UnityEditor.dll");
            return logEntriesType != null && logEntryType != null;
        }

        private static bool TryGetLogEntryMethods(Type logEntriesType, out MethodInfo startGettingEntries,
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
    }
}