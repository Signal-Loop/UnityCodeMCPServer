using UnityEditor;
using System.IO;
using UnityEngine;

namespace LoopMcpServer.Editor.Installer
{
    [InitializeOnLoad]
    public static class PackageInit
    {
        private const string SOURCE_FOLDER = "Editor/STDIO~";
        private const string TARGET_FOLDER = "Assets/Plugins/Loop4UnityMcpServer/Editor/STDIO~";

        static PackageInit()
        {
            // Delay call to ensure PackageInfo is ready
            EditorApplication.delayCall += RunInstaller;
        }

        private static void RunInstaller()
        {
            Debug.Log("[PackageInit] Running package installer...");
            // Reliability: Find the package path dynamically.
            // This works even if the package is in PackageCache, Embedded, or Local.
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackageInit).Assembly);

            if (packageInfo == null)
            {
                Debug.LogWarning("[PackageInit] PackageInfo not found. Skipping installation.");
                // Fallback or just return if not found (e.g. during script reloads)
                return;
            }

            string packageRoot = packageInfo.resolvedPath;
            string sourcePath = Path.Combine(packageRoot, SOURCE_FOLDER);
            string targetPath = Path.GetFullPath(TARGET_FOLDER);

            // Dependency Injection
            IFileSystem fileSystem = new EditorFileSystem();
            PackageInstaller installer = new PackageInstaller(fileSystem);

            Debug.Log($"[PackageInit] Installing from {sourcePath} to {targetPath}");
            // Execute
            bool installed = installer.Install(sourcePath, targetPath);
            Debug.Log("[PackageInit] Package installation process completed.");

            // Only refresh if we actually changed something
            if (installed)
            {
                AssetDatabase.Refresh();
            }
        }
    }
}