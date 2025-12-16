using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using LoopMcpServer.Interfaces;
using LoopMcpServer.Protocol;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace LoopMcpServer.Tools
{
    public class RunUnityTestsTool : IToolAsync
    {
        public string Name => "run_unity_tests";

        public string Description =>
            "Runs Unity tests using the TestRunnerApi. Can run all tests or specific tests by name. " +
            "Returns the test results including status and logs.";

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

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var callbacks = new TestCallbacks();
            api.RegisterCallbacks(callbacks);

            var filter = new Filter()
            {
                testMode = options.Mode
            };

            if (options.TestNames != null && options.TestNames.Length > 0)
            {
                filter.testNames = options.TestNames;
            }

            try
            {
                api.Execute(new ExecutionSettings(filter));
                var result = await callbacks.ResultTask;
                return BuildResult(result);
            }
            catch (Exception ex)
            {
                return new ToolsCallResult
                {
                    IsError = true,
                    Content = new List<ContentItem>
                    {
                        ContentItem.TextContent($"Error executing tests: {ex.Message}\n{ex.StackTrace}")
                    }
                };
            }
            finally
            {
                api.UnregisterCallbacks(callbacks);
                UnityEngine.Object.DestroyImmediate(api);
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
                return new ToolsCallResult
                {
                    IsError = true,
                    Content = new List<ContentItem>
                    {
                        ContentItem.TextContent("No tests found matching the provided criteria. Please check if the test names are correct (fully qualified like 'Namespace.ClassName.MethodName') and if the test mode (EditMode/PlayMode) is correct.")
                    }
                };
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

            return new ToolsCallResult
            {
                IsError = result.FailCount > 0,
                Content = new List<ContentItem>
                {
                    ContentItem.TextContent(sb.ToString())
                }
            };
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
