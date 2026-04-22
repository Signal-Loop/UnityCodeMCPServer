using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using UnityCodeMcpServer.McpTools;
using UnityCodeMcpServer.Protocol;
using UnityEditor.TestTools.TestRunner.Api;

namespace UnityCodeMcpServer.Tests.EditMode
{
    public class RunUnityTestsToolTests
    {
        [Test]
        public void Tool_Instantiation_Success()
        {
            RunUnityTestsTool tool = new();
            Assert.IsNotNull(tool);
            Assert.AreEqual("run_unity_tests", tool.Name);
            Assert.IsNotEmpty(tool.Description);
        }

        [Test]
        public void InputSchema_IsValidJson()
        {
            RunUnityTestsTool tool = new();
            JsonElement schema = tool.InputSchema;
            Assert.AreEqual(JsonValueKind.Object, schema.ValueKind);
        }

        [Test]
        public void ParseArguments_Defaults_EditMode()
        {
            string json = "{}";
            JsonElement args = JsonDocument.Parse(json).RootElement;
            RunUnityTestsTool.TestOptions options = RunUnityTestsTool.ParseArguments(args);

            Assert.AreEqual(TestMode.EditMode, options.Mode);
            Assert.IsEmpty(options.TestNames);
        }

        [Test]
        public void ParseArguments_ValidTestMode()
        {
            string json = @"{ ""test_mode"": ""PlayMode"" }";
            JsonElement args = JsonDocument.Parse(json).RootElement;
            RunUnityTestsTool.TestOptions options = RunUnityTestsTool.ParseArguments(args);

            Assert.AreEqual(TestMode.PlayMode, options.Mode);
        }

        [Test]
        public void ParseArguments_BothMode()
        {
            string json = @"{ ""test_mode"": ""Both"" }";
            JsonElement args = JsonDocument.Parse(json).RootElement;
            RunUnityTestsTool.TestOptions options = RunUnityTestsTool.ParseArguments(args);

            Assert.AreEqual(TestMode.EditMode | TestMode.PlayMode, options.Mode);
        }

        [Test]
        public void ParseArguments_InvalidMode_DefaultsToEditMode()
        {
            string json = @"{ ""test_mode"": ""Invalid"" }";
            JsonElement args = JsonDocument.Parse(json).RootElement;
            RunUnityTestsTool.TestOptions options = RunUnityTestsTool.ParseArguments(args);

            Assert.AreEqual(TestMode.EditMode, options.Mode);
        }

        [Test]
        public void ParseArguments_TestsList()
        {
            string json = @"{ ""tests"": [""Test1"", ""Test2""] }";
            JsonElement args = JsonDocument.Parse(json).RootElement;
            RunUnityTestsTool.TestOptions options = RunUnityTestsTool.ParseArguments(args);

            Assert.AreEqual(2, options.TestNames.Length);
            Assert.Contains("Test1", options.TestNames);
            Assert.Contains("Test2", options.TestNames);
        }

        [Test]
        public void ParseArguments_EmptyTestsList()
        {
            string json = @"{ ""tests"": [] }";
            JsonElement args = JsonDocument.Parse(json).RootElement;
            RunUnityTestsTool.TestOptions options = RunUnityTestsTool.ParseArguments(args);

            Assert.IsEmpty(options.TestNames);
        }

        [Test]
        public void BuildResult_Passed()
        {
            MockTestResultAdaptor mockResult = new()
            {
                TestStatus = TestStatus.Passed,
                PassCount = 5,
                FailCount = 0,
                Duration = 2.5
            };

            ToolsCallResult result = RunUnityTestsTool.BuildResult(mockResult);

            Assert.IsFalse(result.IsError);
            Assert.IsNotEmpty(result.Content);
            string text = result.Content[0].Text;
            Assert.That(text, Does.Contain("Status: Passed"));
            Assert.That(text, Does.Contain("Passed: 5"));
            Assert.That(text, Does.Contain("Duration:"));
        }

