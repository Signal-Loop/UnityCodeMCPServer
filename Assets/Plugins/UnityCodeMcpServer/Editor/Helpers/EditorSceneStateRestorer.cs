using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityCodeMcpServer.Helpers
{
    public static class EditorSceneStateRestorer
    {
        private static readonly Queue<List<string>> PendingSceneRestores = new Queue<List<string>>();
        private static bool _scene_restore_hook_registered;

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
                EditorSceneManager.SaveScenes(dirtyScenes.ToArray());
            }
        }

        public static List<string> CaptureCurrentSceneState()
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
                var currentScenes = GetOpenScenesByPath();

                foreach (var scenePath in originalScenePaths)
                {
                    if (!currentScenes.ContainsKey(scenePath))
                    {
                        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                    }
                }

                currentScenes = GetOpenScenesByPath();

                foreach (var kvp in currentScenes)
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
            var scenes = new Dictionary<string, Scene>();
            var sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!string.IsNullOrEmpty(scene.path))
                {
                    scenes[scene.path] = scene;
                }
            }

            return scenes;
        }
    }
}
