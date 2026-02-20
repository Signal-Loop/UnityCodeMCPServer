using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityCodeMcpServer.Tools
{
    public class RunUnityTestsTool : IToolAsync
    {
        public string Name => "run_unity_tests";

        public string Description =>
            @"Executes Unity tests via the TestRunnerApi and returns the results.

**CRITICAL DIRECTIVES:**
- ALWAYS run relevant tests after modifying scripts or editor state to ensure you haven't broken existing functionality.
- If a test fails, analyze the returned error message and stack trace, then fix the code and re-run the test.
- Prefer running specific tests by name when debugging a localized issue. Running ALL tests can consume too much time and context window space.

**PARAMETERS & USAGE GUIDELINES:**
- `test_mode` (String): Determines the execution context.
  - `EditMode` (Default): Runs quickly in the Editor without entering Play mode. Prefer this for pure C# logic, mathematical calculations, and standard unit tests.
  - `PlayMode`: Enters Play mode. Use ONLY when testing `MonoBehaviour` lifecycles (Start/Update), physics, or runtime-specific behaviors.
  - `Both`: Runs EditMode followed by PlayMode.
- `test_names` (Array/List, Optional): Specific test names to run. If left empty, runs ALL tests for the selected mode.

**OUTPUT:**
Returns pass/fail status, total execution time, and detailed stack traces for any test failures.";

        public JsonElement InputSchema => JsonHelper.ParseElement(@"
        {
            ""type"": ""object"",
            ""properties"": {
                ""tests"": {
                    ""type"": ""array"",
                    ""items"": {
                        ""type"": ""string""
                    },
                    ""description"": ""Optional list of test names to run. If omitted, all tests are run. NOTE: You must use fully qualified test names (e.g. 'Namespace.ClassName.MethodName').""
                },
                ""test_mode"": {
                    ""type"": ""string"",
                    ""enum"": [""EditMode"", ""PlayMode"", ""Both""],
                    ""description"": ""Optional test mode to run. Defaults to EditMode if not specified, but this tool can handle both.""
                }
            }
        }
        ");

        public async UniTask<ToolsCallResult> ExecuteAsync(JsonElement arguments)
        {
            var options = ParseArguments(arguments);

            if (ShouldBlockEditMode(options.Mode, EditorApplication.isPlaying))
            {
                return BuildEditModeBlockedResult();
            }

            // Save dirty scenes and capture current scene state before running tests
            SaveDirtyScenes();
            var sceneState = CaptureCurrentSceneState();

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();

            try
            {
                if (options.Mode == (TestMode.EditMode | TestMode.PlayMode))
                {
                    // Run both modes sequentially
                    var editResult = await RunModeAsync(api, TestMode.EditMode, options.TestNames);
                    var playResult = await RunModeAsync(api, TestMode.PlayMode, options.TestNames);
                    return BuildCombinedResult(editResult, playResult);
                }
                else
                {
                    var result = await RunModeAsync(api, options.Mode, options.TestNames);
                    return BuildResult(result);
                }
            }
            catch (Exception ex)
            {
                return ToolsCallResult.ErrorResult($"Error executing tests: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(api);
                RestoreSceneState(sceneState);
            }
        }

        private async UniTask<ITestResultAdaptor> RunModeAsync(TestRunnerApi api, TestMode mode, string[] testNames)
        {
            var callbacks = new TestCallbacks();
            api.RegisterCallbacks(callbacks);

            var filter = new Filter()
            {
                testMode = mode
            };

            if (testNames != null && testNames.Length > 0)
            {
                filter.testNames = testNames;
            }

            try
            {
                api.Execute(new ExecutionSettings(filter));
                return await callbacks.ResultTask;
            }
            finally
            {
                api.UnregisterCallbacks(callbacks);
            }
        }

        public static bool ShouldBlockEditMode(TestMode mode, bool isPlaying)
        {
            if (!isPlaying)
            {
                return false;
            }

            return (mode & TestMode.EditMode) == TestMode.EditMode;
        }

        public static ToolsCallResult BuildEditModeBlockedResult()
        {
            return ToolsCallResult.ErrorResult("Cannot run EditMode tests while the editor is in Play Mode.");
        }

        /// <summary>
        /// Checks if any currently open scenes are dirty (have unsaved changes) and saves them.
        /// </summary>
        public static void SaveDirtyScenes()
        {
            var sceneCount = SceneManager.sceneCount;
            var dirtyScenes = new List<Scene>();

            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                {
                    dirtyScenes.Add(scene);
                }
            }

            if (dirtyScenes.Count > 0)
            {
                var scenesToSave = dirtyScenes.ToArray();
                EditorSceneManager.SaveScenes(scenesToSave);
            }
        }

        /// <summary>
        /// Captures the paths of currently open scenes.
        /// </summary>
        /// <returns>A list of scene paths that are currently open.</returns>
        private static List<string> CaptureCurrentSceneState()
        {
            var sceneState = new List<string>();
            var sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!string.IsNullOrEmpty(scene.path))
                {
                    sceneState.Add(scene.path);
                }
            }
            return sceneState;
        }

        /// <summary>
        /// Restores the scenes that were open before test execution.
        /// Closes any temporary scenes that were loaded during tests.
        /// </summary>
        private static void RestoreSceneState(List<string> originalScenePaths)
        {
            try
            {
                // Get currently open scenes
                var currentScenes = new Dictionary<string, Scene>();
                var sceneCount = SceneManager.sceneCount;
                for (int i = 0; i < sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!string.IsNullOrEmpty(scene.path))
                    {
                        currentScenes[scene.path] = scene;
                    }
                }

                // Close scenes that weren't in the original state
                var scenesToClose = new List<Scene>();
                foreach (var kvp in currentScenes)
                {
                    if (!originalScenePaths.Contains(kvp.Key))
                    {
                        scenesToClose.Add(kvp.Value);
                    }
                }

                foreach (var scene in scenesToClose)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }

                // Reopen original scenes that are not currently open
                foreach (var scenePath in originalScenePaths)
                {
                    if (!currentScenes.ContainsKey(scenePath))
                    {
                        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                    }
                }
            }
            catch (Exception ex)
            {
                LoopLogger.Warn($"Failed to restore scene state: {ex.Message}");
            }
        }

        public static TestOptions ParseArguments(JsonElement arguments)
        {
            List<string> testNames = null;
            if (arguments.TryGetProperty("tests", out var testsElement) && testsElement.ValueKind == JsonValueKind.Array)
            {
                testNames = testsElement.EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
            }

            string testModeStr = arguments.GetStringOrDefault("test_mode", "EditMode");
            TestMode testMode = TestMode.EditMode;
            if (Enum.TryParse<TestMode>(testModeStr, true, out var parsedMode))
            {
                testMode = parsedMode;
            }
            else if (testModeStr.Equals("Both", StringComparison.OrdinalIgnoreCase))
            {
                testMode = TestMode.EditMode | TestMode.PlayMode;
            }

            return new TestOptions
            {
                TestNames = testNames?.ToArray() ?? Array.Empty<string>(),
                Mode = testMode
            };
        }

        public static ToolsCallResult BuildResult(ITestResultAdaptor result)
        {
            var totalTests = result.PassCount + result.FailCount + result.InconclusiveCount + result.SkipCount;

            if (totalTests == 0)
            {
                return ToolsCallResult.ErrorResult("No tests found matching the provided criteria. Please check if the test names are correct (fully qualified like 'Namespace.ClassName.MethodName') and if the test mode (EditMode/PlayMode) is correct.");
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Test Run Completed. Status: {result.TestStatus}");
            sb.AppendLine($"Passed: {result.PassCount}, Failed: {result.FailCount}, Inconclusive: {result.InconclusiveCount}, Skipped: {result.SkipCount}");
            sb.AppendLine($"Duration: {result.Duration}s");

            if (result.FailCount > 0)
            {
                sb.AppendLine("\nFailed Tests:");
                AppendFailedTests(sb, result);
            }

            return ToolsCallResult.TextResult(sb.ToString(), result.FailCount > 0);
        }

        private static ToolsCallResult BuildCombinedResult(ITestResultAdaptor editResult, ITestResultAdaptor playResult)
        {
            var totalTests = editResult.PassCount + editResult.FailCount + editResult.InconclusiveCount + editResult.SkipCount +
                            playResult.PassCount + playResult.FailCount + playResult.InconclusiveCount + playResult.SkipCount;

            if (totalTests == 0)
            {
                return ToolsCallResult.ErrorResult("No tests found matching the provided criteria in either EditMode or PlayMode.");
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Test Run Completed (Both Modes).");
            sb.AppendLine($"Total Passed: {editResult.PassCount + playResult.PassCount}, Failed: {editResult.FailCount + playResult.FailCount}, Inconclusive: {editResult.InconclusiveCount + playResult.InconclusiveCount}, Skipped: {editResult.SkipCount + playResult.SkipCount}");
            sb.AppendLine($"Total Duration: {editResult.Duration + playResult.Duration}s");

            sb.AppendLine("\n--- EditMode Results ---");
            sb.AppendLine($"Status: {editResult.TestStatus}, Passed: {editResult.PassCount}, Failed: {editResult.FailCount}, Duration: {editResult.Duration}s");
            if (editResult.FailCount > 0)
            {
                AppendFailedTests(sb, editResult);
            }

            sb.AppendLine("\n--- PlayMode Results ---");
            sb.AppendLine($"Status: {playResult.TestStatus}, Passed: {playResult.PassCount}, Failed: {playResult.FailCount}, Duration: {playResult.Duration}s");
            if (playResult.FailCount > 0)
            {
                AppendFailedTests(sb, playResult);
            }

            return ToolsCallResult.TextResult(sb.ToString(), editResult.FailCount > 0 || playResult.FailCount > 0);
        }

        internal static void AppendFailedTests(System.Text.StringBuilder sb, ITestResultAdaptor result)
        {
            if (result.TestStatus == TestStatus.Failed)
            {
                if (!result.HasChildren)
                {
                    sb.AppendLine($"- {result.Name}: {result.Message}");
                    if (!string.IsNullOrEmpty(result.StackTrace))
                    {
                        sb.AppendLine($"  Stack Trace: {result.StackTrace}");
                    }
                }
                else
                {
                    foreach (var child in result.Children)
                    {
                        AppendFailedTests(sb, child);
                    }
                }
            }
        }

        public struct TestOptions
        {
            public string[] TestNames;
            public TestMode Mode;
        }

        private class TestCallbacks : ICallbacks
        {
            private readonly UniTaskCompletionSource<ITestResultAdaptor> _completionSource = new UniTaskCompletionSource<ITestResultAdaptor>();

            public UniTask<ITestResultAdaptor> ResultTask => _completionSource.Task;

            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _completionSource.TrySetResult(result);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
    }
}
