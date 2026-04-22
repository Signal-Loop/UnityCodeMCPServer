using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityCodeMcpServer.Helpers
{
    public static class EditorSceneStateRestorer
    {
        private static readonly Queue<List<string>> PendingSceneRestores = new();
        private static bool _scene_restore_hook_registered;

        public static void SaveDirtyScenes()
        {
            int sceneCount = SceneManager.sceneCount;
            List<Scene> dirtyScenes = new();

            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                {
                    dirtyScenes.Add(scene);
                }
            }

            if (dirtyScenes.Count > 0)
            {
                EditorSceneManager.SaveScenes(dirtyScenes.ToArray());
            }
        }

        public static List<string> CaptureCurrentSceneState()
        {
            List<string> sceneState = new();
            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!string.IsNullOrEmpty(scene.path))
                {
                    sceneState.Add(scene.path);
                }
            }

            return sceneState;
        }

        public static void RestoreSceneStateWhenSafe(List<string> originalScenePaths)
        {
            if (!ShouldDeferSceneRestore(EditorApplication.isPlaying, EditorApplication.isPlayingOrWillChangePlaymode))
            {
                RestoreSceneState(originalScenePaths);
                return;
            }

            PendingSceneRestores.Enqueue(new List<string>(originalScenePaths));
            RegisterSceneRestoreHook();
        }

        public static bool ShouldDeferSceneRestore(bool isPlaying, bool isPlayingOrWillChangePlaymode)
        {
            return isPlaying || isPlayingOrWillChangePlaymode;
        }

        private static void RestoreSceneState(List<string> originalScenePaths)
        {
            try
            {
                Dictionary<string, Scene> currentScenes = GetOpenScenesByPath();

                foreach (string scenePath in originalScenePaths)
                {
                    if (!currentScenes.ContainsKey(scenePath))
                    {
                        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                    }
                }

                currentScenes = GetOpenScenesByPath();

                foreach (KeyValuePair<string, Scene> kvp in currentScenes)
                {
                    if (!originalScenePaths.Contains(kvp.Key))
                    {
                        EditorSceneManager.CloseScene(kvp.Value, true);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Warn($"Failed to restore scene state: {ex.Message}");
            }
        }

        private static void RegisterSceneRestoreHook()
        {
            if (_scene_restore_hook_registered)
            {
                return;
            }

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            _scene_restore_hook_registered = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            FlushPendingSceneRestores();
        }

        private static void FlushPendingSceneRestores()
        {
            if (ShouldDeferSceneRestore(EditorApplication.isPlaying, EditorApplication.isPlayingOrWillChangePlaymode))
            {
                return;
            }

            while (PendingSceneRestores.Count > 0)
            {
                RestoreSceneState(PendingSceneRestores.Dequeue());
            }

            if (!_scene_restore_hook_registered)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            _scene_restore_hook_registered = false;
        }

        private static Dictionary<string, Scene> GetOpenScenesByPath()
        {
            Dictionary<string, Scene> scenes = new();
            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!string.IsNullOrEmpty(scene.path))
                {
                    scenes[scene.path] = scene;
                }
            }

            return scenes;
        }
    }
}
