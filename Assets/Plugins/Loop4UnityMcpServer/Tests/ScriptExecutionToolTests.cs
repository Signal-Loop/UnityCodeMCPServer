using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using LoopMcpServer.Protocol;
using LoopMcpServer.Registry;
using LoopMcpServer.Tools;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LoopMcpServer.Tests
{
    [TestFixture]
    public class ScriptExecutionToolTests
    {
        [Test]
        public void BuildResult_ProducesCombinedTextAndFlags()
        {
            var method = typeof(ScriptExecutionTool).GetMethod(
                "BuildResult",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "BuildResult should exist and be non-public static");

            var result = (ToolsCallResult)method.Invoke(
                null,
                new object[]
                {
                    true,
                    "compilation_error",
                    "42",
                    "log line",
                    "error line",
                    "return 42;"
                });

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content, Is.Not.Null);
            Assert.That(result.Content.Count, Is.EqualTo(1));
            var text = result.Content[0].Text;
            Assert.That(text, Does.Contain("Status: compilation_error"));
            Assert.That(text, Does.Contain("### Result"));
            Assert.That(text, Does.Contain("42"));
            Assert.That(text, Does.Contain("### Script"));
            Assert.That(text, Does.Contain("return 42;"));
            Assert.That(text, Does.Contain("### Logs"));
            Assert.That(text, Does.Contain("log line"));
            Assert.That(text, Does.Contain("### Errors"));
            Assert.That(text, Does.Contain("error line"));
        }

        [UnityTest]
        public IEnumerator ExecuteAsync_ReturnsSuccess_ForSimpleScript() => UniTask.ToCoroutine(async () =>
        {
            var tool = new ScriptExecutionTool();
            var args = JsonHelper.ParseElement(@"{""script"": ""return 2 + 3;""}");

            var result = await tool.ExecuteAsync(args);

            Assert.That(result.IsError, Is.False);
            Assert.That(result.Content, Is.Not.Empty);
            Assert.That(result.Content[0].Text, Does.Contain("### Result"));
            Assert.That(result.Content[0].Text, Does.Contain("5"));
            Assert.That(result.Content[0].Text, Does.Contain("Status: success"));
        });

        [UnityTest]
        public IEnumerator ExecuteAsync_ReturnsError_ForCompilationIssue() => UniTask.ToCoroutine(async () =>
        {
            var tool = new ScriptExecutionTool();
            var args = JsonHelper.ParseElement(@"{""script"": ""this is not valid csharp""}");

            LogAssert.Expect(LogType.Error, new Regex("Script execution compilation error", RegexOptions.Singleline));
            LogAssert.Expect(LogType.Error, new Regex("ScriptExecutionTool result", RegexOptions.Singleline));

            var result = await tool.ExecuteAsync(args);

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content[0].Text, Does.Contain("compilation_error"));
        });

        [UnityTest]
        public IEnumerator ExecuteAsync_CapturesLogsAndErrors_FromScriptLogs() => UniTask.ToCoroutine(async () =>
        {
            var tool = new ScriptExecutionTool();
            var script = "Debug.Log(\"debug log\"); Debug.LogWarning(\"warning log\"); Debug.LogError(\"error log\"); return 7;";
            var args = BuildScriptArguments(script);

            LogAssert.Expect(LogType.Error, new Regex("error log", RegexOptions.Singleline));

            // You can now await normally inside this lambda
            var result = await tool.ExecuteAsync(args);

            Assert.That(result.IsError, Is.False);
            Assert.That(result.Content, Is.Not.Empty);
            var text = result.Content[0].Text;

            Assert.That(text, Does.Contain("Status: success_with_errors"));
            Assert.That(text, Does.Contain("Standard Log:"));
            Assert.That(text, Does.Contain("warning log"));
            Assert.That(text, Does.Contain("debug log"));
            Assert.That(text, Does.Contain("Errors Log:"));
            Assert.That(text, Does.Contain("error log"));
            Assert.That(text, Does.Contain("### Errors"));
        });

        [UnityTest]
        public IEnumerator ExecuteAsync_ReturnsError_ForRuntimeException() => UniTask.ToCoroutine(async () =>
        {
            var tool = new ScriptExecutionTool();
            var script = "throw new System.InvalidOperationException(\"runtime boom\");";
            var args = BuildScriptArguments(script);

            LogAssert.Expect(LogType.Error, new Regex("Script execution runtime error", RegexOptions.Singleline));
            LogAssert.Expect(LogType.Error, new Regex("ScriptExecutionTool result", RegexOptions.Singleline));

            var result = await tool.ExecuteAsync(args);

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content, Is.Not.Empty);
            var text = result.Content[0].Text;
            Assert.That(text, Does.Contain("Status: execution_error"));
            Assert.That(text, Does.Contain("InvalidOperationException"));
            Assert.That(text, Does.Contain("runtime boom"));
            Assert.That(text, Does.Contain("### Errors"));
        });

        [UnityTest]
        public IEnumerator Registry_CanExecuteScriptExecutionTool() => UniTask.ToCoroutine(async () =>
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            var arguments = JsonHelper.ParseElement(@"{""script"": ""return 2 + 3;""}");
            var result = await registry.ExecuteToolAsync("execute_csharp_script_in_unity_editor", arguments);

            Assert.That(result.IsError, Is.False);
            Assert.That(result.Content, Is.Not.Empty);
            Assert.That(result.Content[0].Text, Does.Contain("### Result"));
            Assert.That(result.Content[0].Text, Does.Contain("5"));
            Assert.That(result.Content[0].Text, Does.Contain("Status: success"));
        });

        private static JsonElement BuildScriptArguments(string script)
        {
            var escaped = script.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return JsonHelper.ParseElement($@"{{""script"": ""{escaped}""}}");
        }
    }
}
