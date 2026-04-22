using System;
using System.IO;
using UnityCodeMcpServer.Helpers;

namespace UnityCodeMcpServer.Editor.Installer
{
    public class PackageInstaller
    {
        private const string SourceFolder = "Editor/STDIO~";
        private const string TargetFolder = "Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~";

        private readonly IFileSystem _fileSystem;

        // Files to copy relative to source directory
        private static readonly string[] FilesToCopy =
        {
            "src/unity_code_mcp_stdio/__init__.py",
            "src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio.py",
            "src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio_over_http.py",
            "pyproject.toml",
            "uv.lock"
        };

        public PackageInstaller(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public bool Install(string packageRoot)
        {
            string sourcePath = NormalizePath(Path.Combine(packageRoot, SourceFolder));
            string targetPath = NormalizePath(Path.GetFullPath(TargetFolder));

            UnityCodeMcpServerLogger.Debug($"[PackageInstaller] Installing from {sourcePath} to {targetPath}");

            return Install(sourcePath, targetPath);
        }

        public bool Install(string sourcePath, string targetPath)
        {
            if (PathsMatch(sourcePath, targetPath))
            {
                UnityCodeMcpServerLogger.Debug($"[PackageInstaller] Source and target are the same, skipping STDIO installation.");
                return false;
            }

            if (!_fileSystem.DirectoryExists(sourcePath))
            {
                UnityCodeMcpServerLogger.Error($"[PackageInstaller] Source directory not found: {sourcePath}");
                return false;
            }

            try
            {
                bool anyFilesCopied = CopySpecificFiles(sourcePath, targetPath);

                if (anyFilesCopied)
                {
                    UnityCodeMcpServerLogger.Info($"[PackageInstaller] Successfully installed assets to: {targetPath}");
                }
                else
                {
                    UnityCodeMcpServerLogger.Trace($"[PackageInstaller] No files needed updating in: {targetPath}");
                }

                return anyFilesCopied;
            }
            catch (System.Exception ex)
            {
                UnityCodeMcpServerLogger.Error($"[PackageInstaller] Failed to install assets. Error: {ex.Message}");
                return false;
            }
        }

        private bool CopySpecificFiles(string sourceDir, string targetDir)
        {
            bool anyFilesCopied = false;

            foreach (string relativeFilePath in FilesToCopy)
            {
                string sourcePath = NormalizePath(Path.Combine(sourceDir, relativeFilePath));
                string destPath = NormalizePath(Path.Combine(targetDir, relativeFilePath));

                if (!_fileSystem.FileExists(sourcePath))
                {
                    UnityCodeMcpServerLogger.Error($"[PackageInstaller] Required file not found: {sourcePath}");
                    continue;
                }

                bool shouldCopy = ShouldCopyFile(sourcePath, destPath);

                if (shouldCopy)
                {
                    // Create directory if needed
                    string destDirectory = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDirectory) && !_fileSystem.DirectoryExists(destDirectory))
                    {
                        _fileSystem.CreateDirectory(destDirectory);
                        UnityCodeMcpServerLogger.Debug($"[PackageInstaller] Created directory: {destDirectory}");
                    }

                    _fileSystem.CopyFile(sourcePath, destPath, true);
                    UnityCodeMcpServerLogger.Info($"[PackageInstaller] Copied: {NormalizePath(Path.Combine(targetDir, relativeFilePath))}");
                    anyFilesCopied = true;
                }
                else
                {
                    UnityCodeMcpServerLogger.Trace($"[PackageInstaller] Skipped (unchanged): {NormalizePath(Path.Combine(targetDir, relativeFilePath))}");
                }
            }

            return anyFilesCopied;
        }

        private bool ShouldCopyFile(string sourcePath, string destPath)
        {
            // Copy if destination doesn't exist
            if (!_fileSystem.FileExists(destPath))
            {
                return true;
            }

            // Compare file hashes
            try
            {
                string sourceHash = _fileSystem.ComputeFileHash(sourcePath);
                string destHash = _fileSystem.ComputeFileHash(destPath);
                return sourceHash != destHash;
            }
            catch (System.Exception ex)
            {
                UnityCodeMcpServerLogger.Warn($"[PackageInstaller] Failed to compute hash, will copy file. Error: {ex.Message}");
                return true; // Copy on hash error to be safe
            }
        }

        private static bool PathsMatch(string leftPath, string rightPath)
        {
            string normalizedLeftPath = NormalizePath(Path.GetFullPath(leftPath)).TrimEnd('/');
            string normalizedRightPath = NormalizePath(Path.GetFullPath(rightPath)).TrimEnd('/');
            return string.Equals(normalizedLeftPath, normalizedRightPath, StringComparison.OrdinalIgnoreCase);
        }

        // Normalize to forward slashes so tests and Unity paths stay consistent across platforms.
        private static string NormalizePath(string path) => path.Replace("\\", "/");
    }
}
