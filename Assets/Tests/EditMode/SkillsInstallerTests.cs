using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using UnityCodeMcpServer.Editor.Installer;
using UnityCodeMcpServer.Settings.Editor;
using UnityEngine.TestTools;

namespace UnityCodeMcpServer.Tests.EditMode
{
    public class SkillsInstallerTests
    {
        // ── Shared mock ───────────────────────────────────────────────────────

        /// <summary>
        /// In-memory file system for deterministic, isolation-friendly tests.
        /// Paths stored and compared as-is (forward-slash normalised externally by caller).
        /// </summary>
        private class MockFileSystem : IFileSystem
        {
            /// <summary>All directories that "exist".</summary>
            public HashSet<string> Directories { get; } = new HashSet<string>();

            /// <summary>All files that "exist", keyed by path with their content.</summary>
            public Dictionary<string, string> Files { get; } = new Dictionary<string, string>();

            /// <summary>Record of every CopyFile call as "src->dst".</summary>
            public List<string> CopiedFiles { get; } = new List<string>();

            /// <summary>Record of every CreateDirectory call.</summary>
            public List<string> CreatedDirectories { get; } = new List<string>();

            public bool DirectoryExists(string path) => Directories.Contains(path);
            public bool FileExists(string path) => Files.ContainsKey(path);

            public void CreateDirectory(string path)
            {
                Directories.Add(path);
                CreatedDirectories.Add(path);
            }

            public void CopyFile(string src, string dst, bool overwrite)
            {
                CopiedFiles.Add($"{src}->{dst}");
                // Propagate content so subsequent hash comparisons work correctly
                if (Files.TryGetValue(src, out string content))
                    Files[dst] = content;
            }

            public string[] GetFiles(string path)
            {
                // Return only direct children (no sub-path separator after the prefix)
                return Files.Keys
                    .Where(k =>
                    {
                        if (!k.StartsWith(path + "/")) return false;
                        string remainder = k.Substring(path.Length + 1);
                        return !remainder.Contains('/');
                    })
                    .ToArray();
            }

            public string[] GetDirectories(string path)
            {
                // Return the unique immediate sub-directories of `path`
                var result = new HashSet<string>();
                foreach (var dir in Directories)
                {
                    if (!dir.StartsWith(path + "/")) continue;
                    string remainder = dir.Substring(path.Length + 1);
                    if (remainder.Contains('/')) continue;  // skip nested dirs here
                    result.Add(dir);
                }
                return result.ToArray();
            }

            public string GetFileName(string path) =>
                System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));

            public string ReadAllText(string filePath) =>
                Files.TryGetValue(filePath, out string v) ? v : string.Empty;

