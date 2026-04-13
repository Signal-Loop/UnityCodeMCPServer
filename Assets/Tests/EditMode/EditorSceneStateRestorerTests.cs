using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityCodeMcpServer.Helpers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityCodeMcpServer.Tests.EditMode
{
    public class EditorSceneStateRestorerTests
    {
        [Test]
        public void SaveDirtyScenes_WhenSceneIsDirty_SavesTheScene()
        {
            var tempScenePath = "Assets/Tests/EditMode/TempTestScene.unity";
            var tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(tempScene, tempScenePath);

            try
            {
                EditorSceneManager.MarkSceneDirty(tempScene);

                Assert.IsTrue(tempScene.isDirty, "Scene should be dirty after marking it");

                EditorSceneStateRestorer.SaveDirtyScenes();

                Assert.IsFalse(tempScene.isDirty, "Scene should not be dirty after SaveDirtyScenes");
            }
            finally
            {
                if (File.Exists(tempScenePath))
                {
                    AssetDatabase.DeleteAsset(tempScenePath);
                }
            }
        }

        [Test]
        public void SaveDirtyScenes_WhenNoScenesDirty_DoesNotThrow()
        {
            var tempScenePath = "Assets/Tests/EditMode/TempCleanScene.unity";
            var tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(tempScene, tempScenePath);

            try
            {
                Assert.IsFalse(tempScene.isDirty, "Scene should not be dirty");

                Assert.DoesNotThrow(() => EditorSceneStateRestorer.SaveDirtyScenes());
            }
            finally
            {
                if (File.Exists(tempScenePath))
                {
                    AssetDatabase.DeleteAsset(tempScenePath);
                }
            }
        }

        [Test]
        public void SaveDirtyScenes_WithMultipleDirtyScenes_SavesAllScenes()
        {
            var baseScenePath = "Assets/Tests/EditMode/TempBaseScene.unity";
            var baseScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(baseScene, baseScenePath);

            var tempScenePath1 = "Assets/Tests/EditMode/TempTestScene1.unity";
            var tempScene1 = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(tempScene1, tempScenePath1);

            var tempScenePath2 = "Assets/Tests/EditMode/TempTestScene2.unity";
            var tempScene2 = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(tempScene2, tempScenePath2);

            try
            {
                EditorSceneManager.MarkSceneDirty(tempScene1);
                EditorSceneManager.MarkSceneDirty(tempScene2);

                Assert.IsTrue(tempScene1.isDirty, "Scene 1 should be dirty");
                Assert.IsTrue(tempScene2.isDirty, "Scene 2 should be dirty");

                EditorSceneStateRestorer.SaveDirtyScenes();

                Assert.IsFalse(tempScene1.isDirty, "Scene 1 should not be dirty after SaveDirtyScenes");
                Assert.IsFalse(tempScene2.isDirty, "Scene 2 should not be dirty after SaveDirtyScenes");
            }
            finally
            {
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

        [Test]
        public void RestoreSceneState_ReopensOriginalBeforeClosingTemporaryScene()
        {
            var originalScenePath = "Assets/Tests/EditMode/TempRestoreOriginalScene.unity";
            var temporaryScenePath = "Assets/Tests/EditMode/TempRestoreTemporaryScene.unity";

            var originalScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(originalScene, originalScenePath);

            var temporaryScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(temporaryScene, temporaryScenePath);

            try
            {
                InvokeRestoreSceneState(new List<string> { originalScenePath });

                Assert.IsTrue(SceneIsOpen(originalScenePath), "Original scene should be open after restoration.");
                Assert.IsFalse(SceneIsOpen(temporaryScenePath), "Temporary scene should be closed after restoration.");
            }
            finally
            {
                if (SceneIsOpen(originalScenePath))
                {
                    EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
                }
                else
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }

                if (File.Exists(originalScenePath))
                {
                    AssetDatabase.DeleteAsset(originalScenePath);
                }

                if (File.Exists(temporaryScenePath))
                {
                    AssetDatabase.DeleteAsset(temporaryScenePath);
                }
            }
        }

        [Test]
        public void ShouldDeferSceneRestore_WhenEditorIsPlaying()
        {
            var shouldDefer = EditorSceneStateRestorer.ShouldDeferSceneRestore(isPlaying: true, isPlayingOrWillChangePlaymode: true);

            Assert.IsTrue(shouldDefer);
        }

        [Test]
        public void ShouldDeferSceneRestore_WhenPlayModeStateIsChanging()
        {
            var shouldDefer = EditorSceneStateRestorer.ShouldDeferSceneRestore(isPlaying: false, isPlayingOrWillChangePlaymode: true);

            Assert.IsTrue(shouldDefer);
        }

        [Test]
        public void ShouldNotDeferSceneRestore_WhenEditorIsStableInEditMode()
        {
            var shouldDefer = EditorSceneStateRestorer.ShouldDeferSceneRestore(isPlaying: false, isPlayingOrWillChangePlaymode: false);

            Assert.IsFalse(shouldDefer);
        }

        private static void InvokeRestoreSceneState(List<string> originalScenePaths)
        {
            var method = typeof(EditorSceneStateRestorer).GetMethod("RestoreSceneState", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "RestoreSceneState method was not found.");
            method.Invoke(null, new object[] { originalScenePaths });
        }

        private static bool SceneIsOpen(string scenePath)
        {
            var sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.path == scenePath)
                {
                    return true;
                }
            }

            return false;
        }
    }
}