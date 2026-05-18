using System;
using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.McpTools;
using UnityCodeMcpServer.Protocol;
using UnityEngine;

namespace UnityCodeMcpServer.Tests.EditMode
{
    public class ReadUnityConsoleLogsToolTests
    {
        [Test]
        public void Tool_Metadata_IsPresent()
        {
            ReadUnityConsoleLogsTool tool = new();

            Assert.AreEqual("read_unity_console_logs", tool.Name);
            Assert.IsNotEmpty(tool.Description);
        }

        [Test]
        public void InputSchema_DefinesMaxEntries()
        {
            JsonElement schema = new ReadUnityConsoleLogsTool().InputSchema;

            Assert.AreEqual(JsonValueKind.Object, schema.ValueKind);
            Assert.IsTrue(schema.TryGetProperty("properties", out JsonElement properties));
            Assert.IsTrue(properties.TryGetProperty("max_entries", out JsonElement maxEntries));
            Assert.AreEqual(JsonValueKind.Object, maxEntries.ValueKind);
        }

        [Test]
        public void Execute_UsesDefaultLimit_WhenNotProvided()
        {
            int capturedLimit = -1;
            ReadUnityConsoleLogsTool tool = new(limit =>
            {
                capturedLimit = limit;
                return CreateReaderResult(new UnityConsoleLogEntry("stub log", null, UnityConsoleLogSeverity.Info));
            });

            ToolsCallResult result = tool.Execute(JsonHelper.ParseElement("{}"));

            Assert.AreEqual(200, capturedLimit);
            Assert.IsFalse(result.IsError);
            StringAssert.Contains("stub log", result.Content[0].Text);
        }

        [Test]
        public void Execute_ClampsLimit_ToUpperBound()
        {
            int capturedLimit = -1;
            ReadUnityConsoleLogsTool tool = new(limit =>
            {
                capturedLimit = limit;
                return CreateReaderResult(new UnityConsoleLogEntry("ok", null, UnityConsoleLogSeverity.Info));
            });

            JsonElement args = JsonHelper.ParseElement("{\"max_entries\": 5000}");
            tool.Execute(args);

            Assert.AreEqual(1000, capturedLimit);
        }

        [Test]
        public void Execute_DefaultsLimit_WhenNegativeProvided()
        {
            int capturedLimit = -1;
            ReadUnityConsoleLogsTool tool = new(limit =>
            {
                capturedLimit = limit;
                return CreateReaderResult(new UnityConsoleLogEntry("ok", null, UnityConsoleLogSeverity.Info));
            });

            JsonElement args = JsonHelper.ParseElement("{\"max_entries\": -5}");
            tool.Execute(args);

            Assert.AreEqual(200, capturedLimit);
        }

        [Test]
        public void Execute_PropagatesReaderError()
        {
            ReadUnityConsoleLogsTool tool = new(_ => new UnityConsoleLogReadResult(Array.Empty<UnityConsoleLogEntry>(), 0, "reader failure", true));

            ToolsCallResult result = tool.Execute(JsonHelper.ParseElement("{}"));

            Assert.IsTrue(result.IsError);
            StringAssert.Contains("reader failure", result.Content[0].Text);
        }

        [Test]
        public void Execute_ReadsUnityLogs_AndReturnsContent()
        {
            ReadUnityConsoleLogsTool tool = new();
            string uniqueMessage = "ConsoleLog_" + Guid.NewGuid();
            Debug.Log(uniqueMessage);

            ToolsCallResult result = tool.Execute(JsonHelper.ParseElement("{}"));

            Assert.IsFalse(result.IsError);
            Assert.IsNotEmpty(result.Content);
            StringAssert.Contains(uniqueMessage, result.Content[0].Text);
        }

