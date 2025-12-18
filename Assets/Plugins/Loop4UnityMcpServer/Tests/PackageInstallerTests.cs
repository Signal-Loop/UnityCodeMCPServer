using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using LoopMcpServer.Editor.Installer;
using UnityEngine.TestTools;

public class PackageInstallerTests
{
    // Mock class to simulate File System
    public class MockFileSystem : IFileSystem
    {
        public HashSet<string> Directories = new HashSet<string>();
        public Dictionary<string, string> Files = new Dictionary<string, string>(); // Path, Content
        public List<string> CopiedFiles = new List<string>();

        public bool DirectoryExists(string path) => Directories.Contains(path);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public void CreateDirectory(string path) => Directories.Add(path);
        public void CopyFile(string s, string d, bool o) => CopiedFiles.Add($"{s}->{d}");
        public string GetFileName(string path) => System.IO.Path.GetFileName(path);
        public string ReadAllText(string filePath) => Files.ContainsKey(filePath) ? Files[filePath] : "";

        public string ComputeFileHash(string filePath)
        {
            if (!Files.ContainsKey(filePath)) return "";

            var content = Files[filePath];
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
                return string.Concat(hash.Select(b => b.ToString("x2")));
            }
        }

        public string[] GetFiles(string path)
        {
            // Simple mock implementation for finding files in "path"
            var list = new List<string>();
            foreach (var k in Files.Keys) if (k.StartsWith(path) && !k.EndsWith(".meta")) list.Add(k);
            return list.ToArray();
        }

        public string[] GetDirectories(string path) => new string[0]; // Simplify for basic test
    }

    [Test]
    public void Install_CopiesSpecificFiles_WhenTargetDoesNotExist()
    {
        // Arrange
        var mockFS = new MockFileSystem();
        string source = "Packages/MyPkg/STDIO~";
        string target = "Assets/Plugins/MyPkg/STDIO~";

        mockFS.Directories.Add(source);
        mockFS.Files.Add(source + "/src/loop_mcp_stdio/loop_mcp_bridge_stdio.py", "python code");
        mockFS.Files.Add(source + "/pyproject.toml", "toml content");
        mockFS.Files.Add(source + "/uv.lock", "lock content");

        var installer = new PackageInstaller(mockFS, verboseLogging: false);

        // Act
        bool result = installer.Install(source, target);

        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual(3, mockFS.CopiedFiles.Count);
        Assert.IsTrue(mockFS.CopiedFiles.Any(f => f.Contains("loop_mcp_bridge_stdio.py")));
        Assert.IsTrue(mockFS.CopiedFiles.Any(f => f.Contains("pyproject.toml")));
        Assert.IsTrue(mockFS.CopiedFiles.Any(f => f.Contains("uv.lock")));
    }

    [Test]
    public void Install_SkipsUnchangedFiles_WhenHashMatches()
    {
        // Arrange
        var mockFS = new MockFileSystem();
        string source = "Packages/MyPkg/STDIO~";
        string target = "Assets/Plugins/MyPkg/STDIO~";

        mockFS.Directories.Add(source);
        mockFS.Directories.Add(target);

        // Add source files
        mockFS.Files.Add(source + "/src/loop_mcp_stdio/loop_mcp_bridge_stdio.py", "python code");
        mockFS.Files.Add(source + "/pyproject.toml", "toml content");
        mockFS.Files.Add(source + "/uv.lock", "lock content");

        // Add existing target files with same content (same hash)
        mockFS.Files.Add(target + "/src/loop_mcp_stdio/loop_mcp_bridge_stdio.py", "python code");
        mockFS.Files.Add(target + "/pyproject.toml", "toml content");
        mockFS.Files.Add(target + "/uv.lock", "lock content");

        var installer = new PackageInstaller(mockFS, verboseLogging: false);

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
        var mockFS = new MockFileSystem();
        string source = "Packages/MyPkg/STDIO~";
        string target = "Assets/Plugins/MyPkg/STDIO~";

        mockFS.Directories.Add(source);
        mockFS.Directories.Add(target);

        // Add source files
        mockFS.Files.Add(source + "/src/loop_mcp_stdio/loop_mcp_bridge_stdio.py", "NEW python code");
        mockFS.Files.Add(source + "/pyproject.toml", "toml content");
        mockFS.Files.Add(source + "/uv.lock", "NEW lock content");

        // Add existing target files - only pyproject.toml matches
        mockFS.Files.Add(target + "/src/loop_mcp_stdio/loop_mcp_bridge_stdio.py", "OLD python code");
        mockFS.Files.Add(target + "/pyproject.toml", "toml content"); // Same content
        mockFS.Files.Add(target + "/uv.lock", "OLD lock content");

        var installer = new PackageInstaller(mockFS, verboseLogging: false);

        // Act
        bool result = installer.Install(source, target);

        // Assert
        Assert.IsTrue(result); // Changed files were copied
        Assert.AreEqual(2, mockFS.CopiedFiles.Count); // Only 2 changed files
        Assert.IsTrue(mockFS.CopiedFiles.Any(f => f.Contains("loop_mcp_bridge_stdio.py")));
        Assert.IsFalse(mockFS.CopiedFiles.Any(f => f.Contains("pyproject.toml"))); // Unchanged
        Assert.IsTrue(mockFS.CopiedFiles.Any(f => f.Contains("uv.lock")));
    }

    [Test]
    public void Install_ReturnsFalse_WhenSourceNotFound()
    {
        // Arrange
        var mockFS = new MockFileSystem();
        string source = "Packages/NonExistent";
        string target = "Assets/Plugins/MyPkg/STDIO~";

        var installer = new PackageInstaller(mockFS, verboseLogging: false);

        // Expect error log
        LogAssert.Expect(UnityEngine.LogType.Error, $"#LoopMcpServer Source directory not found: {source}");

        // Act
        bool result = installer.Install(source, target);

        // Assert
        Assert.IsFalse(result);
        Assert.AreEqual(0, mockFS.CopiedFiles.Count);
    }
}