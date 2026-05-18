using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityCodeMcpServer.Helpers
{
    public enum UnityConsoleLogSeverity
    {
        Unknown = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    public readonly struct UnityConsoleLogEntry
    {
        public UnityConsoleLogEntry(string message)
            : this(message, null, UnityConsoleLogSeverity.Unknown)
        {
        }

        public UnityConsoleLogEntry(string message, string stackTrace, UnityConsoleLogSeverity severity)
        {
            Message = message ?? string.Empty;
            StackTrace = string.IsNullOrWhiteSpace(stackTrace) ? null : stackTrace.Trim();
            Severity = severity;
        }

        public string Message { get; }

        public string StackTrace { get; }

        public UnityConsoleLogSeverity Severity { get; }
    }

    public readonly struct UnityConsoleLogReadResult
    {
        public UnityConsoleLogReadResult(IReadOnlyList<UnityConsoleLogEntry> entries, int totalCount, string errorText, bool isError)
        {
            Entries = entries ?? Array.Empty<UnityConsoleLogEntry>();
            TotalCount = Math.Max(0, totalCount);
            ErrorText = string.IsNullOrWhiteSpace(errorText) ? null : errorText.Trim();
            IsError = isError;
        }

        public IReadOnlyList<UnityConsoleLogEntry> Entries { get; }

        public int TotalCount { get; }

        public string ErrorText { get; }

        public bool IsError { get; }
    }

    public static class UnityConsoleLogReader
    {
        public const int MaxEntriesLimit = 1000;

        private static readonly BindingFlags StaticMethodFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly Lazy<LogMessageFlagsAccessor> LogMessageFlagsAccessorInstance =
            new(CreateLogMessageFlagsAccessor);

        public static UnityConsoleLogReadResult ReadTail(int maxEntries)
        {
            int effectiveLimit = NormalizeMaxEntries(maxEntries);
            if (!TryCreateAccessor(out ReflectionAccessor accessor, out string errorText))
            {
                return new UnityConsoleLogReadResult(Array.Empty<UnityConsoleLogEntry>(), 0, errorText, true);
            }

            try
            {
                SafeInvokeMethod(accessor.StartGettingEntries, "StartGettingEntries");

                int totalCount = SafeGetLogCount(accessor.GetCount);
                if (totalCount <= 0)
                {
                    return new UnityConsoleLogReadResult(Array.Empty<UnityConsoleLogEntry>(), 0, null, false);
                }

                IReadOnlyList<UnityConsoleLogEntry> entries = ReadTailEntries(accessor, totalCount, effectiveLimit);
                return new UnityConsoleLogReadResult(entries, totalCount, null, false);
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Error($"UnityConsoleLogReader: error reading logs: {ex.Message}");
                return new UnityConsoleLogReadResult(Array.Empty<UnityConsoleLogEntry>(), 0, $"Error reading logs: {ex.Message}", true);
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

        private static IReadOnlyList<UnityConsoleLogEntry> ReadTailEntries(ReflectionAccessor accessor, int totalCount, int maxEntries)
        {
            int startIndex = Math.Max(0, totalCount - maxEntries);
            List<UnityConsoleLogEntry> entries = new(totalCount - startIndex);

            for (int row = startIndex; row < totalCount; row++)
            {
                if (!TryCreateStructuredEntry(accessor, row, out UnityConsoleLogEntry entry))
                {
                    continue;
                }

                entries.Add(entry);
            }

            return entries;
        }

        private static bool TryCreateStructuredEntry(ReflectionAccessor accessor, int row, out UnityConsoleLogEntry entry)
        {
            entry = default;

            if (!TryReadRawEntry(accessor, row, out object logEntry, out string rawMessage))
            {
                return false;
            }

            int mode = SafeGetMode(accessor.ModeField, logEntry);
            string formattedMessage = TryReadFormattedEntry(accessor, row, out string candidateMessage)
                ? candidateMessage
                : null;

            int messageLineCount = GetMessageLineCount(accessor, row);
            SplitEntryText(rawMessage, messageLineCount, out string fallbackMessage, out string fallbackStackTrace);

            string message = string.IsNullOrWhiteSpace(formattedMessage) ? fallbackMessage : formattedMessage;
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            entry = CreateStructuredEntry(rawMessage, message, fallbackStackTrace, mode);
            return true;
        }

        private static bool TryReadRawEntry(ReflectionAccessor accessor, int row, out object logEntry, out string rawMessage)
        {
            logEntry = Activator.CreateInstance(accessor.LogEntryType);
            rawMessage = null;

            object result = accessor.GetEntryInternal.Invoke(null, new[] { (object)row, logEntry });
            if (result is bool hasEntry && !hasEntry)
            {
                return false;
            }

            rawMessage = accessor.MessageField.GetValue(logEntry) as string;
            return !string.IsNullOrWhiteSpace(rawMessage);
        }

        private static int GetMessageLineCount(ReflectionAccessor accessor, int row)
        {
            return accessor.GetEntryCount != null ? SafeGetEntryCount(accessor.GetEntryCount, row) : 1;
        }

        private static UnityConsoleLogEntry CreateStructuredEntry(string rawMessage, string message, string fallbackStackTrace, int mode)
        {
            UnityConsoleLogSeverity severity = ClassifySeverity(mode);
            string stackTrace = ExtractStackTrace(rawMessage, message, fallbackStackTrace);
            return new UnityConsoleLogEntry(message, stackTrace, severity);
        }

        private static bool TryReadFormattedEntry(ReflectionAccessor accessor, int row, out string messageText)
        {
            messageText = null;

            if (accessor.GetLinesAndModeFromEntryInternal == null)
            {
                return false;
            }

            int lineCount = accessor.GetEntryCount != null ? SafeGetEntryCount(accessor.GetEntryCount, row) : 1;
            if (lineCount < 1)
            {
                lineCount = 1;
            }

            if (!TryGetEntryText(accessor.GetLinesAndModeFromEntryInternal, row, lineCount, out messageText, out _)
                || string.IsNullOrWhiteSpace(messageText))
            {
                return false;
            }

            return true;
        }

        private static UnityConsoleLogSeverity ClassifySeverity(int mode)
        {
            LogMessageFlagsAccessor accessor = LogMessageFlagsAccessorInstance.Value;
            if (accessor == null)
            {
                return UnityConsoleLogSeverity.Unknown;
            }

            object flags = Enum.ToObject(accessor.EnumType, mode);
            bool isInfo = InvokeFlagPredicate(accessor.IsInfoMethod, flags, "IsInfo");
            if (isInfo)
            {
                return UnityConsoleLogSeverity.Info;
            }

            if (InvokeFlagPredicate(accessor.IsWarningMethod, flags, "IsWarning"))
            {
                return UnityConsoleLogSeverity.Warning;
            }

            if (InvokeFlagPredicate(accessor.IsErrorMethod, flags, "IsError"))
            {
                return UnityConsoleLogSeverity.Error;
            }

            return UnityConsoleLogSeverity.Unknown;
        }

        private static int SafeGetMode(FieldInfo modeField, object logEntry)
        {
            try
            {
                object value = modeField?.GetValue(logEntry);
                return value is int mode ? mode : 0;
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Debug($"UnityConsoleLogReader: could not read mode: {ex.Message}");
                return 0;
            }
        }

        private static bool TryGetEntryText(MethodInfo getLinesAndModeFromEntryInternal, int row, int numberOfLines, out string text, out int mode)
        {
            text = null;
            mode = 0;

            try
            {
                object[] args = { row, numberOfLines, 0, null };
                getLinesAndModeFromEntryInternal.Invoke(null, args);
                mode = args[2] is int mask ? mask : 0;
                text = args[3] as string;
                return !string.IsNullOrWhiteSpace(text);
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Debug($"UnityConsoleLogReader: could not read formatted entry text for row {row}: {ex.Message}");
                return false;
            }
        }

        private static int SafeGetEntryCount(MethodInfo getEntryCount, int row)
        {
            try
            {
                object result = getEntryCount.Invoke(null, new object[] { row });
                return result is int count ? count : 0;
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Debug($"UnityConsoleLogReader: could not read entry count for row {row}: {ex.Message}");
                return 0;
            }
        }

        private static void SplitEntryText(string rawText, int messageLineCount, out string message, out string stackTrace)
        {
            string normalizedText = NormalizeLineEndings(rawText);
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                message = string.Empty;
                stackTrace = null;
                return;
            }

            string[] lines = normalizedText.Split('\n');
            int boundedMessageLineCount = Math.Max(1, Math.Min(messageLineCount, lines.Length));
            message = string.Join("\n", lines, 0, boundedMessageLineCount).TrimEnd();

            if (boundedMessageLineCount >= lines.Length)
            {
                stackTrace = null;
                return;
            }

            stackTrace = string.Join("\n", lines, boundedMessageLineCount, lines.Length - boundedMessageLineCount).Trim();
            if (string.IsNullOrWhiteSpace(stackTrace))
            {
                stackTrace = null;
            }
        }

        private static string ExtractStackTrace(string rawText, string messageText, string fallbackStackTrace)
        {
            string normalizedRawText = NormalizeLineEndings(rawText);
            string normalizedMessageText = NormalizeLineEndings(messageText);
            if (!string.IsNullOrWhiteSpace(normalizedRawText) && !string.IsNullOrWhiteSpace(normalizedMessageText)
                && normalizedRawText.StartsWith(normalizedMessageText, StringComparison.Ordinal))
            {
                string remainder = normalizedRawText[normalizedMessageText.Length..].TrimStart('\n');
                if (!string.IsNullOrWhiteSpace(remainder))
                {
                    return remainder.Trim();
                }
            }

            return fallbackStackTrace;
        }

        private static string ExtractFirstLine(string text)
        {
            string normalizedText = NormalizeLineEndings(text);
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                return string.Empty;
            }

            int firstNewline = normalizedText.IndexOf('\n');
            return firstNewline >= 0 ? normalizedText[..firstNewline].TrimEnd() : normalizedText;
        }

        private static string NormalizeLineEndings(string text)
        {
            return text?.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
        }

        private static LogMessageFlagsAccessor CreateLogMessageFlagsAccessor()
        {
            Type enumType = Type.GetType("UnityEditor.LogMessageFlags, UnityEditor.CoreModule")
                ?? Type.GetType("UnityEditor.LogMessageFlags, UnityEditor.dll");
            Type extensionsType = Type.GetType("UnityEditor.LogMessageFlagsExtensions, UnityEditor.CoreModule")
                ?? Type.GetType("UnityEditor.LogMessageFlagsExtensions, UnityEditor.dll");

            if (enumType == null || extensionsType == null)
            {
                UnityCodeMcpServerLogger.Debug("UnityConsoleLogReader: could not resolve UnityEditor.LogMessageFlags reflection types.");
                return null;
            }

            MethodInfo isInfoMethod = extensionsType.GetMethod("IsInfo", StaticMethodFlags, null, new[] { enumType }, null);
            MethodInfo isWarningMethod = extensionsType.GetMethod("IsWarning", StaticMethodFlags, null, new[] { enumType }, null);
            MethodInfo isErrorMethod = extensionsType.GetMethod("IsError", StaticMethodFlags, null, new[] { enumType }, null);
            if (isInfoMethod == null || isWarningMethod == null || isErrorMethod == null)
            {
                UnityCodeMcpServerLogger.Debug("UnityConsoleLogReader: could not resolve UnityEditor.LogMessageFlagsExtensions methods.");
                return null;
            }

            return new LogMessageFlagsAccessor(enumType, isInfoMethod, isWarningMethod, isErrorMethod);
        }

        private static bool InvokeFlagPredicate(MethodInfo method, object flags, string methodName)
        {
            try
            {
                object result = method.Invoke(null, new[] { flags });
                return result is bool value && value;
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Debug($"UnityConsoleLogReader: could not invoke {methodName} on LogMessageFlagsExtensions: {ex.Message}");
                return false;
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
            MethodInfo getEntryCount = logEntriesType.GetMethod("GetEntryCount", StaticMethodFlags);
            MethodInfo getLinesAndModeFromEntryInternal = logEntriesType.GetMethod("GetLinesAndModeFromEntryInternal", StaticMethodFlags);
            MethodInfo endGettingEntries = logEntriesType.GetMethod("EndGettingEntries", StaticMethodFlags);
            if (startGettingEntries == null || getCount == null || getEntryInternal == null || endGettingEntries == null)
            {
                errorText = "Error: Could not find necessary methods on UnityEditor.LogEntries.";
                return false;
            }

            FieldInfo messageField = logEntryType.GetField("message", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo modeField = logEntryType.GetField("mode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (messageField == null || modeField == null)
            {
                errorText = "Error: Could not find required fields on LogEntry type.";
                return false;
            }

            accessor = new ReflectionAccessor(
                logEntryType,
                messageField,
                modeField,
                startGettingEntries,
                getCount,
                getEntryInternal,
                getEntryCount,
                getLinesAndModeFromEntryInternal,
                endGettingEntries);

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
                FieldInfo modeField,
                MethodInfo startGettingEntries,
                MethodInfo getCount,
                MethodInfo getEntryInternal,
                MethodInfo getEntryCount,
                MethodInfo getLinesAndModeFromEntryInternal,
                MethodInfo endGettingEntries)
            {
                LogEntryType = logEntryType;
                MessageField = messageField;
                ModeField = modeField;
                StartGettingEntries = startGettingEntries;
                GetCount = getCount;
                GetEntryInternal = getEntryInternal;
                GetEntryCount = getEntryCount;
                GetLinesAndModeFromEntryInternal = getLinesAndModeFromEntryInternal;
                EndGettingEntries = endGettingEntries;
            }

            public Type LogEntryType { get; }

            public FieldInfo MessageField { get; }

            public FieldInfo ModeField { get; }

            public MethodInfo StartGettingEntries { get; }

            public MethodInfo GetCount { get; }

            public MethodInfo GetEntryInternal { get; }

            public MethodInfo GetEntryCount { get; }

            public MethodInfo GetLinesAndModeFromEntryInternal { get; }

            public MethodInfo EndGettingEntries { get; }
        }

        private sealed class LogMessageFlagsAccessor
        {
            public LogMessageFlagsAccessor(Type enumType, MethodInfo isInfoMethod, MethodInfo isWarningMethod, MethodInfo isErrorMethod)
            {
                EnumType = enumType;
                IsInfoMethod = isInfoMethod;
                IsWarningMethod = isWarningMethod;
                IsErrorMethod = isErrorMethod;
            }

            public Type EnumType { get; }

            public MethodInfo IsInfoMethod { get; }

            public MethodInfo IsWarningMethod { get; }

            public MethodInfo IsErrorMethod { get; }
        }
    }
}
