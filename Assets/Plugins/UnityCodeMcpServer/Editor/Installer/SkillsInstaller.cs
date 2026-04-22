using System;
using System.IO;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Settings;
using UnityCodeMcpServer.Settings.Editor;

namespace UnityCodeMcpServer.Editor.Installer
{
    /// <summary>
    /// Result of a skills installation operation.
    /// </summary>
    public class SkillsInstallResult
    {
        public bool Success { get; set; }
        public int SkillFoldersUpdated { get; set; }
        public int FilesUpdated { get; set; }
        public string ErrorMessage { get; set; }

        public bool AnyChanges => FilesUpdated > 0;

        public static SkillsInstallResult Failure(string errorMessage) =>
            new()
            { Success = false, ErrorMessage = errorMessage };

        public override string ToString()
        {
            if (!Success)
                return $"Failed: {ErrorMessage}";
            if (!AnyChanges)
                return "All skills are already up to date.";
            return $"Installed {SkillFoldersUpdated} skill folder(s), updated {FilesUpdated} file(s).";
        }
    }

    /// <summary>
    /// Installs AI agent skill files by copying every skill subfolder
    /// from a source directory into a target directory recursively.
    /// Files whose content hash matches are skipped.
    /// </summary>
    public class SkillsInstaller
    {
        private readonly IFileSystem _fileSystem;

        public SkillsInstaller(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public bool InstallConfiguredSkills()
        {
            string sourcePath = UnityCodeMcpServerSettingsEditor.ResolveSkillsSourcePath();
            if (string.IsNullOrEmpty(sourcePath))
            {
                UnityCodeMcpServerLogger.Warn("[SkillsInstaller] Could not locate the Skills source directory within the package. Skipping skills install.");
                return false;
            }

            UnityCodeMcpServerSettings settings = UnityCodeMcpServerSettings.Instance;
            string targetPath = settings.GetEffectiveSkillsTargetPath();
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                UnityCodeMcpServerLogger.Warn("[SkillsInstaller] Skills target directory is empty. Skipping skills install.");
                return false;
            }

            SkillsInstallResult result = Install(sourcePath, targetPath);
            return result.Success && result.AnyChanges;
        }

        /// <summary>
        /// Copy all skill subfolders from <paramref name="sourcePath"/> into <paramref name="targetPath"/>.
        /// </summary>
        public SkillsInstallResult Install(string sourcePath, string targetPath)
        {
            if (!_fileSystem.DirectoryExists(sourcePath))
            {
                UnityCodeMcpServerLogger.Error($"[SkillsInstaller] Skills source directory not found: {sourcePath}");
                return SkillsInstallResult.Failure($"Source directory not found: {sourcePath}");
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return SkillsInstallResult.Failure("Target path must not be empty.");
            }

            try
            {
                SkillsInstallResult result = new() { Success = true };

                string[] skillFolders = _fileSystem.GetDirectories(sourcePath);

                foreach (string skillFolder in skillFolders)
                {
                    string folderName = GetDirectoryName(skillFolder);
                    string targetSkillFolder = NormalizePath(Path.Combine(targetPath, folderName));

                    int filesCopied = CopyDirectoryRecursive(skillFolder, targetSkillFolder);

                    if (filesCopied > 0)
                    {
                        result.SkillFoldersUpdated++;
                        result.FilesUpdated += filesCopied;
                        UnityCodeMcpServerLogger.Info(
                            $"[SkillsInstaller] Installed skill '{folderName}'" +
                            $" ({filesCopied} file(s) updated) to: {targetSkillFolder}");
                    }
                    else
                    {
                        UnityCodeMcpServerLogger.Trace(
                            $"[SkillsInstaller] Skill '{folderName}' is already up to date.");
                    }
                }

                if (result.AnyChanges)
                    UnityCodeMcpServerLogger.Debug($"[SkillsInstaller] Skills install complete — {result}");
                else
                    UnityCodeMcpServerLogger.Trace($"[SkillsInstaller] Skills install complete — {result}");
                return result;
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Error($"[SkillsInstaller] Failed to install skills. Error: {ex.Message}");
                return SkillsInstallResult.Failure(ex.Message);
            }
        }

        public bool RelocateInstalledSkills(string sourcePath, string currentTargetPath, string newTargetPath)
        {
            SkillsInstallResult installResult = Install(sourcePath, newTargetPath);
            if (!installResult.Success)
            {
                return false;
            }

            string normalizedCurrentTargetPath = NormalizePath(currentTargetPath ?? string.Empty).TrimEnd('/');
            string normalizedNewTargetPath = NormalizePath(newTargetPath ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalizedCurrentTargetPath) ||
                string.Equals(normalizedCurrentTargetPath, normalizedNewTargetPath, StringComparison.OrdinalIgnoreCase))
            {
                return installResult.AnyChanges;
            }

