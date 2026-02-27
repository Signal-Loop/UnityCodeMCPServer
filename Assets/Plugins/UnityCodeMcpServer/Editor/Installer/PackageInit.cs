using UnityEditor;
using System.IO;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Settings;

namespace UnityCodeMcpServer.Editor.Installer
{
    [InitializeOnLoad]
    public static class PackageInit
    {
        private const string SOURCE_FOLDER = "Editor/STDIO~";
        private const string TARGET_FOLDER = "Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~";

        static PackageInit()
        {
            // Delay call to ensure PackageInfo is ready
            EditorApplication.delayCall += RunInstaller;
        }

        private static void RunInstaller()
        {
            // Reliability: Find the package path dynamically.
            // This works even if the package is in PackageCache, Embedded, or Local.
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackageInit).Assembly);

            string packageRoot;
            if (packageInfo == null)
            {
                // Fallback for when it's not a formal package (e.g. just raw files in Assets)
                packageRoot = Path.GetFullPath("Assets/Plugins/UnityCodeMcpServer");
                if (!Directory.Exists(packageRoot))
                {
                    LoopLogger.Warn("[PackageInit] PackageInfo not found and fallback path does not exist. Skipping installation.");
                    return;
                }
            }
            else
            {
                packageRoot = packageInfo.resolvedPath;
            }

            string sourcePath = Path.Combine(packageRoot, SOURCE_FOLDER);
            string targetPath = Path.GetFullPath(TARGET_FOLDER);

            // If source and target are the same, skip copy to avoid errors and unnecessary work
            if (string.Equals(Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                              Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                              System.StringComparison.OrdinalIgnoreCase))
            {
                LoopLogger.Debug($"{Protocol.McpProtocol.LogPrefix} Source and target are the same, skipping installation.");
                return;
            }

            // Dependency Injection
            IFileSystem fileSystem = new EditorFileSystem();
            PackageInstaller installer = new PackageInstaller(fileSystem);

            LoopLogger.Debug($"{Protocol.McpProtocol.LogPrefix} Installing from {sourcePath} to {targetPath}");

            // Execute
            bool installed = installer.Install(sourcePath, targetPath);

            LoopLogger.Debug($"{Protocol.McpProtocol.LogPrefix} Package installation process completed.");

            // Only refresh if we actually changed something
            if (installed)
            {

                AssetDatabase.Refresh();
            }
        }
    }
}