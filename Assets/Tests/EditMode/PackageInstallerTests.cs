using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityCodeMcpServer.Editor.Installer;
using UnityEngine.TestTools;

namespace UnityCodeMcpServer.Tests.EditMode
{
    public class PackageInstallerTests
    {
        // Mock class to simulate File System
        public class MockFileSystem : IFileSystem
        {
            public HashSet<string> Directories = new();
            public Dictionary<string, string> Files = new(); // Path, Content
            public List<string> CopiedFiles = new();

            public bool DirectoryExists(string path) => Directories.Contains(path);
            public bool FileExists(string path) => Files.ContainsKey(path);
            public void CreateDirectory(string path) => Directories.Add(path);
            public void CopyFile(string s, string d, bool o) => CopiedFiles.Add($"{s}->{d}");
            public void DeleteFile(string path) => Files.Remove(path);
            public void DeleteDirectory(string path, bool recursive) => Directories.Remove(path);
            public string GetFileName(string path) => System.IO.Path.GetFileName(path);
            public string ReadAllText(string filePath) => Files.ContainsKey(filePath) ? Files[filePath] : "";

            public string ComputeFileHash(string filePath)
            {
                if (!Files.ContainsKey(filePath)) return "";

                string content = Files[filePath];
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
                    return string.Concat(hash.Select(b => b.ToString("x2")));
                }
            }

            public string[] GetFiles(string path)
            {
                // Simple mock implementation for finding files in "path"
                List<string> list = new();
                foreach (string k in Files.Keys) if (k.StartsWith(path) && !k.EndsWith(".meta")) list.Add(k);
                return list.ToArray();
            }

            public string[] GetDirectories(string path) => new string[0]; // Simplify for basic test
        }

        [Test]
        public void Install_CopiesSpecificFiles_WhenTargetDoesNotExist()
        {
            // Arrange
            MockFileSystem mockFS = new();
            string source = "Packages/MyPkg/STDIO~";
            string target = "Assets/Plugins/MyPkg/STDIO~";

            mockFS.Directories.Add(source);
            mockFS.Files.Add(source + "/src/unity_code_mcp_stdio/__init__.py", "init");
            mockFS.Files.Add(source + "/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio.py", "python code");
            mockFS.Files.Add(source + "/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio_over_http.py", "http python code");
            mockFS.Files.Add(source + "/pyproject.toml", "toml content");
            mockFS.Files.Add(source + "/uv.lock", "lock content");

            PackageInstaller installer = new(mockFS);

            // Act
            bool result = installer.Install(source, target);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(5, mockFS.CopiedFiles.Count);
            Assert.IsTrue(mockFS.CopiedFiles.Any(f => f.Contains("__init__.py")));
            Assert.IsTrue(mockFS.CopiedFiles.Any(f => f.Contains("unity_code_mcp_bridge_stdio.py")));
            Assert.IsTrue(mockFS.CopiedFiles.Any(f => f.Contains("unity_code_mcp_bridge_stdio_over_http.py")));
            Assert.IsTrue(mockFS.CopiedFiles.Any(f => f.Contains("pyproject.toml")));
            Assert.IsTrue(mockFS.CopiedFiles.Any(f => f.Contains("uv.lock")));
        }

        [Test]
        public void Install_SkipsUnchangedFiles_WhenHashMatches()
        {
            // Arrange
            MockFileSystem mockFS = new();
            string source = "Packages/MyPkg/STDIO~";
            string target = "Assets/Plugins/MyPkg/STDIO~";

            mockFS.Directories.Add(source);
            mockFS.Directories.Add(target);

            // Add source files
            mockFS.Files.Add(source + "/src/unity_code_mcp_stdio/__init__.py", "init");
            mockFS.Files.Add(source + "/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio.py", "python code");
            mockFS.Files.Add(source + "/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio_over_http.py", "http python code");
            mockFS.Files.Add(source + "/pyproject.toml", "toml content");
            mockFS.Files.Add(source + "/uv.lock", "lock content");

            // Add existing target files with same content (same hash)
            mockFS.Files.Add(target + "/src/unity_code_mcp_stdio/__init__.py", "init");
            mockFS.Files.Add(target + "/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio.py", "python code");
            mockFS.Files.Add(target + "/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio_over_http.py", "http python code");
            mockFS.Files.Add(target + "/pyproject.toml", "toml content");
            mockFS.Files.Add(target + "/uv.lock", "lock content");

            PackageInstaller installer = new(mockFS);

            // Act
            bool result = installer.Install(source, target);

            // Assert
            Assert.IsFalse(result); // Should report nothing done
            Assert.AreEqual(0, mockFS.CopiedFiles.Count); // Should not copy
        }