            bool removedAnyExistingSkills = false;
            foreach (string skillFolder in _fileSystem.GetDirectories(sourcePath))
            {
                string folderName = GetDirectoryName(skillFolder);
                string oldTargetSkillFolder = NormalizePath(Path.Combine(normalizedCurrentTargetPath, folderName));
                if (!_fileSystem.DirectoryExists(oldTargetSkillFolder))
                {
                    continue;
                }

                removedAnyExistingSkills |= DeleteInstalledFilesRecursive(skillFolder, oldTargetSkillFolder);
            }

            return installResult.AnyChanges || removedAnyExistingSkills;
        }

        // ── private helpers ───────────────────────────────────────────────────

        private bool DeleteInstalledFilesRecursive(string sourceDir, string oldTargetDir)
        {
            if (!_fileSystem.DirectoryExists(oldTargetDir))
            {
                return false;
            }

            bool removedAnyFiles = false;

            foreach (string sourceFilePath in _fileSystem.GetFiles(sourceDir))
            {
                string fileName = _fileSystem.GetFileName(sourceFilePath);
                if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string oldTargetFilePath = NormalizePath(Path.Combine(oldTargetDir, fileName));
                if (!_fileSystem.FileExists(oldTargetFilePath))
                {
                    continue;
                }

                _fileSystem.DeleteFile(oldTargetFilePath);
                removedAnyFiles = true;
                UnityCodeMcpServerLogger.Trace($"[SkillsInstaller] Removed old skill file: {oldTargetFilePath}");
            }

            foreach (string sourceSubDir in _fileSystem.GetDirectories(sourceDir))
            {
                string subDirName = GetDirectoryName(sourceSubDir);
                string oldTargetSubDir = NormalizePath(Path.Combine(oldTargetDir, subDirName));
                removedAnyFiles |= DeleteInstalledFilesRecursive(sourceSubDir, oldTargetSubDir);
            }

            if (_fileSystem.DirectoryExists(oldTargetDir) &&
                _fileSystem.GetFiles(oldTargetDir).Length == 0 &&
                _fileSystem.GetDirectories(oldTargetDir).Length == 0)
            {
                _fileSystem.DeleteDirectory(oldTargetDir, recursive: false);
                UnityCodeMcpServerLogger.Trace($"[SkillsInstaller] Removed empty old skill directory: {oldTargetDir}");
                return true;
            }

            return removedAnyFiles;
        }

        private int CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            int filesCopied = 0;

            if (!_fileSystem.DirectoryExists(targetDir))
            {
                _fileSystem.CreateDirectory(targetDir);
                UnityCodeMcpServerLogger.Trace($"[SkillsInstaller] Created directory: {targetDir}");
            }

            foreach (string filePath in _fileSystem.GetFiles(sourceDir))
            {
                string fileName = _fileSystem.GetFileName(filePath);
                if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destPath = NormalizePath(Path.Combine(targetDir, fileName));

                if (ShouldCopyFile(filePath, destPath))
                {
                    _fileSystem.CopyFile(filePath, destPath, overwrite: true);
                    UnityCodeMcpServerLogger.Trace($"[SkillsInstaller] Copied: {destPath}");
                    filesCopied++;
                }
            }

            foreach (string subDir in _fileSystem.GetDirectories(sourceDir))
            {
                string subDirName = GetDirectoryName(subDir);
                string targetSubDir = NormalizePath(Path.Combine(targetDir, subDirName));
                filesCopied += CopyDirectoryRecursive(subDir, targetSubDir);
            }

            return filesCopied;
        }

        private bool ShouldCopyFile(string sourcePath, string destPath)
        {
            if (!_fileSystem.FileExists(destPath))
                return true;

            try
            {
                return _fileSystem.ComputeFileHash(sourcePath) != _fileSystem.ComputeFileHash(destPath);
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Warn(
                    $"[SkillsInstaller] Cannot compute hash, will copy file. Error: {ex.Message}");
                return true;
            }
        }

        /// <summary>Return the last path segment (directory name) from a path, trimming any trailing separators.</summary>
        private static string GetDirectoryName(string path)
        {
            return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        private static string NormalizePath(string path) => path.Replace("\\", "/");
    }
}
