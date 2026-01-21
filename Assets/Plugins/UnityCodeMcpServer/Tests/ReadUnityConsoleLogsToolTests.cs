using System;
using System.Text.Json;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Tools;
using NUnit.Framework;
using UnityEngine;

namespace UnityCodeMcpServer.Tests
{
    public class ReadUnityConsoleLogsToolTests
    {
        [Test]
        public void Tool_Metadata_IsPresent()
        {
            var tool = new ReadUnityConsoleLogsTool();

            Assert.AreEqual("read_unity_console_logs", tool.Name);
            Assert.IsNotEmpty(tool.Description);
        }

        [Test]
        public void InputSchema_DefinesMaxEntries()
        {
            var schema = new ReadUnityConsoleLogsTool().InputSchema;

            Assert.AreEqual(JsonValueKind.Object, schema.ValueKind);
            Assert.IsTrue(schema.TryGetProperty("properties", out var properties));
            Assert.IsTrue(properties.TryGetProperty("max_entries", out var maxEntries));
            Assert.AreEqual(JsonValueKind.Object, maxEntries.ValueKind);
        }

        [Test]
        public void Execute_UsesDefaultLimit_WhenNotProvided()
        {
            int capturedLimit = -1;
            var tool = new ReadUnityConsoleLogsTool(limit =>
            {
                capturedLimit = limit;
                return ("stub log", false);
            });

            var result = tool.Execute(JsonHelper.ParseElement("{}"));

            Assert.AreEqual(200, capturedLimit);
            Assert.IsFalse(result.IsError);
            StringAssert.Contains("stub log", result.Content[0].Text);
        }

        [Test]
        public void Execute_ClampsLimit_ToUpperBound()
        {
            int capturedLimit = -1;
            var tool = new ReadUnityConsoleLogsTool(limit =>
            {
                capturedLimit = limit;
                return ("ok", false);
            });

            var args = JsonHelper.ParseElement("{\"max_entries\": 5000}");
            tool.Execute(args);

            Assert.AreEqual(1000, capturedLimit);
        }

        [Test]
        public void Execute_DefaultsLimit_WhenNegativeProvided()
        {
            int capturedLimit = -1;
            var tool = new ReadUnityConsoleLogsTool(limit =>
            {
                capturedLimit = limit;
                return ("ok", false);
            });

            var args = JsonHelper.ParseElement("{\"max_entries\": -5}");
            tool.Execute(args);

            Assert.AreEqual(200, capturedLimit);
        }

        [Test]
        public void Execute_PropagatesReaderError()
        {
            var tool = new ReadUnityConsoleLogsTool(_ => ("reader failure", true));

            var result = tool.Execute(JsonHelper.ParseElement("{}"));

            Assert.IsTrue(result.IsError);
            StringAssert.Contains("reader failure", result.Content[0].Text);
        }

        [Test]
        public void Execute_ReadsUnityLogs_AndReturnsContent()
        {
            var tool = new ReadUnityConsoleLogsTool();
            string uniqueMessage = "ConsoleLog_" + Guid.NewGuid();
            Debug.Log(uniqueMessage);

            var result = tool.Execute(JsonHelper.ParseElement("{}"));

            Assert.IsFalse(result.IsError);
            Assert.IsNotEmpty(result.Content);
            StringAssert.Contains(uniqueMessage, result.Content[0].Text);
        }

        [Test]
        public void Execute_EmitsTruncationHeader_WhenReaderIndicatesTruncation()
        {
            int capturedLimit = -1;
            var tool = new ReadUnityConsoleLogsTool(limit =>
            {
                capturedLimit = limit;
                return ($"--- Showing last {limit} logs (Total: {limit + 1}) ---\nold\nnew", false);
            });

            var result = tool.Execute(JsonHelper.ParseElement("{\"max_entries\": 1}"));

            Assert.AreEqual(1, capturedLimit);
            Assert.IsFalse(result.IsError);
            StringAssert.Contains("Showing last 1 logs", result.Content[0].Text);
        }
    }
}