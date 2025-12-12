using NUnit.Framework;
using System.Collections.Generic;
using LoopMcpServer.Editor.Installer;

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
        
        public string[] GetFiles(string path) 
        {
            // Simple mock implementation for finding files in "path"
            var list = new List<string>();
            foreach(var k in Files.Keys) if(k.StartsWith(path) && !k.EndsWith(".meta")) list.Add(k);
            return list.ToArray();
        }
        
        public string[] GetDirectories(string path) => new string[0]; // Simplify for basic test
    }

    [Test]
    public void Install_CopiesFiles_WhenTargetDoesNotExist()
    {
        // Arrange
        var mockFS = new MockFileSystem();
        string source = "Packages/MyPkg/Plugins~";
        string target = "Assets/Plugins/MyPkg";
        
        mockFS.Directories.Add(source);
        mockFS.Files.Add(source + "/lib.dll", "data");

        var installer = new PackageInstaller(mockFS);

        // Act
        bool result = installer.Install(source, target);

        // Assert
        Assert.IsTrue(result);
        Assert.IsTrue(mockFS.Directories.Contains(target)); // Created Target
        Assert.IsTrue(mockFS.CopiedFiles.Contains($"{source}/lib.dll->{target}/lib.dll")); // Copied File
    }

    [Test]
    public void Install_DoesNothing_WhenTargetExists()
    {
        // Arrange
        var mockFS = new MockFileSystem();
        string source = "Packages/MyPkg/Plugins~";
        string target = "Assets/Plugins/MyPkg";
        
        mockFS.Directories.Add(source);
        mockFS.Directories.Add(target); // Target already exists

        var installer = new PackageInstaller(mockFS);

        // Act
        bool result = installer.Install(source, target);

        // Assert
        Assert.IsFalse(result); // Should report nothing done
        Assert.AreEqual(0, mockFS.CopiedFiles.Count); // Should not copy
    }
}