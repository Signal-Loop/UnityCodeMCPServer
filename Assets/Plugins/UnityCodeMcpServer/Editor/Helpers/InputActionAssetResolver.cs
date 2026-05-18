using System;
using System.Collections.Generic;
using System.Linq;
using UnityCodeMcpServer.Settings;
using UnityEditor;
using UnityEngine.InputSystem;

namespace UnityCodeMcpServer.Helpers
{
    internal static class InputActionAssetResolver
    {
        public static InputActionAsset LoadInputActionAsset(out string warningMessage)
        {
            UnityCodeMcpServerSettings settings = UnityCodeMcpServerSettings.Instance;
            string configuredPath = settings == null ? string.Empty : settings.InputActionsAssetPath;

            string configuredExistingPath = string.IsNullOrWhiteSpace(configuredPath)
                ? string.Empty
                : GetValidAssetPath(configuredPath);

            IReadOnlyList<string> projectAssetPaths = FindInputActionAssetPaths(new[] { "Assets" });
            IReadOnlyList<string> allAssetPaths = FindInputActionAssetPaths(Array.Empty<string>());

            string resolvedPath = ResolveInputActionAssetPath(
                configuredExistingPath,
                projectAssetPaths,
                allAssetPaths,
                out _,
                out warningMessage);

            return string.IsNullOrWhiteSpace(resolvedPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<InputActionAsset>(resolvedPath);
        }

        private static IReadOnlyList<string> FindInputActionAssetPaths(string[] searchFolders)
        {
            string[] guids = searchFolders == null || searchFolders.Length == 0
                ? AssetDatabase.FindAssets("t:InputActionAsset")
                : AssetDatabase.FindAssets("t:InputActionAsset", searchFolders);

            return guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string GetValidAssetPath(string path)
        {
            string normalizedPath = path.Replace("\\", "/");
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(normalizedPath);
            return asset == null ? string.Empty : normalizedPath;
        }

        private static string ResolveInputActionAssetPath(
            string configuredPath,
            IReadOnlyList<string> projectAssetPaths,
            IReadOnlyList<string> allAssetPaths,
            out bool usedFallback,
            out string warningMessage)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                usedFallback = false;
                warningMessage = null;
                return configuredPath;
            }

            if (projectAssetPaths != null && projectAssetPaths.Count > 0)
            {
                usedFallback = true;
                warningMessage = $"Input actions are not set in settings asset. Using project InputActionAsset: {projectAssetPaths[0]}";
                return projectAssetPaths[0];
            }

            if (allAssetPaths != null && allAssetPaths.Count > 0)
            {
                usedFallback = true;
                warningMessage = $"Input actions are not set in settings asset. Using fallback InputActionAsset: {allAssetPaths[0]}";
                return allAssetPaths[0];
            }

            usedFallback = false;
            warningMessage = null;
            return null;
        }
    }
}