        [Test]
        public void Execute_EmitsTruncationHeader_WhenReaderIndicatesTruncation()
        {
            int capturedLimit = -1;
            ReadUnityConsoleLogsTool tool = new(limit =>
            {
                capturedLimit = limit;
                return new UnityConsoleLogReadResult(
                    new[]
                    {
                        new UnityConsoleLogEntry("new", null, UnityConsoleLogSeverity.Info)
                    },
                    limit + 1,
                    null,
                    false);
            });

            ToolsCallResult result = tool.Execute(JsonHelper.ParseElement("{\"max_entries\": 1}"));

            Assert.AreEqual(1, capturedLimit);
            Assert.IsFalse(result.IsError);
            StringAssert.Contains("Showing last 1 logs", result.Content[0].Text);
        }

        [Test]
        public void Execute_Replaces_EmptyReaderText_WithPlaceholder()
        {
            ReadUnityConsoleLogsTool tool = new(_ => new UnityConsoleLogReadResult(Array.Empty<UnityConsoleLogEntry>(), 0, null, false));

            ToolsCallResult result = tool.Execute(JsonHelper.ParseElement("{}"));

            Assert.IsFalse(result.IsError);
            StringAssert.Contains("(No console logs available)", result.Content[0].Text);
        }

        [Test]
        public void SelectTail_ReturnsNewestLogs_LikeTail()
        {
            IReadOnlyList<UnityConsoleLogEntry> tail = UnityConsoleLogReader.SelectTail(
                new[]
                {
                    new UnityConsoleLogEntry("oldest"),
                    new UnityConsoleLogEntry("middle"),
                    new UnityConsoleLogEntry("newest")
                },
                2);

            Assert.AreEqual(2, tail.Count);
            Assert.AreEqual("middle", tail[0].Message);
            Assert.AreEqual("newest", tail[1].Message);
        }

        [Test]
        public void FormatEntries_RendersMessageWithoutTimestampPrefix()
        {
            string text = ReadUnityConsoleLogsTool.FormatEntries(
                new[]
                {
                    new UnityConsoleLogEntry("message")
                },
                1,
                1);

            Assert.AreEqual("message", text);
        }

        [Test]
        public void FormatEntries_StripsUnknownSeverityStackTrace()
        {
            string text = ReadUnityConsoleLogsTool.FormatEntries(
                new[]
                {
                    new UnityConsoleLogEntry("message\nsecond-line", "unknown-stack", UnityConsoleLogSeverity.Unknown)
                },
                1,
                1);

            StringAssert.Contains("message\nsecond-line", text);
            StringAssert.DoesNotContain("unknown-stack", text);
        }

        [Test]
        public void Execute_StripsStackTrace_ForPlainLogs_AndWarnings()
        {
            ReadUnityConsoleLogsTool tool = new();
            string probeId = Guid.NewGuid().ToString("N");
            string plainLog = "plain-log-" + probeId;
            string warningLog = "warning-log-" + probeId;

            Debug.Log(plainLog);
            Debug.LogWarning(warningLog);

            ToolsCallResult result = tool.Execute(JsonHelper.ParseElement("{\"max_entries\": 5}"));
            string text = result.Content[0].Text;

            StringAssert.DoesNotContain($"{plainLog}\nUnityEngine.Debug:Log (object)", text);
            StringAssert.Contains(warningLog, text);
            StringAssert.DoesNotContain($"{warningLog}\nUnityEngine.Debug:LogWarning (object)", text);
        }

        [Test]
        public void FormatEntries_KeepsStackTrace_ForErrors()
        {
            string text = ReadUnityConsoleLogsTool.FormatEntries(
                new[]
                {
                    new UnityConsoleLogEntry("error-message", "error-stack", UnityConsoleLogSeverity.Error)
                },
                1,
                1);

            StringAssert.Contains("error-message", text);
            StringAssert.Contains("error-stack", text);
        }

        private static UnityConsoleLogReadResult CreateReaderResult(params UnityConsoleLogEntry[] entries)
        {
            return new UnityConsoleLogReadResult(entries, entries.Length, null, false);
        }
    }
}
