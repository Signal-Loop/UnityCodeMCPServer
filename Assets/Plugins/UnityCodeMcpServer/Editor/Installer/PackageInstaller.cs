using UnityCodeMcpServer.Helpers;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace UnityCodeMcpServer.Editor.Installer
{
    public class PackageInstaller
    {
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

        public static bool InstallContent(Func<bool> installPackageFiles, Func<bool> installSkills)
        {
            bool packageFilesChanged = installPackageFiles != null && installPackageFiles();
            bool skillsChanged = installSkills != null && installSkills();
            return packageFilesChanged || skillsChanged;
        }

        public bool Install(string sourcePath, string targetPath)
        {
            if (!_fileSystem.DirectoryExists(sourcePath))
            {
                LoopLogger.Error($"{Protocol.McpProtocol.LogPrefix} Source directory not found: {sourcePath}");
                return false;
            }

            try
            {
                bool anyFilesCopied = CopySpecificFiles(sourcePath, targetPath);

                if (anyFilesCopied)
                {
                    LoopLogger.Info($"{Protocol.McpProtocol.LogPrefix} Successfully installed assets to: {targetPath}");
                }
                else
                {
                    LoopLogger.Trace($"{Protocol.McpProtocol.LogPrefix} No files needed updating in: {targetPath}");
                }

                return anyFilesCopied;
            }
            catch (System.Exception ex)
            {
                LoopLogger.Error($"{Protocol.McpProtocol.LogPrefix} Failed to install assets. Error: {ex.Message}");
                return false;
            }
        }

        private bool CopySpecificFiles(string sourceDir, string targetDir)
        {
            bool anyFilesCopied = false;

            foreach (var relativeFilePath in FilesToCopy)
            {
                string sourcePath = NormalizePath(Path.Combine(sourceDir, relativeFilePath));
                string destPath = NormalizePath(Path.Combine(targetDir, relativeFilePath));

                if (!_fileSystem.FileExists(sourcePath))
                {
                    LoopLogger.Error($"{Protocol.McpProtocol.LogPrefix} Required file not found: {sourcePath}");
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
                        LoopLogger.Debug($"{Protocol.McpProtocol.LogPrefix} Created directory: {destDirectory}");
                    }

                    _fileSystem.CopyFile(sourcePath, destPath, true);
                    LoopLogger.Info($"{Protocol.McpProtocol.LogPrefix} Copied: {NormalizePath(Path.Combine(targetDir, relativeFilePath))}");
                    anyFilesCopied = true;
                }
                else
                {
                    LoopLogger.Trace($"{Protocol.McpProtocol.LogPrefix} Skipped (unchanged): {NormalizePath(Path.Combine(targetDir, relativeFilePath))}");
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
                LoopLogger.Warn($"{Protocol.McpProtocol.LogPrefix} Failed to compute hash, will copy file. Error: {ex.Message}");
                return true; // Copy on hash error to be safe
            }
        }

        // Normalize to forward slashes so tests and Unity paths stay consistent across platforms.
        private static string NormalizePath(string path) => path.Replace("\\", "/");
    }
}