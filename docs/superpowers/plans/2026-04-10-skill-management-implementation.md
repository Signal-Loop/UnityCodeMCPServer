# Skill Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically install and update bundled skills during package install/update, default to `.agents/skills/`, expose preset/custom target selection in settings, remove the manual install button, and cover the change with tests and README updates.

**Architecture:** Keep `PackageInit` as the lifecycle entry point, leave `PackageInstaller` and `SkillsInstaller` focused on their respective file sets, and make settings the source of truth for the resolved skills target. Execute the package bootstrap as a two-phase sync and refresh Unity assets only when either phase changes files.

**Tech Stack:** Unity Editor C#, NUnit edit-mode tests, existing installer/settings infrastructure.

---

### Task 1: Add failing tests for settings target selection and migration

**Files:**
- Modify: `Assets/Tests/EditMode/UnityCodeMcpServerSettingsTests.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Settings/UnityCodeMcpServerSettings.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void InitializeSkillsTarget_UsesAgentsPreset_WhenUnset()
{
    var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

    try
    {
        settings.InitializeSkillsTarget();

        Assert.That(settings.SkillsInstallTarget, Is.EqualTo(UnityCodeMcpServerSettings.SkillInstallTarget.Agents));
        StringAssert.Contains(".agents/skills", settings.SkillsTargetPath);
    }
    finally
    {
        ScriptableObject.DestroyImmediate(settings);
    }
}

[Test]
public void InitializeSkillsTarget_MigratesLegacyAbsolutePath_ToCustom()
{
    var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

    try
    {
        settings.SkillsTargetPath = "C:/tools/custom-skills";

        settings.InitializeSkillsTarget();

        Assert.That(settings.SkillsInstallTarget, Is.EqualTo(UnityCodeMcpServerSettings.SkillInstallTarget.Custom));
        Assert.That(settings.SkillsTargetPath, Is.EqualTo("C:/tools/custom-skills"));
    }
    finally
    {
        ScriptableObject.DestroyImmediate(settings);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `mcp_unity-code-mc_run_unity_tests` with `EditMode` and the two new test names.
Expected: FAIL because `InitializeSkillsTarget`, `SkillsInstallTarget`, or target resolution helpers do not exist yet.

- [ ] **Step 3: Write minimal implementation**

```csharp
public enum SkillInstallTarget
{
    GitHub,
    Claude,
    Agents,
    Custom
}

public SkillInstallTarget SkillsInstallTarget = SkillInstallTarget.Agents;

