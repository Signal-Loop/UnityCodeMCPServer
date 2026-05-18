using System.IO;
using UnityCodeMcpServer.Helpers;
using UnityEditor;

namespace UnityCodeMcpServer.Editor.Installer
{
    [InitializeOnLoad]
    public static class PackageInit
    {
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
            UnityEditor.PackageManager.PackageInfo packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackageInit).Assembly);

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

            IFileSystem fileSystem = new EditorFileSystem();
            PackageInstaller packageInstaller = new(fileSystem);
            SkillsInstaller skillsInstaller = new(fileSystem);

            bool anyChanges = RunInstallers(
                () => packageInstaller.Install(packageRoot),
                () => skillsInstaller.InstallConfiguredSkills());

            UnityCodeMcpServerLogger.Debug($"[PackageInit] Install steps completed. Changes applied: {anyChanges}");
        }

        public static bool RunInstallers(System.Func<bool> installPackageFiles, System.Func<bool> installSkills)
        {
            bool packageFilesChanged = installPackageFiles != null && installPackageFiles();
            bool skillsChanged = installSkills != null && installSkills();
            return packageFilesChanged || skillsChanged;
        }
    }
}
