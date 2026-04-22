using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace UnityCodeMcpServer.Helpers
{
    public readonly struct UnityConsoleLogEntry
    {
        public UnityConsoleLogEntry(string message, string timestamp)
        {
            Message = message ?? string.Empty;
            Timestamp = string.IsNullOrWhiteSpace(timestamp) ? null : timestamp.Trim();
        }

        public string Message { get; }

        public string Timestamp { get; }
    }

    public static class UnityConsoleLogReader
    {
        public const int MaxEntriesLimit = 1000;

        private static readonly BindingFlags StaticMethodFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        public static (string text, bool isError) ReadTail(int maxEntries)
        {
            int effectiveLimit = NormalizeMaxEntries(maxEntries);
            if (!TryCreateAccessor(out ReflectionAccessor accessor, out string errorText))
            {
                return (errorText, true);
            }

            try
            {
                SafeInvokeMethod(accessor.StartGettingEntries, "StartGettingEntries");

                int totalCount = SafeGetLogCount(accessor.GetCount);
                if (totalCount <= 0)
                {
                    return ("(No console logs available)", false);
                }

                IReadOnlyList<UnityConsoleLogEntry> entries = ReadTailEntries(accessor, totalCount, effectiveLimit);
                return (FormatEntries(entries, totalCount, effectiveLimit), false);
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Error($"UnityConsoleLogReader: error reading logs: {ex.Message}");
                return ($"Error reading logs: {ex.Message}", true);
            }
            finally
            {
                SafeInvokeMethod(accessor.EndGettingEntries, "EndGettingEntries");
            }
        }

        public static IReadOnlyList<UnityConsoleLogEntry> SelectTail(IReadOnlyList<UnityConsoleLogEntry> entries, int maxEntries)
        {
            if (entries == null || entries.Count == 0)
            {
                return Array.Empty<UnityConsoleLogEntry>();
            }

            int effectiveLimit = NormalizeMaxEntries(maxEntries);
            int startIndex = Math.Max(0, entries.Count - effectiveLimit);
            List<UnityConsoleLogEntry> tailEntries = new(entries.Count - startIndex);
            for (int i = startIndex; i < entries.Count; i++)
            {
                tailEntries.Add(entries[i]);
            }

            return tailEntries;
        }

        public static string FormatEntries(IReadOnlyList<UnityConsoleLogEntry> entries, int totalCount, int maxEntries)
        {
            if (entries == null || entries.Count == 0)
            {
                return "(No console logs available)";
            }

            int effectiveLimit = NormalizeMaxEntries(maxEntries);
            StringBuilder sb = new();

            if (totalCount > effectiveLimit)
            {
                sb.AppendLine($"--- Showing last {entries.Count} logs (Total: {totalCount}) ---");
            }

            for (int i = 0; i < entries.Count; i++)
            {
                UnityConsoleLogEntry entry = entries[i];
                if (!string.IsNullOrWhiteSpace(entry.Timestamp))
                {
                    sb.Append(entry.Timestamp);
                    sb.Append(' ');
                }

                sb.AppendLine(entry.Message);
            }

            return sb.ToString().TrimEnd();
        }

        private static IReadOnlyList<UnityConsoleLogEntry> ReadTailEntries(ReflectionAccessor accessor, int totalCount, int maxEntries)
        {
            int startIndex = Math.Max(0, totalCount - maxEntries);
            List<UnityConsoleLogEntry> entries = new(totalCount - startIndex);

            for (int i = startIndex; i < totalCount; i++)
            {
                object logEntry = Activator.CreateInstance(accessor.LogEntryType);
                object result = accessor.GetEntryInternal.Invoke(null, new[] { (object)i, logEntry });
                if (result is bool hasEntry && !hasEntry)
                {
                    continue;
                }

                string message = accessor.MessageField.GetValue(logEntry) as string;
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                string timestamp = TryGetTimestamp(accessor.GetEntryTimestampInternal, i);
                entries.Add(new UnityConsoleLogEntry(message.TrimEnd(), timestamp));
            }

            return entries;
        }

        private static string TryGetTimestamp(MethodInfo getEntryTimestampInternal, int row)
        {
            if (getEntryTimestampInternal == null)
            {
                return null;
            }

            try
            {
                object[] args = { row, null };
                object result = getEntryTimestampInternal.Invoke(null, args);
                return result is bool hasTimestamp && hasTimestamp ? args[1] as string : null;
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Debug($"UnityConsoleLogReader: could not read timestamp for row {row}: {ex.Message}");
                return null;
            }
        }

        private static int NormalizeMaxEntries(int requested)
        {
            if (requested < 1)
            {
                return 1;
            }

            return Math.Min(requested, MaxEntriesLimit);
        }

        private static bool TryCreateAccessor(out ReflectionAccessor accessor, out string errorText)
        {
            accessor = null;
            errorText = null;

            Type logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
            Type logEntryType = Type.GetType("UnityEditor.LogEntry, UnityEditor.dll");
            if (logEntriesType == null || logEntryType == null)
            {
                errorText = "Error: Could not find UnityEditor.LogEntries or LogEntry types.";
                return false;
            }

            MethodInfo startGettingEntries = logEntriesType.GetMethod("StartGettingEntries", StaticMethodFlags);
            MethodInfo getCount = logEntriesType.GetMethod("GetCount", StaticMethodFlags);
            MethodInfo getEntryInternal = logEntriesType.GetMethod("GetEntryInternal", StaticMethodFlags);
            MethodInfo endGettingEntries = logEntriesType.GetMethod("EndGettingEntries", StaticMethodFlags);
            if (startGettingEntries == null || getCount == null || getEntryInternal == null || endGettingEntries == null)
            {
                errorText = "Error: Could not find necessary methods on UnityEditor.LogEntries.";
                return false;
            }

            FieldInfo messageField = logEntryType.GetField("message", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (messageField == null)
            {
                errorText = "Error: Could not find 'message' field on LogEntry type.";
                return false;
            }

            MethodInfo getEntryTimestampInternal = logEntriesType.GetMethod("GetEntryTimestampInternal", StaticMethodFlags);

            accessor = new ReflectionAccessor(
                logEntryType,
                messageField,
                startGettingEntries,
                getCount,
                getEntryInternal,
                endGettingEntries,
                getEntryTimestampInternal);

            return true;
        }

        private static void SafeInvokeMethod(MethodInfo method, string methodName)
        {
            try
            {
                method?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Error($"UnityConsoleLogReader: error invoking {methodName}: {ex.Message}");
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
                UnityCodeMcpServerLogger.Error($"UnityConsoleLogReader: error getting log count: {ex.Message}");
                return 0;
            }
        }

        private sealed class ReflectionAccessor
        {
            public ReflectionAccessor(
                Type logEntryType,
                FieldInfo messageField,
                MethodInfo startGettingEntries,
                MethodInfo getCount,
                MethodInfo getEntryInternal,
                MethodInfo endGettingEntries,
                MethodInfo getEntryTimestampInternal)
            {
                LogEntryType = logEntryType;
                MessageField = messageField;
                StartGettingEntries = startGettingEntries;
                GetCount = getCount;
                GetEntryInternal = getEntryInternal;
                EndGettingEntries = endGettingEntries;
                GetEntryTimestampInternal = getEntryTimestampInternal;
            }

            public Type LogEntryType { get; }

            public FieldInfo MessageField { get; }

            public MethodInfo StartGettingEntries { get; }

            public MethodInfo GetCount { get; }

            public MethodInfo GetEntryInternal { get; }

            public MethodInfo EndGettingEntries { get; }

            public MethodInfo GetEntryTimestampInternal { get; }
        }
    }
}
