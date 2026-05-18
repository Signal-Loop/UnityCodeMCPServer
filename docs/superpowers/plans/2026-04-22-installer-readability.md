# Installer Readability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `PackageInit` a thin coordinator, move STDIO install policy into `PackageInstaller`, and keep `SkillsInstaller` focused on skills operations.

**Architecture:** `PackageInit` should only coordinate installer execution and combine results. `PackageInstaller` should own STDIO-specific path resolution, skip behavior, and file copy execution. `SkillsInstaller` should continue to own skill installation and relocation details without leaking those details into `PackageInit`.

**Tech Stack:** Unity Editor C#, NUnit EditMode tests, existing `IFileSystem` abstraction

---

### Task 1: Replace callback-based coordinator tests with installer-boundary tests

**Files:**

- Modify: `Assets/Tests/EditMode/PackageInstallerTests.cs`
- Test: `Assets/Tests/EditMode/PackageInstallerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void Install_ReturnsFalse_WhenSourceAndTargetAreTheSame()
{
    MockFileSystem mock_fs = new();
    string source = "Packages/MyPkg/STDIO~";

    mock_fs.Directories.Add(source);

    PackageInstaller installer = new(mock_fs);

    bool result = installer.Install(source, source);

    Assert.IsFalse(result);
    Assert.AreEqual(0, mock_fs.CopiedFiles.Count);
}

[Test]
public void CombineInstallResults_ReturnsTrue_WhenEitherInstallerChangesFiles()
{
    bool result = PackageInit.CombineInstallResults(packageFilesChanged: false, skillsChanged: true);

    Assert.IsTrue(result);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test UnityCodeMcpServerTestsEditMode.csproj --filter "Install_ReturnsFalse_WhenSourceAndTargetAreTheSame|CombineInstallResults_ReturnsTrue_WhenEitherInstallerChangesFiles"`
Expected: FAIL because `PackageInstaller.Install` still copies when source equals target and `PackageInit.CombineInstallResults` does not exist yet.

- [ ] **Step 3: Write minimal implementation**

```csharp
public bool Install(string sourcePath, string targetPath)
{
    if (PathsMatch(sourcePath, targetPath))
    {
        UnityCodeMcpServerLogger.Debug($"{Protocol.McpProtocol.LogPrefix} Source and target are the same, skipping STDIO installation.");
        return false;
    }

    // existing install logic
}

internal static bool CombineInstallResults(bool packageFilesChanged, bool skillsChanged)
{
    return packageFilesChanged || skillsChanged;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test UnityCodeMcpServerTestsEditMode.csproj --filter "Install_ReturnsFalse_WhenSourceAndTargetAreTheSame|CombineInstallResults_ReturnsTrue_WhenEitherInstallerChangesFiles"`
Expected: PASS

### Task 2: Simplify PackageInit into a thin coordinator

**Files:**

- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Installer/PackageInit.cs`
- Test: `Assets/Tests/EditMode/PackageInstallerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void CombineInstallResults_ReturnsFalse_WhenNothingChanges()
{
    bool result = PackageInit.CombineInstallResults(packageFilesChanged: false, skillsChanged: false);

    Assert.IsFalse(result);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test UnityCodeMcpServerTestsEditMode.csproj --filter "CombineInstallResults_ReturnsFalse_WhenNothingChanges"`
Expected: FAIL until the helper exists and is used by the coordinator.

- [ ] **Step 3: Write minimal implementation**

```csharp
bool package_files_changed = packageInstaller.Install(packageRoot);
bool skills_changed = InstallSkills(fileSystem);
bool any_changes = CombineInstallResults(package_files_changed, skills_changed);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test UnityCodeMcpServerTestsEditMode.csproj --filter "CombineInstallResults_ReturnsFalse_WhenNothingChanges"`
Expected: PASS

### Task 3: Remove orchestration helper from PackageInstaller and preserve package-copy behavior

**Files:**

- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Installer/PackageInstaller.cs`
- Modify: `Assets/Tests/EditMode/PackageInstallerTests.cs`
- Test: `Assets/Tests/EditMode/PackageInstallerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void Install_CopiesSpecificFiles_WhenTargetDiffers()
{
    MockFileSystem mock_fs = new();
    string source = "Packages/MyPkg/STDIO~";
    string target = "Assets/Plugins/MyPkg/STDIO~";

    mock_fs.Directories.Add(source);
    mock_fs.Files.Add(source + "/src/unity_code_mcp_stdio/__init__.py", "init");
    mock_fs.Files.Add(source + "/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio.py", "python code");
    mock_fs.Files.Add(source + "/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio_over_http.py", "http python code");
    mock_fs.Files.Add(source + "/pyproject.toml", "toml content");
    mock_fs.Files.Add(source + "/uv.lock", "lock content");

    PackageInstaller installer = new(mock_fs);

    bool result = installer.Install(source, target);

    Assert.IsTrue(result);
    Assert.AreEqual(5, mock_fs.CopiedFiles.Count);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test UnityCodeMcpServerTestsEditMode.csproj --filter "Install_CopiesSpecificFiles_WhenTargetDiffers"`
Expected: PASS before refactor, then re-run after removing helper to confirm behavior stays green.

- [ ] **Step 3: Write minimal implementation**

```csharp
// Delete InstallContent and keep PackageInstaller limited to package installation responsibilities.
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test UnityCodeMcpServerTestsEditMode.csproj --filter "PackageInstallerTests"`
Expected: PASS

### Task 4: Verify Unity-facing behavior remains healthy

**Files:**

- Verify: `Assets/Plugins/UnityCodeMcpServer/Editor/Installer/PackageInit.cs`
- Verify: `Assets/Plugins/UnityCodeMcpServer/Editor/Installer/PackageInstaller.cs`
- Verify: `Assets/Plugins/UnityCodeMcpServer/Editor/Installer/SkillsInstaller.cs`

- [ ] **Step 1: Run EditMode tests for touched installer coverage**

Run: `dotnet test UnityCodeMcpServerTestsEditMode.csproj --filter "PackageInstallerTests|SkillsInstallerTests"`
Expected: PASS

- [ ] **Step 2: Check Unity console for compilation issues**

Run the Unity console log reader and confirm there are no new compilation errors related to the installer changes.

- [ ] **Step 3: Summarize the final boundary**

Confirm that `PackageInit` coordinates installers, `PackageInstaller` owns STDIO install decisions, and `SkillsInstaller` owns skills operations.
