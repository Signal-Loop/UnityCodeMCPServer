using System.IO;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Settings;
using UnityCodeMcpServer.Settings.Editor;
using UnityEditor;

namespace UnityCodeMcpServer.Editor.Installer
{
    [InitializeOnLoad]
    public static class PackageInit
    {
        private const string SOURCE_FOLDER = "Editor/STDIO~";
        private const string TARGET_FOLDER = "Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~";

        static PackageInit()
        {
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        private static void OnAfterAssemblyReload()
        {
            UnityCodeMcpServerLogger.Debug($"[PackageInit] OnAfterAssemblyReload event");
            RunInstaller();
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
                    UnityCodeMcpServerLogger.Warn($"[PackageInit] PackageInfo not found and fallback path does not exist. Skipping installation.");
                    return;
                }
            }
            else
            {
                packageRoot = packageInfo.resolvedPath;
            }

            string sourcePath = Path.Combine(packageRoot, SOURCE_FOLDER);
            string targetPath = Path.GetFullPath(TARGET_FOLDER);

            bool skipPackageInstall = string.Equals(
                Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                System.StringComparison.OrdinalIgnoreCase);

            // Dependency Injection
            IFileSystem fileSystem = new EditorFileSystem();
            PackageInstaller installer = new PackageInstaller(fileSystem);

            UnityCodeMcpServerLogger.Debug($"[PackageInit] Installing from {sourcePath} to {targetPath}");

            RunInstallSteps(
                skipPackageInstall,
                () => installer.Install(sourcePath, targetPath),
                () => InstallSkills(fileSystem));

            UnityCodeMcpServerLogger.Debug($"[PackageInit] Package installation process completed.");
        }

        public static bool RunInstallSteps(bool skipPackageInstall, System.Func<bool> installPackageFiles, System.Func<bool> installSkills)
        {
            return PackageInstaller.InstallContent(
                () =>
                {
                    if (skipPackageInstall)
                    {
                        UnityCodeMcpServerLogger.Debug($"[PackageInit] Source and target are the same, skipping STDIO installation.");
                        return false;
                    }

                    return installPackageFiles != null && installPackageFiles();
                },
                installSkills);
        }

        private static bool InstallSkills(IFileSystem fileSystem)
        {
            string sourcePath = UnityCodeMcpServerSettingsEditor.ResolveSkillsSourcePath();
            if (string.IsNullOrEmpty(sourcePath))
            {
                UnityCodeMcpServerLogger.Warn($"[PackageInit] Could not locate the Skills source directory within the package. Skipping skills install.");
                return false;
            }

            var settings = UnityCodeMcpServerSettings.Instance;
            string targetPath = settings.GetEffectiveSkillsTargetPath();
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                UnityCodeMcpServerLogger.Warn($"[PackageInit] Skills target directory is empty. Skipping skills install.");
                return false;
            }

            var installer = new SkillsInstaller(fileSystem);
            SkillsInstallResult result = installer.Install(sourcePath, targetPath);
            return result.Success && result.AnyChanges;
        }
    }
}
