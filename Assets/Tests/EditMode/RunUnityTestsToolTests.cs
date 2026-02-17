using System.Text.Json;
using UnityCodeMcpServer.Tools;
using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

namespace UnityCodeMcpServer.Tests.EditMode
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

        [Test]
        public void ShouldBlockEditMode_WhenPlaying_AndEditModeRequested()
        {
            var shouldBlock = RunUnityTestsTool.ShouldBlockEditMode(TestMode.EditMode, true);
            Assert.IsTrue(shouldBlock);
        }

        [Test]
        public void ShouldBlockEditMode_WhenPlaying_AndBothRequested()
        {
            var shouldBlock = RunUnityTestsTool.ShouldBlockEditMode(TestMode.EditMode | TestMode.PlayMode, true);
            Assert.IsTrue(shouldBlock);
        }

        [Test]
        public void ShouldNotBlockEditMode_WhenPlaying_AndPlayModeOnly()
        {
            var shouldBlock = RunUnityTestsTool.ShouldBlockEditMode(TestMode.PlayMode, true);
            Assert.IsFalse(shouldBlock);
        }

        [Test]
        public void BuildEditModeBlockedResult_ReturnsErrorMessage()
        {
            var result = RunUnityTestsTool.BuildEditModeBlockedResult();

            Assert.IsTrue(result.IsError);
            Assert.That(result.Content[0].Text, Does.Contain("Cannot run EditMode tests while the editor is in Play Mode"));
        }

        [Test]
        public void SaveDirtyScenes_WhenSceneIsDirty_SavesTheScene()
        {
            // Create a temporary scene to test with (single mode to avoid issues with untitled scenes)
            var tempScenePath = "Assets/Tests/EditMode/TempTestScene.unity";
            var tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(tempScene, tempScenePath);

            try
            {
                // Mark the scene as dirty
                EditorSceneManager.MarkSceneDirty(tempScene);

                // Verify scene is dirty
                Assert.IsTrue(tempScene.isDirty, "Scene should be dirty after marking it");

                // Call SaveDirtyScenes
                RunUnityTestsTool.SaveDirtyScenes();

                // Verify scene is no longer dirty
                Assert.IsFalse(tempScene.isDirty, "Scene should not be dirty after SaveDirtyScenes");
            }
            finally
            {
                // Cleanup: delete the temp scene
                if (File.Exists(tempScenePath))
                {
                    AssetDatabase.DeleteAsset(tempScenePath);
                }
            }
        }

        [Test]
        public void SaveDirtyScenes_WhenNoScenesDirty_DoesNotThrow()
        {
            // Create a clean scene
            var tempScenePath = "Assets/Tests/EditMode/TempCleanScene.unity";
            var tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(tempScene, tempScenePath);

            try
            {
                // Verify scene is not dirty
                Assert.IsFalse(tempScene.isDirty, "Scene should not be dirty");

                // This should not throw any exceptions
                Assert.DoesNotThrow(() => RunUnityTestsTool.SaveDirtyScenes());
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempScenePath))
                {
                    AssetDatabase.DeleteAsset(tempScenePath);
                }
            }
        }

        [Test]
        public void SaveDirtyScenes_WithMultipleDirtyScenes_SavesAllScenes()
        {
            // Create a base scene first
            var baseScenePath = "Assets/Tests/EditMode/TempBaseScene.unity";
            var baseScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(baseScene, baseScenePath);

            // Create two additional scenes additively
            var tempScenePath1 = "Assets/Tests/EditMode/TempTestScene1.unity";
            var tempScene1 = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(tempScene1, tempScenePath1);

            var tempScenePath2 = "Assets/Tests/EditMode/TempTestScene2.unity";
            var tempScene2 = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(tempScene2, tempScenePath2);

            try
            {
                // Mark both scenes as dirty
                EditorSceneManager.MarkSceneDirty(tempScene1);
                EditorSceneManager.MarkSceneDirty(tempScene2);

                // Verify both scenes are dirty
                Assert.IsTrue(tempScene1.isDirty, "Scene 1 should be dirty");
                Assert.IsTrue(tempScene2.isDirty, "Scene 2 should be dirty");

                // Call SaveDirtyScenes
                RunUnityTestsTool.SaveDirtyScenes();

                // Verify both scenes are no longer dirty
                Assert.IsFalse(tempScene1.isDirty, "Scene 1 should not be dirty after SaveDirtyScenes");
                Assert.IsFalse(tempScene2.isDirty, "Scene 2 should not be dirty after SaveDirtyScenes");
            }
            finally
            {
                // Cleanup: close and delete the temp scenes
                EditorSceneManager.CloseScene(tempScene1, true);
                EditorSceneManager.CloseScene(tempScene2, true);
                if (File.Exists(tempScenePath1))
                {
                    AssetDatabase.DeleteAsset(tempScenePath1);
                }
                if (File.Exists(tempScenePath2))
                {
                    AssetDatabase.DeleteAsset(tempScenePath2);
                }
                if (File.Exists(baseScenePath))
                {
                    AssetDatabase.DeleteAsset(baseScenePath);
                }
            }
        }
    }
}
