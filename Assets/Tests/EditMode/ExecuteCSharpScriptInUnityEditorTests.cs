using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityCodeMcpServer.McpTools;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Registry;
using UnityCodeMcpServer.Services;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class ExecuteCSharpScriptInUnityEditorTests
    {
        [Test]
        public void BuildResult_ProducesCombinedTextAndFlags()
        {
            MethodInfo method = typeof(ExecuteCSharpScriptInUnityEditor).GetMethod(
                "CreateToolCallResult",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "CreateToolCallResult should exist and be non-public static");

            ToolsCallResult result = (ToolsCallResult)method.Invoke(
                null,
                new object[]
                {
                    true,
                    "compilation_error",
                    "42",
                    "log line",
                    "error line",
                    null
                });

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content, Is.Not.Null);
            Assert.That(result.Content.Count, Is.EqualTo(1));
            string text = result.Content[0].Text;
            Assert.That(text, Does.Contain("Status: compilation_error"));
            Assert.That(text, Does.Contain("### Result"));
            Assert.That(text, Does.Contain("42"));
            Assert.That(text, Does.Contain("### Logs"));
            Assert.That(text, Does.Contain("log line"));
            Assert.That(text, Does.Contain("### Errors"));
            Assert.That(text, Does.Contain("error line"));
        }

        [Test]
        public void BuildCompilationBlockedResult_ReturnsCompilerErrorMessage()
        {
            ToolsCallResult result = ExecuteCSharpScriptInUnityEditor.BuildCompilationBlockedResult(isCompiling: false, hasCompileErrors: true);

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content[0].Text, Does.Contain("Cannot execute C# scripts while the project has compiler errors"));
        }

        [Test]
        public void BuildCompilationBlockedResult_ReturnsCompilingMessage()
        {
            ToolsCallResult result = ExecuteCSharpScriptInUnityEditor.BuildCompilationBlockedResult(isCompiling: true, hasCompileErrors: false);

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content[0].Text, Does.Contain("Cannot execute C# scripts while the editor is compiling scripts"));
        }

        [UnityTest]
        public IEnumerator ExecuteAsync_ReturnsSuccess_ForSimpleScript() => UniTask.ToCoroutine(async () =>
        {
            ExecuteCSharpScriptInUnityEditor tool = new();
            JsonElement args = JsonHelper.ParseElement(@"{""script"": ""return 2 + 3;""}");

            ToolsCallResult result = await tool.ExecuteAsync(args);

            Assert.That(result.IsError, Is.False);
            Assert.That(result.Content, Is.Not.Empty);
            Assert.That(result.Content[0].Text, Does.Contain("### Result"));
            Assert.That(result.Content[0].Text, Does.Contain("5"));
            Assert.That(result.Content[0].Text, Does.Contain("Status: SUCCESS"));
        });

        [UnityTest]
        public IEnumerator ExecuteAsync_MarksSceneDirty_AfterSuccessfulExecution() => UniTask.ToCoroutine(async () =>
        {
            Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            Assert.That(scene.isDirty, Is.False, "Scene should start clean");

            ExecuteCSharpScriptInUnityEditor tool = new();
            JsonElement args = JsonHelper.ParseElement(@"{""script"": ""return 1;""}");

            ToolsCallResult result = await tool.ExecuteAsync(args);

            Assert.That(result.IsError, Is.False);
            Assert.That(scene.isDirty, Is.True, "Scene should be marked dirty after successful ExecuteAsync");
        });

        [Test]
        public void MarkActiveSceneDirtyIfNeeded_MarksSceneDirty_WhenNotPlaying()
        {
            // Load a new temporary scene so we have a valid, clean scene to mark dirty
            Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            Assert.That(scene.isDirty, Is.False, "Fresh scene should not be dirty");

            ScriptExecutionService service = new();
            service.MarkActiveSceneDirtyIfNeeded();

            Assert.That(scene.isDirty, Is.True, "Scene should be marked dirty after calling MarkActiveSceneDirtyIfNeeded");
        }

        [Test]
        public void MarkActiveSceneDirtyIfNeeded_DoesNotThrow_WhenSceneIsValid()
        {
            ScriptExecutionService service = new();
            Assert.DoesNotThrow(() => service.MarkActiveSceneDirtyIfNeeded());
        }

        [Test]
        public void ExecuteScript_ReturnsSuccess_ForSimpleScript()
        {
            ScriptExecutionService service = new();

            ScriptExecutionService.ExecutionResult result = service.ExecuteScript("return 2 + 3;");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Status, Is.EqualTo("SUCCESS"));
            Assert.That(result.ResultText, Is.EqualTo("5"));
        }

        [Test]
        public void ExecuteScript_ReturnsCompilationError_ForInvalidScript()
        {
            ScriptExecutionService service = new();

            LogAssert.Expect(LogType.Error, new Regex("Script execution compilation error", RegexOptions.Singleline));
            ScriptExecutionService.ExecutionResult result = service.ExecuteScript("this is not valid csharp");

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Status, Is.EqualTo("COMPILATION_ERROR"));
            Assert.That(result.Errors, Does.Contain("error"));
        }

        [UnityTest]
        public IEnumerator ExecuteAsync_ReturnsError_ForCompilationIssue() => UniTask.ToCoroutine(async () =>
        {
            ExecuteCSharpScriptInUnityEditor tool = new();
            JsonElement args = JsonHelper.ParseElement(@"{""script"": ""this is not valid csharp""}");

            LogAssert.Expect(LogType.Error, new Regex("Script execution compilation error", RegexOptions.Singleline));
            LogAssert.Expect(LogType.Error, new Regex("ExecuteCSharpScriptInUnityEditor result", RegexOptions.Singleline));

            ToolsCallResult result = await tool.ExecuteAsync(args);

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content[0].Text, Does.Contain("COMPILATION_ERROR"));
        });

        [UnityTest]
        public IEnumerator ExecuteAsync_CapturesLogsAndErrors_FromScriptLogs() => UniTask.ToCoroutine(async () =>
        {
            ExecuteCSharpScriptInUnityEditor tool = new();
            string script = "Debug.Log(\"debug log\"); Debug.LogWarning(\"warning log\"); Debug.LogError(\"error log\"); return 7;";
            JsonElement args = BuildScriptArguments(script);

            LogAssert.Expect(LogType.Error, new Regex("error log", RegexOptions.Singleline));

            // You can now await normally inside this lambda
            ToolsCallResult result = await tool.ExecuteAsync(args);

            Assert.That(result.IsError, Is.False);
            Assert.That(result.Content, Is.Not.Empty);
            string text = result.Content[0].Text;

            Assert.That(text, Does.Contain("Status: SUCCESS_WITH_ERRORS"));
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
            ExecuteCSharpScriptInUnityEditor tool = new();
            string script = "throw new System.InvalidOperationException(\"runtime boom\");";
            JsonElement args = BuildScriptArguments(script);

            LogAssert.Expect(LogType.Error, new Regex("Script execution runtime error", RegexOptions.Singleline));
            LogAssert.Expect(LogType.Error, new Regex("ExecuteCSharpScriptInUnityEditor result", RegexOptions.Singleline));

            ToolsCallResult result = await tool.ExecuteAsync(args);

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content, Is.Not.Empty);
            string text = result.Content[0].Text;
            Assert.That(text, Does.Contain("Status: EXECUTION_ERROR"));
            Assert.That(text, Does.Contain("InvalidOperationException"));
            Assert.That(text, Does.Contain("runtime boom"));
            Assert.That(text, Does.Contain("### Errors"));
        });

        [UnityTest]
        public IEnumerator Registry_CanExecuteScriptExecutionTool() => UniTask.ToCoroutine(async () =>
        {
            McpRegistry registry = new();
            registry.DiscoverAndRegisterAll();

            JsonElement arguments = JsonHelper.ParseElement(@"{""script"": ""return 2 + 3;""}");
            ToolsCallResult result = await registry.ExecuteToolAsync("execute_csharp_script_in_unity_editor", arguments);

            Assert.That(result.IsError, Is.False);
            Assert.That(result.Content, Is.Not.Empty);
            Assert.That(result.Content[0].Text, Does.Contain("### Result"));
            Assert.That(result.Content[0].Text, Does.Contain("5"));
            Assert.That(result.Content[0].Text, Does.Contain("Status: SUCCESS"));
        });

        private static JsonElement BuildScriptArguments(string script)
        {
            string escaped = script.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return JsonHelper.ParseElement($@"{{""script"": ""{escaped}""}}");
        }
    }
}