        [Test]
        public void Install_CopiesOnlyChangedFiles_WhenHashDiffers()
        {
            // Arrange
            MockFileSystem mockFS = new();
            string source = "Packages/MyPkg/STDIO~";
            string target = "Assets/Plugins/MyPkg/STDIO~";

            mockFS.Directories.Add(source);
            mockFS.Directories.Add(target);

            // Add source files
            mockFS.Files.Add(source + "/src/unity_code_mcp_stdio/__init__.py", "init");
            mockFS.Files.Add(source + "/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio.py", "NEW python code");
            mockFS.Files.Add(source + "/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio_over_http.py", "NEW http python code");
            mockFS.Files.Add(source + "/pyproject.toml", "toml content");
            mockFS.Files.Add(source + "/uv.lock", "NEW lock content");

            // Add existing target files - only pyproject.toml matches
            mockFS.Files.Add(target + "/src/unity_code_mcp_stdio/__init__.py", "init"); // Same
            mockFS.Files.Add(target + "/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio.py", "OLD python code");
            mockFS.Files.Add(target + "/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio_over_http.py", "OLD http python code");
            mockFS.Files.Add(target + "/pyproject.toml", "toml content"); // Same content
            mockFS.Files.Add(target + "/uv.lock", "OLD lock content");

            PackageInstaller installer = new(mockFS);

            // Act
            bool result = installer.Install(source, target);

            // Assert
            Assert.IsTrue(result); // Changed files were copied
            Assert.AreEqual(3, mockFS.CopiedFiles.Count); // Only changed files
            Assert.IsFalse(mockFS.CopiedFiles.Any(f => f.Contains("__init__.py"))); // Unchanged
            Assert.IsTrue(mockFS.CopiedFiles.Any(f => f.Contains("unity_code_mcp_bridge_stdio.py")));
            Assert.IsTrue(mockFS.CopiedFiles.Any(f => f.Contains("unity_code_mcp_bridge_stdio_over_http.py")));
            Assert.IsFalse(mockFS.CopiedFiles.Any(f => f.Contains("pyproject.toml"))); // Unchanged
            Assert.IsTrue(mockFS.CopiedFiles.Any(f => f.Contains("uv.lock")));
        }

        [Test]
        public void Install_ReturnsFalse_WhenSourceNotFound()
        {
            // Arrange
            MockFileSystem mockFS = new();
            string source = "Packages/NonExistent";
            string target = "Assets/Plugins/MyPkg/STDIO~";

            PackageInstaller installer = new(mockFS);

            // Expect error log
            LogAssert.Expect(UnityEngine.LogType.Error, $"[ERROR] #UnityCodeMcpServer Source directory not found: {source}");

            // Act
            bool result = installer.Install(source, target);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(0, mockFS.CopiedFiles.Count);
        }

        [Test]
        public void InstallContent_ReturnsTrue_WhenSkillsInstallerCopiesFiles()
        {
            bool packageInstallerCalled = false;
            bool skillsInstallerCalled = false;

            bool result = PackageInstaller.InstallContent(
                installPackageFiles: () =>
                {
                    packageInstallerCalled = true;
                    return false;
                },
                installSkills: () =>
                {
                    skillsInstallerCalled = true;
                    return true;
                });

            Assert.IsTrue(result);
            Assert.IsTrue(packageInstallerCalled);
            Assert.IsTrue(skillsInstallerCalled);
        }

        [Test]
        public void InstallContent_ReturnsFalse_WhenNothingChanges()
        {
            bool result = PackageInstaller.InstallContent(
                installPackageFiles: () => false,
                installSkills: () => false);

            Assert.IsFalse(result);
        }

        [Test]
        public void RunInstallSteps_RunsSkills_WhenPackageInstallIsSkipped()
        {
            bool packageInstallerCalled = false;
            bool skillsInstallerCalled = false;

            bool result = PackageInit.RunInstallSteps(
                skipPackageInstall: true,
                installPackageFiles: () =>
                {
                    packageInstallerCalled = true;
                    return true;
                },
                installSkills: () =>
                {
                    skillsInstallerCalled = true;
                    return true;
                });

            Assert.IsTrue(result);
            Assert.IsFalse(packageInstallerCalled);
            Assert.IsTrue(skillsInstallerCalled);
        }
    }
}
