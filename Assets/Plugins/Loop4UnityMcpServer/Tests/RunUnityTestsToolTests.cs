using System;
using System.Collections;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using LoopMcpServer.Tools;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEditor.TestTools.TestRunner.Api;
using System.Collections.Generic;
using System.Linq;

namespace LoopMcpServer.Tests
{
    public class RunUnityTestsToolTests
    {
        [Test]
        public void Tool_Instantiation_Success()
        {
            var tool = new RunUnityTestsTool();
            Assert.IsNotNull(tool);
            Assert.AreEqual("run_unity_tests", tool.Name);
            Assert.IsNotEmpty(tool.Description);
        }

        [Test]
        public void InputSchema_IsValidJson()
        {
            var tool = new RunUnityTestsTool();
            var schema = tool.InputSchema;
            Assert.AreEqual(JsonValueKind.Object, schema.ValueKind);
        }

        [Test]
        public void ParseArguments_Defaults_EditMode()
        {
            var json = "{}";
            var args = JsonDocument.Parse(json).RootElement;
            var options = RunUnityTestsTool.ParseArguments(args);

            Assert.AreEqual(TestMode.EditMode, options.Mode);
            Assert.IsEmpty(options.TestNames);
        }

        [Test]
        public void ParseArguments_ValidTestMode()
        {
            var json = @"{ ""test_mode"": ""PlayMode"" }";
            var args = JsonDocument.Parse(json).RootElement;
            var options = RunUnityTestsTool.ParseArguments(args);

            Assert.AreEqual(TestMode.PlayMode, options.Mode);
        }

        [Test]
        public void ParseArguments_BothMode()
        {
            var json = @"{ ""test_mode"": ""Both"" }";
            var args = JsonDocument.Parse(json).RootElement;
            var options = RunUnityTestsTool.ParseArguments(args);

            Assert.AreEqual(TestMode.EditMode | TestMode.PlayMode, options.Mode);
        }

        [Test]
        public void ParseArguments_InvalidMode_DefaultsToEditMode()
        {
            var json = @"{ ""test_mode"": ""Invalid"" }";
            var args = JsonDocument.Parse(json).RootElement;
            var options = RunUnityTestsTool.ParseArguments(args);

            Assert.AreEqual(TestMode.EditMode, options.Mode);
        }

        [Test]
        public void ParseArguments_TestsList()
        {
            var json = @"{ ""tests"": [""Test1"", ""Test2""] }";
            var args = JsonDocument.Parse(json).RootElement;
            var options = RunUnityTestsTool.ParseArguments(args);

            Assert.AreEqual(2, options.TestNames.Length);
            Assert.Contains("Test1", options.TestNames);
            Assert.Contains("Test2", options.TestNames);
        }

        [Test]
        public void ParseArguments_EmptyTestsList()
        {
            var json = @"{ ""tests"": [] }";
            var args = JsonDocument.Parse(json).RootElement;
            var options = RunUnityTestsTool.ParseArguments(args);

            Assert.IsEmpty(options.TestNames);
        }

        [Test]
        public void BuildResult_Passed()
        {
            var mockResult = new MockTestResultAdaptor
            {
                TestStatus = TestStatus.Passed,
                PassCount = 5,
                FailCount = 0,
                Duration = 2.5
            };

            var result = RunUnityTestsTool.BuildResult(mockResult);

            Assert.IsFalse(result.IsError);
            Assert.IsNotEmpty(result.Content);
            var text = result.Content[0].Text;
            Assert.That(text, Does.Contain("Status: Passed"));
            Assert.That(text, Does.Contain("Passed: 5"));
            Assert.That(text, Does.Contain("Duration:"));
        }

        [Test]
        public void BuildResult_Failed()
        {
            var failedChild = new MockTestResultAdaptor
            {
                TestStatus = TestStatus.Failed,
                Name = "FailedTest",
                Message = "Assertion Failed",
                StackTrace = "at SomeClass.Method()",
                HasChildren = false
            };

            var mockResult = new MockTestResultAdaptor
            {
                TestStatus = TestStatus.Failed,
                PassCount = 0,
                FailCount = 1,
                Duration = 1.0,
                HasChildren = true,
                Children = new List<ITestResultAdaptor> { failedChild }
            };

            var result = RunUnityTestsTool.BuildResult(mockResult);

            Assert.IsTrue(result.IsError);
            var text = result.Content[0].Text;
            Assert.That(text, Does.Contain("Status: Failed"));
            Assert.That(text, Does.Contain("Failed: 1"));
            Assert.That(text, Does.Contain("- FailedTest: Assertion Failed"));
            Assert.That(text, Does.Contain("Stack Trace: at SomeClass.Method()"));
        }

        [Test]
        public void BuildResult_NoTestsRun()
        {
            var mockResult = new MockTestResultAdaptor
            {
                TestStatus = TestStatus.Passed,
                PassCount = 0,
                FailCount = 0,
                InconclusiveCount = 0,
                SkipCount = 0,
                Duration = 0.1
            };

            var result = RunUnityTestsTool.BuildResult(mockResult);

            Assert.IsTrue(result.IsError, "Should be an error if no tests were found matching the criteria");
            Assert.That(result.Content[0].Text, Does.Contain("No tests found"));
        }
    }
}