        [Test]
        public void BuildResult_Failed()
        {
            MockTestResultAdaptor failedChild = new()
            {
                TestStatus = TestStatus.Failed,
                Name = "FailedTest",
                Message = "Assertion Failed",
                StackTrace = "at SomeClass.Method()",
                HasChildren = false
            };

            MockTestResultAdaptor mockResult = new()
            {
                TestStatus = TestStatus.Failed,
                PassCount = 0,
                FailCount = 1,
                Duration = 1.0,
                HasChildren = true,
                Children = new List<ITestResultAdaptor> { failedChild }
            };

            ToolsCallResult result = RunUnityTestsTool.BuildResult(mockResult);

            Assert.IsTrue(result.IsError);
            string text = result.Content[0].Text;
            Assert.That(text, Does.Contain("Status: Failed"));
            Assert.That(text, Does.Contain("Failed: 1"));
            Assert.That(text, Does.Contain("- FailedTest: Assertion Failed"));
            Assert.That(text, Does.Contain("Stack Trace: at SomeClass.Method()"));
        }

        [Test]
        public void BuildResult_NoTestsRun()
        {
            MockTestResultAdaptor mockResult = new()
            {
                TestStatus = TestStatus.Passed,
                PassCount = 0,
                FailCount = 0,
                InconclusiveCount = 0,
                SkipCount = 0,
                Duration = 0.1
            };

            ToolsCallResult result = RunUnityTestsTool.BuildResult(mockResult);

            Assert.IsTrue(result.IsError, "Should be an error if no tests were found matching the criteria");
            Assert.That(result.Content[0].Text, Does.Contain("No tests found"));
        }

        [Test]
        public void ShouldBlockEditMode_WhenPlaying_AndEditModeRequested()
        {
            bool shouldBlock = RunUnityTestsTool.ShouldBlockEditMode(TestMode.EditMode, true);
            Assert.IsTrue(shouldBlock);
        }

        [Test]
        public void ShouldBlockEditMode_WhenPlaying_AndBothRequested()
        {
            bool shouldBlock = RunUnityTestsTool.ShouldBlockEditMode(TestMode.EditMode | TestMode.PlayMode, true);
            Assert.IsTrue(shouldBlock);
        }

        [Test]
        public void ShouldNotBlockEditMode_WhenPlaying_AndPlayModeOnly()
        {
            bool shouldBlock = RunUnityTestsTool.ShouldBlockEditMode(TestMode.PlayMode, true);
            Assert.IsFalse(shouldBlock);
        }

        [Test]
        public void BuildEditModeBlockedResult_ReturnsErrorMessage()
        {
            ToolsCallResult result = RunUnityTestsTool.BuildEditModeBlockedResult();

            Assert.IsTrue(result.IsError);
            Assert.That(result.Content[0].Text, Does.Contain("Cannot run EditMode tests while the editor is in Play Mode"));
        }

        [Test]
        public void ShouldBlockForCompilationIssues_WhenEditorIsCompiling()
        {
            bool shouldBlock = RunUnityTestsTool.ShouldBlockForCompilationIssues(isCompiling: true, hasCompileErrors: false);

            Assert.IsTrue(shouldBlock);
        }

        [Test]
        public void ShouldBlockForCompilationIssues_WhenCompilerErrorsExist()
        {
            bool shouldBlock = RunUnityTestsTool.ShouldBlockForCompilationIssues(isCompiling: false, hasCompileErrors: true);

            Assert.IsTrue(shouldBlock);
        }

        [Test]
        public void ShouldNotBlockForCompilationIssues_WhenEditorIsReady()
        {
            bool shouldBlock = RunUnityTestsTool.ShouldBlockForCompilationIssues(isCompiling: false, hasCompileErrors: false);

            Assert.IsFalse(shouldBlock);
        }

        [Test]
        public void BuildCompilationBlockedResult_ReturnsCompilerErrorMessage()
        {
            ToolsCallResult result = RunUnityTestsTool.BuildCompilationBlockedResult(isCompiling: false, hasCompileErrors: true);

            Assert.IsTrue(result.IsError);
            Assert.That(result.Content[0].Text, Does.Contain("Cannot run Unity tests while the project has compiler errors"));
        }

    }
}
