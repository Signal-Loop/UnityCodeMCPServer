using System;
using System.Text.Json;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityEditor;

namespace UnityCodeMcpServer.McpTools
{
    /// <summary>
    /// Tool that reads Unity Editor console logs via reflection.
    /// Provides recent log entries as text while guarding against reflection failures.
    /// </summary>
    public class ReadUnityConsoleLogsTool : ITool
    {
        private const int DefaultMaxEntries = 200;

        private readonly Func<int, (string text, bool isError)> _logReader;

        public ReadUnityConsoleLogsTool()
            : this(null)
        {
        }

        public ReadUnityConsoleLogsTool(Func<int, (string text, bool isError)> logReader)
        {
            _logReader = logReader ?? UnityConsoleLogReader.ReadTail;
        }

        public string Name => "read_unity_console_logs";

        public string Description =>
            @"Retrieves recent log entries from the Unity Editor Console. 

**WHEN TO USE:**
- To debug compilation errors, runtime exceptions, or Unity Editor issues.
- To investigate why a C# script execution failed or produced unexpected results.
- To verify the status of background tasks, asset imports, or editor actions that might generate silent warnings/errors.

**PARAMETERS & USAGE GUIDELINES:**
- `max_entries` (Optional): Limits the number of returned logs. You MUST use this to protect your context window from token bloat. Recommend setting this to 20-50 entries for standard debugging.
- Output includes the log type (Message, Warning, Error, Exception), the log message, and stack traces where applicable.";

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

            string mode = EditorApplication.isPlaying ? "Play Mode" : "Edit Mode";
            text = $"**Unity Editor is in {mode}**\n\n{text}";

            return ToolsCallResult.TextResult(text, isError);
        }

        private static int NormalizeMaxEntries(int requested)
        {
            if (requested < 1)
            {
                return DefaultMaxEntries;
            }

            return Math.Min(requested, UnityConsoleLogReader.MaxEntriesLimit);
        }
    }
}