public void InitializeSkillsTarget()
{
    if (!string.IsNullOrWhiteSpace(SkillsTargetPath) && IsAbsolutePath(SkillsTargetPath))
    {
        SkillsInstallTarget = SkillInstallTarget.Custom;
        SkillsTargetPath = NormalizePath(SkillsTargetPath);
        return;
    }

    SkillsInstallTarget = SkillInstallTarget.Agents;
    SkillsTargetPath = ResolveSkillsTargetPath(SkillInstallTarget.Agents);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `mcp_unity-code-mc_run_unity_tests` with `EditMode` and the two new test names.
Expected: PASS.

### Task 2: Add failing tests for target path resolution helpers

**Files:**
- Modify: `Assets/Tests/EditMode/UnityCodeMcpServerSettingsTests.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Settings/UnityCodeMcpServerSettings.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void GetEffectiveSkillsTargetPath_ReturnsPresetPath()
{
    var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

    try
    {
        settings.SkillsInstallTarget = UnityCodeMcpServerSettings.SkillInstallTarget.GitHub;

        string path = settings.GetEffectiveSkillsTargetPath();

        StringAssert.EndsWith(".github/skills/", path.Replace('\\', '/'));
    }
    finally
    {
        ScriptableObject.DestroyImmediate(settings);
    }
}

[Test]
public void GetEffectiveSkillsTargetPath_ReturnsCustomPath()
{
    var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

    try
    {
        settings.SkillsInstallTarget = UnityCodeMcpServerSettings.SkillInstallTarget.Custom;
        settings.SkillsTargetPath = "C:/skills/custom";

        Assert.That(settings.GetEffectiveSkillsTargetPath(), Is.EqualTo("C:/skills/custom"));
    }
    finally
    {
        ScriptableObject.DestroyImmediate(settings);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `mcp_unity-code-mc_run_unity_tests` with `EditMode` and the two new test names.
Expected: FAIL because `GetEffectiveSkillsTargetPath` does not exist or returns incorrect values.

- [ ] **Step 3: Write minimal implementation**

```csharp
public string GetEffectiveSkillsTargetPath()
{
    InitializeSkillsTarget();

    switch (SkillsInstallTarget)
    {
        case SkillInstallTarget.GitHub:
            return ResolveSkillsTargetPath(SkillInstallTarget.GitHub);
        case SkillInstallTarget.Claude:
            return ResolveSkillsTargetPath(SkillInstallTarget.Claude);
        case SkillInstallTarget.Agents:
            return ResolveSkillsTargetPath(SkillInstallTarget.Agents);
        case SkillInstallTarget.Custom:
        default:
            return NormalizePath(SkillsTargetPath);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `mcp_unity-code-mc_run_unity_tests` with `EditMode` and the two new test names.
Expected: PASS.

### Task 3: Add failing tests for automatic skills install orchestration

**Files:**
- Modify: `Assets/Tests/EditMode/PackageInstallerTests.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Installer/PackageInstaller.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Installer/PackageInit.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Installer/SkillsInstaller.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void InstallContent_ReturnsTrue_WhenSkillsInstallerCopiesFiles()
{
    bool packageInstalled = false;
    bool skillsInstalled = false;

    bool result = PackageInstaller.InstallContent(
        installPackageFiles: () => packageInstalled,
        installSkills: () => { skillsInstalled = true; return true; });

    Assert.IsTrue(result);
    Assert.IsTrue(skillsInstalled);
}

[Test]
public void InstallContent_ReturnsFalse_WhenNothingChanges()
{
    bool result = PackageInstaller.InstallContent(
        installPackageFiles: () => false,
        installSkills: () => false);

    Assert.IsFalse(result);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `mcp_unity-code-mc_run_unity_tests` with `EditMode` and the two new test names.
Expected: FAIL because the combined orchestration helper does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
public static bool InstallContent(Func<bool> installPackageFiles, Func<bool> installSkills)
{
    bool packageChanged = installPackageFiles != null && installPackageFiles();
    bool skillsChanged = installSkills != null && installSkills();
    return packageChanged || skillsChanged;
}
```

Update `PackageInit` to use the helper when running both install phases.

- [ ] **Step 4: Run test to verify it passes**

Run: `mcp_unity-code-mc_run_unity_tests` with `EditMode` and the two new test names.
Expected: PASS.

### Task 4: Update the settings inspector UX

**Files:**
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Settings/UnityCodeMcpServerSettingsEditor.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Settings/UnityCodeMcpServerSettings.cs`

- [ ] **Step 1: Write the failing tests or helper-oriented assertions**

Add settings-level tests that prove switching the target enum updates the effective path for preset targets and preserves a custom path.

- [ ] **Step 2: Run test to verify it fails**

Run the focused settings test set.
Expected: FAIL because the setter/helper behavior does not exist yet.

- [ ] **Step 3: Write minimal implementation**

Update the inspector to:

- replace the quick-select buttons and manual install button with a dropdown
- show a label for the resolved target directory
- show folder picker UI only when `Custom` is selected
- update `SkillsTargetPath` through settings helpers when the dropdown changes

- [ ] **Step 4: Run test to verify it passes**

Run the focused settings test set.
Expected: PASS.

### Task 5: Preserve and verify changed-file-only behavior in skills installer

**Files:**
- Modify: `Assets/Tests/EditMode/SkillsInstallerTests.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Installer/SkillsInstaller.cs`

- [ ] **Step 1: Write the failing test**

Add a test that verifies the installer result reports no changes when all destination hashes match and reports changes only for differing files.

- [ ] **Step 2: Run test to verify it fails**

Run the focused skills installer tests.
Expected: FAIL only if behavior changed during the refactor; otherwise keep implementation unchanged.

- [ ] **Step 3: Write minimal implementation**

Only adjust `SkillsInstaller` if needed for the new automatic flow or path handling. Do not add deletion behavior.

- [ ] **Step 4: Run test to verify it passes**

Run the focused skills installer tests.
Expected: PASS.

### Task 6: Update README for the new automatic flow

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Write the documentation update**

Document:

- skills install automatically during package install/update
- `.agents/skills/` is the default first-run target
- settings allow switching among GitHub, Claude, Agents, or Custom
- folder selection UI only appears for Custom
- only new or changed files are copied

- [ ] **Step 2: Verify the README text is consistent with implementation**

Check the updated setup steps and agent skills section against the final code paths and setting names.

### Task 7: Full verification

**Files:**
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Installer/PackageInit.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Installer/PackageInstaller.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Installer/SkillsInstaller.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Settings/UnityCodeMcpServerSettings.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Settings/UnityCodeMcpServerSettingsEditor.cs`
- Modify: `Assets/Tests/EditMode/PackageInstallerTests.cs`
- Modify: `Assets/Tests/EditMode/SkillsInstallerTests.cs`
- Modify: `Assets/Tests/EditMode/UnityCodeMcpServerSettingsTests.cs`
- Modify: `README.md`

- [ ] **Step 1: Run focused edit-mode tests**

Run: `mcp_unity-code-mc_run_unity_tests` with:

- `UnityCodeMcpServer.Tests.EditMode.PackageInstallerTests`
- `UnityCodeMcpServer.Tests.EditMode.SkillsInstallerTests`
- `UnityCodeMcpServer.Tests.EditMode.UnityCodeMcpServerSettingsTests`

Expected: PASS.

- [ ] **Step 2: Read Unity console logs**

Run: `mcp_unity-code-mc_read_unity_console_logs`
Expected: no new compilation errors caused by the change.

- [ ] **Step 3: Verify requirements coverage**

Check each requested outcome against the final code and README changes before closing the task.