            public string ComputeFileHash(string filePath)
            {
                string content = ReadAllText(filePath);
                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
                    return string.Concat(hash.Select(b => b.ToString("x2")));
                }
            }
        }

        // ── Helper: build a standard source layout ────────────────────────────

        private const string SourceRoot = "pkg/Editor/Skills";
        private const string TargetRoot = "/home/user/.copilot/skills";

        /// <summary>
        /// Adds a single skill folder with one SKILL.md file to the mock file system.
        /// </summary>
        private static void AddSkillFolder(MockFileSystem fs, string root, string skillName, string content = "# skill")
        {
            string dir = $"{root}/{skillName}";
            fs.Directories.Add(dir);
            fs.Files[$"{dir}/SKILL.md"] = content;
        }

        // ── Install: source missing ───────────────────────────────────────────

        [Test]
        public void Install_ReturnsFailure_WhenSourceDirectoryDoesNotExist()
        {
            var fs = new MockFileSystem();
            var installer = new SkillsInstaller(fs);

            LogAssert.Expect(UnityEngine.LogType.Error,
                $"[ERROR] #UnityCodeMcpServer Skills source directory not found: missing/path");

            SkillsInstallResult result = installer.Install("missing/path", TargetRoot);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("missing/path", result.ErrorMessage);
            Assert.AreEqual(0, fs.CopiedFiles.Count);
        }

        [Test]
        public void Install_ReturnsFailure_WhenTargetPathIsEmpty()
        {
            var fs = new MockFileSystem();
            fs.Directories.Add(SourceRoot);
            var installer = new SkillsInstaller(fs);

            SkillsInstallResult result = installer.Install(SourceRoot, string.Empty);

            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.ErrorMessage);
        }

        [Test]
        public void Install_ReturnsFailure_WhenTargetPathIsWhitespace()
        {
            var fs = new MockFileSystem();
            fs.Directories.Add(SourceRoot);
            var installer = new SkillsInstaller(fs);

            SkillsInstallResult result = installer.Install(SourceRoot, "   ");

            Assert.IsFalse(result.Success);
        }

        // ── Install: empty source ─────────────────────────────────────────────

        [Test]
        public void Install_ReturnsSuccessWithNoChanges_WhenSourceIsEmpty()
        {
            var fs = new MockFileSystem();
            fs.Directories.Add(SourceRoot);
            var installer = new SkillsInstaller(fs);

            SkillsInstallResult result = installer.Install(SourceRoot, TargetRoot);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, result.FilesUpdated);
            Assert.AreEqual(0, result.SkillFoldersUpdated);
            Assert.IsFalse(result.AnyChanges);
        }

        // ── Install: single skill, fresh target ───────────────────────────────

        [Test]
        public void Install_CopiesAllFiles_WhenTargetDoesNotExist()
        {
            var fs = new MockFileSystem();
            fs.Directories.Add(SourceRoot);
            AddSkillFolder(fs, SourceRoot, "my-skill");

            var installer = new SkillsInstaller(fs);
            SkillsInstallResult result = installer.Install(SourceRoot, TargetRoot);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.FilesUpdated);
            Assert.AreEqual(1, result.SkillFoldersUpdated);
            Assert.IsTrue(result.AnyChanges);
            Assert.AreEqual(1, fs.CopiedFiles.Count);
            StringAssert.Contains("SKILL.md", fs.CopiedFiles[0]);
        }

        [Test]
        public void Install_CreatesTargetSkillDirectory_WhenItDoesNotExist()
        {
            var fs = new MockFileSystem();
            fs.Directories.Add(SourceRoot);
            AddSkillFolder(fs, SourceRoot, "my-skill");

            var installer = new SkillsInstaller(fs);
            installer.Install(SourceRoot, TargetRoot);

            Assert.IsTrue(fs.Directories.Any(d => d.Contains("my-skill")));
        }

        // ── Install: multiple skills ──────────────────────────────────────────

        [Test]
        public void Install_CopiesAllSkillFolders_WhenMultipleSkillsExist()
        {
            var fs = new MockFileSystem();
            fs.Directories.Add(SourceRoot);
            AddSkillFolder(fs, SourceRoot, "skill-a");
            AddSkillFolder(fs, SourceRoot, "skill-b");
            AddSkillFolder(fs, SourceRoot, "skill-c");

            var installer = new SkillsInstaller(fs);
            SkillsInstallResult result = installer.Install(SourceRoot, TargetRoot);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, result.FilesUpdated);
            Assert.AreEqual(3, result.SkillFoldersUpdated);
        }

        // ── Install: skip unchanged files ─────────────────────────────────────

        [Test]
        public void Install_SkipsFile_WhenHashMatchesExistingTarget()
        {
            const string content = "# unchanged skill";
            var fs = new MockFileSystem();
            fs.Directories.Add(SourceRoot);
            AddSkillFolder(fs, SourceRoot, "my-skill", content);

            // Pre-populate target with identical content
            string targetSkillDir = $"{TargetRoot}/my-skill";
            fs.Directories.Add(targetSkillDir);
            fs.Files[$"{targetSkillDir}/SKILL.md"] = content;

            var installer = new SkillsInstaller(fs);
            SkillsInstallResult result = installer.Install(SourceRoot, TargetRoot);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, result.FilesUpdated);
            Assert.IsFalse(result.AnyChanges);
            Assert.AreEqual(0, fs.CopiedFiles.Count);
        }

        [Test]
        public void Install_CopiesOnlyChangedFiles_WhenSomeHashesDiffer()
        {
            var fs = new MockFileSystem();
            fs.Directories.Add(SourceRoot);
            AddSkillFolder(fs, SourceRoot, "skill-unchanged", "# same");
            AddSkillFolder(fs, SourceRoot, "skill-changed", "# new content");

            string targetUnchangedDir = $"{TargetRoot}/skill-unchanged";
            fs.Directories.Add(targetUnchangedDir);
            fs.Files[$"{targetUnchangedDir}/SKILL.md"] = "# same";

            string targetChangedDir = $"{TargetRoot}/skill-changed";
            fs.Directories.Add(targetChangedDir);
            fs.Files[$"{targetChangedDir}/SKILL.md"] = "# old content";

            var installer = new SkillsInstaller(fs);
            SkillsInstallResult result = installer.Install(SourceRoot, TargetRoot);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.FilesUpdated);
            Assert.AreEqual(1, fs.CopiedFiles.Count);
            StringAssert.Contains("skill-changed", fs.CopiedFiles[0]);
        }

        // ── Install: recursive subdirectories ────────────────────────────────

        [Test]
        public void Install_CopiesFilesRecursively_WhenSkillHasSubfolders()
        {
            var fs = new MockFileSystem();
            fs.Directories.Add(SourceRoot);

            string skillDir = $"{SourceRoot}/deep-skill";
            string subDir = $"{skillDir}/examples";
            fs.Directories.Add(skillDir);
            fs.Directories.Add(subDir);
            fs.Files[$"{skillDir}/SKILL.md"] = "# root file";
            fs.Files[$"{subDir}/example.md"] = "# example";

            var installer = new SkillsInstaller(fs);
            SkillsInstallResult result = installer.Install(SourceRoot, TargetRoot);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.FilesUpdated);
            Assert.IsTrue(fs.CopiedFiles.Any(f => f.Contains("SKILL.md")));
            Assert.IsTrue(fs.CopiedFiles.Any(f => f.Contains("example.md")));
        }

        // ── SkillsInstallResult.ToString ──────────────────────────────────────

        [Test]
        public void InstallResult_ToString_DescribesFailure()
        {
            var result = SkillsInstallResult.Failure("oops");

            StringAssert.Contains("Failed", result.ToString());
            StringAssert.Contains("oops", result.ToString());
        }

        [Test]
        public void InstallResult_ToString_DescribesUpToDate()
        {
            var result = new SkillsInstallResult { Success = true, FilesUpdated = 0 };

            StringAssert.Contains("up to date", result.ToString());
        }

        [Test]
        public void InstallResult_ToString_DescribesChanges()
        {
            var result = new SkillsInstallResult
            {
                Success = true,
                SkillFoldersUpdated = 2,
                FilesUpdated = 5
            };

            StringAssert.Contains("2", result.ToString());
            StringAssert.Contains("5", result.ToString());
        }

        // ── ResolveSkillsSourcePath ───────────────────────────────────────────

        [Test]
        public void ResolveSkillsSourcePath_ReturnsNonNullPath_InEditorContext()
        {
            // This test validates that the static resolver can successfully locate
            // the Skills directory when running inside Unity Editor (either via
            // PackageInfo or the Assets/Plugins fallback).
            string path = UnityCodeMcpServerSettingsEditor.ResolveSkillsSourcePath();

            // The test environment is the Unity Editor running from this project,
            // so the Assets/Plugins fallback should always find the folder.
            Assert.IsNotNull(path, "Expected to resolve a non-null skills source path.");
            Assert.IsTrue(System.IO.Directory.Exists(path),
                $"Resolved path should exist on disk: {path}");
        }
    }
}
