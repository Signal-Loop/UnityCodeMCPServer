using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
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

            public void DeleteFile(string path)
            {
                Files.Remove(path);
            }

            public void DeleteDirectory(string path, bool recursive)
            {
                Directories.Remove(path);

                if (!recursive)
                    return;

                foreach (string file in Files.Keys.Where(k => k.StartsWith(path + "/")).ToList())
                {
                    Files.Remove(file);
                }

                foreach (string dir in Directories.Where(d => d.StartsWith(path + "/")).ToList())
                {
                    Directories.Remove(dir);
                }
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
                HashSet<string> result = new();
                foreach (string dir in Directories)
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
                using (SHA256 sha = SHA256.Create())
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
            MockFileSystem fs = new();
            SkillsInstaller installer = new(fs);

            LogAssert.Expect(UnityEngine.LogType.Error,
                $"[ERROR] #UnityCodeMcpServer [SkillsInstaller] Skills source directory not found: missing/path");

            SkillsInstallResult result = installer.Install("missing/path", TargetRoot);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("missing/path", result.ErrorMessage);
            Assert.AreEqual(0, fs.CopiedFiles.Count);
        }

        [Test]
        public void Install_ReturnsFailure_WhenTargetPathIsEmpty()
        {
            MockFileSystem fs = new();
            fs.Directories.Add(SourceRoot);
            SkillsInstaller installer = new(fs);

            SkillsInstallResult result = installer.Install(SourceRoot, string.Empty);

            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.ErrorMessage);
        }

        [Test]
        public void Install_ReturnsFailure_WhenTargetPathIsWhitespace()
        {
            MockFileSystem fs = new();
            fs.Directories.Add(SourceRoot);
            SkillsInstaller installer = new(fs);

            SkillsInstallResult result = installer.Install(SourceRoot, "   ");

            Assert.IsFalse(result.Success);
        }

        // ── Install: empty source ─────────────────────────────────────────────

        [Test]
        public void Install_ReturnsSuccessWithNoChanges_WhenSourceIsEmpty()
        {
            MockFileSystem fs = new();
            fs.Directories.Add(SourceRoot);
            SkillsInstaller installer = new(fs);

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
            MockFileSystem fs = new();
            fs.Directories.Add(SourceRoot);
            AddSkillFolder(fs, SourceRoot, "my-skill");

            SkillsInstaller installer = new(fs);
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
            MockFileSystem fs = new();
            fs.Directories.Add(SourceRoot);
            AddSkillFolder(fs, SourceRoot, "my-skill");

            SkillsInstaller installer = new(fs);
            installer.Install(SourceRoot, TargetRoot);

            Assert.IsTrue(fs.Directories.Any(d => d.Contains("my-skill")));
        }

        // ── Install: multiple skills ──────────────────────────────────────────

        [Test]
        public void Install_CopiesAllSkillFolders_WhenMultipleSkillsExist()
        {
            MockFileSystem fs = new();
            fs.Directories.Add(SourceRoot);
            AddSkillFolder(fs, SourceRoot, "skill-a");
            AddSkillFolder(fs, SourceRoot, "skill-b");
            AddSkillFolder(fs, SourceRoot, "skill-c");

            SkillsInstaller installer = new(fs);
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
            MockFileSystem fs = new();
            fs.Directories.Add(SourceRoot);
            AddSkillFolder(fs, SourceRoot, "my-skill", content);

            // Pre-populate target with identical content
            string targetSkillDir = $"{TargetRoot}/my-skill";
            fs.Directories.Add(targetSkillDir);
            fs.Files[$"{targetSkillDir}/SKILL.md"] = content;

            SkillsInstaller installer = new(fs);
            SkillsInstallResult result = installer.Install(SourceRoot, TargetRoot);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, result.FilesUpdated);
            Assert.IsFalse(result.AnyChanges);
            Assert.AreEqual(0, fs.CopiedFiles.Count);
        }

        [Test]
        public void Install_CopiesOnlyChangedFiles_WhenSomeHashesDiffer()
        {
            MockFileSystem fs = new();
            fs.Directories.Add(SourceRoot);
            AddSkillFolder(fs, SourceRoot, "skill-unchanged", "# same");
            AddSkillFolder(fs, SourceRoot, "skill-changed", "# new content");

            string targetUnchangedDir = $"{TargetRoot}/skill-unchanged";
            fs.Directories.Add(targetUnchangedDir);
            fs.Files[$"{targetUnchangedDir}/SKILL.md"] = "# same";

            string targetChangedDir = $"{TargetRoot}/skill-changed";
            fs.Directories.Add(targetChangedDir);
            fs.Files[$"{targetChangedDir}/SKILL.md"] = "# old content";

            SkillsInstaller installer = new(fs);
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
            MockFileSystem fs = new();
            fs.Directories.Add(SourceRoot);

            string skillDir = $"{SourceRoot}/deep-skill";
            string subDir = $"{skillDir}/examples";
            fs.Directories.Add(skillDir);
            fs.Directories.Add(subDir);
            fs.Files[$"{skillDir}/SKILL.md"] = "# root file";
            fs.Files[$"{subDir}/example.md"] = "# example";

            SkillsInstaller installer = new(fs);
            SkillsInstallResult result = installer.Install(SourceRoot, TargetRoot);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.FilesUpdated);
            Assert.IsTrue(fs.CopiedFiles.Any(f => f.Contains("SKILL.md")));
            Assert.IsTrue(fs.CopiedFiles.Any(f => f.Contains("example.md")));
        }

        [Test]
        public void RelocateInstalledSkills_InstallsIntoNewTarget_AndRemovesOldSkillFolders()
        {
            MockFileSystem fs = new();
            fs.Directories.Add(SourceRoot);
            AddSkillFolder(fs, SourceRoot, "my-skill", "# packaged");

            string previousTarget = "/home/user/.github/skills";
            string previousSkillDir = $"{previousTarget}/my-skill";
            fs.Directories.Add(previousSkillDir);
            fs.Files[$"{previousSkillDir}/SKILL.md"] = "# old target";

            SkillsInstaller installer = new(fs);

            bool result = installer.RelocateInstalledSkills(SourceRoot, previousTarget, TargetRoot);

            Assert.IsTrue(result);
            Assert.IsTrue(fs.Files.ContainsKey($"{TargetRoot}/my-skill/SKILL.md"));
            Assert.IsFalse(fs.Directories.Contains(previousSkillDir));
            Assert.IsFalse(fs.Files.ContainsKey($"{previousSkillDir}/SKILL.md"));
        }

        [Test]
        public void RelocateInstalledSkills_PreservesUnrelatedFiles_InOldTargetSkillFolder()
        {
            MockFileSystem fs = new();
            fs.Directories.Add(SourceRoot);
            AddSkillFolder(fs, SourceRoot, "my-skill", "# packaged");

            string previousTarget = "/home/user/.github/skills";
            string previousSkillDir = $"{previousTarget}/my-skill";
            fs.Directories.Add(previousSkillDir);
            fs.Files[$"{previousSkillDir}/SKILL.md"] = "# old target";
            fs.Files[$"{previousSkillDir}/notes.md"] = "# keep me";

            SkillsInstaller installer = new(fs);

            bool result = installer.RelocateInstalledSkills(SourceRoot, previousTarget, TargetRoot);

            Assert.IsTrue(result);
            Assert.IsTrue(fs.Files.ContainsKey($"{TargetRoot}/my-skill/SKILL.md"));
            Assert.IsTrue(fs.Directories.Contains(previousSkillDir));
            Assert.IsFalse(fs.Files.ContainsKey($"{previousSkillDir}/SKILL.md"));
            Assert.IsTrue(fs.Files.ContainsKey($"{previousSkillDir}/notes.md"));
        }

        // ── SkillsInstallResult.ToString ──────────────────────────────────────

        [Test]
        public void InstallResult_ToString_DescribesFailure()
        {
            SkillsInstallResult result = SkillsInstallResult.Failure("oops");

            StringAssert.Contains("Failed", result.ToString());
            StringAssert.Contains("oops", result.ToString());
        }

        [Test]
        public void InstallResult_ToString_DescribesUpToDate()
        {
            SkillsInstallResult result = new() { Success = true, FilesUpdated = 0 };

            StringAssert.Contains("up to date", result.ToString());
        }

        [Test]
        public void InstallResult_ToString_DescribesChanges()
        {
            SkillsInstallResult result = new()
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
