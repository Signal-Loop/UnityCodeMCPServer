using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace LoopMcpServer.Editor.Installer
{
    public class PackageInstaller
    {
        private readonly IFileSystem _fileSystem;
        private readonly bool _verboseLogging;

        // Files to copy relative to source directory
        private static readonly string[] FilesToCopy =
        {
            "src/loop_mcp_stdio/__init__.py",
            "src/loop_mcp_stdio/loop_mcp_bridge_stdio.py",
            "pyproject.toml",
            "uv.lock"
        };

        public PackageInstaller(IFileSystem fileSystem, bool verboseLogging = false)
        {
            _fileSystem = fileSystem;
            _verboseLogging = verboseLogging;
        }

        public bool Install(string sourcePath, string targetPath)
        {
            if (!_fileSystem.DirectoryExists(sourcePath))
            {
                Debug.LogError($"{Protocol.McpProtocol.LogPrefix} Source directory not found: {sourcePath}");
                return false;
            }

            try
            {
                bool anyFilesCopied = CopySpecificFiles(sourcePath, targetPath);

                if (anyFilesCopied)
                {
                    Debug.Log($"{Protocol.McpProtocol.LogPrefix} Successfully installed assets to: {targetPath}");
                }
                else if (_verboseLogging)
                {
                    Debug.Log($"{Protocol.McpProtocol.LogPrefix} No files needed updating in: {targetPath}");
                }

                return anyFilesCopied;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"{Protocol.McpProtocol.LogPrefix} Failed to install assets. Error: {ex.Message}");
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
                    Debug.LogError($"{Protocol.McpProtocol.LogPrefix} Required file not found: {sourcePath}");
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
                        if (_verboseLogging)
                        {
                            Debug.Log($"{Protocol.McpProtocol.LogPrefix} Created directory: {destDirectory}");
                        }
                    }

                    _fileSystem.CopyFile(sourcePath, destPath, true);
                    Debug.Log($"{Protocol.McpProtocol.LogPrefix} Copied: {NormalizePath(Path.Combine(targetDir, relativeFilePath))}");
                    anyFilesCopied = true;
                }
                else if (_verboseLogging)
                {
                    Debug.Log($"{Protocol.McpProtocol.LogPrefix} Skipped (unchanged): {NormalizePath(Path.Combine(targetDir, relativeFilePath))}");
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
                Debug.LogWarning($"{Protocol.McpProtocol.LogPrefix} Failed to compute hash, will copy file. Error: {ex.Message}");
                return true; // Copy on hash error to be safe
            }
        }

        // Normalize to forward slashes so tests and Unity paths stay consistent across platforms.
        private static string NormalizePath(string path) => path.Replace("\\", "/");
    }
}