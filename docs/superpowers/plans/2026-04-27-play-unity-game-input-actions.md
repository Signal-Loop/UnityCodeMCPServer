# Play Unity Game Input Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hardcoded play tool input asset with per-call path-based resolution driven by settings and fallbacks.

**Architecture:** Persist only a normalized asset path string on `UnityCodeMcpServerSettings`, expose it in the custom inspector, and resolve/load the `InputActionAsset` inside `PlayUnityGameTool` on every call. Keep the search policy local to the tool so fallback warnings and runtime behavior stay in one place.

**Tech Stack:** Unity Editor C#, `AssetDatabase`, `InputActionAsset`, NUnit edit-mode/play-mode tests.

---

### Task 1: Add failing settings tests

**Files:**
- Modify: `Assets/Tests/EditMode/UnityCodeMcpServerSettingsTests.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Settings/UnityCodeMcpServerSettings.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void SetInputActionsAssetPath_NormalizesBackslashes()
{
    UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

    try
    {
        settings.SetInputActionsAssetPath("Assets\\Input\\Game.inputactions");

        Assert.That(settings.InputActionsAssetPath, Is.EqualTo("Assets/Input/Game.inputactions"));
    }
    finally
    {
        ScriptableObject.DestroyImmediate(settings);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test UnityCodeMcpServerTestsEditMode.csproj --filter SetInputActionsAssetPath_NormalizesBackslashes`
Expected: FAIL because `InputActionsAssetPath` and `SetInputActionsAssetPath` do not exist yet.

- [ ] **Step 3: Write minimal implementation**

```csharp
[Header("Input Actions")]
[Tooltip("AssetDatabase path to the InputActionAsset used by play_unity_game.")]
public string InputActionsAssetPath = string.Empty;

public void SetInputActionsAssetPath(string path)
{
    InputActionsAssetPath = string.IsNullOrWhiteSpace(path) ? string.Empty : NormalizePath(path.Trim());
    EditorUtility.SetDirty(this);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test UnityCodeMcpServerTestsEditMode.csproj --filter SetInputActionsAssetPath_NormalizesBackslashes`
Expected: PASS

### Task 2: Add failing play tool resolution tests

**Files:**
- Modify: `Assets/Tests/PlayMode/PlayUnityGameToolTests.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/McpTools/PlayUnityGameTool.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void ResolveInputActionAssetPath_UsesConfiguredSettingsPathFirst()
{
    string configuredPath = "Assets/Tests/TestConfigured.inputactions";

    string resolvedPath = PlayUnityGameTool.ResolveInputActionAssetPath(
        configuredPath,
        new[] { "Assets/Tests/Fallback.inputactions" },
        new[] { "Packages/pkg/Fallback.inputactions" },
        out bool usedFallback,
        out _);

    Assert.That(resolvedPath, Is.EqualTo(configuredPath));
    Assert.That(usedFallback, Is.False);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test UnityCodeMcpServer.Tests.PlayMode.csproj --filter ResolveInputActionAssetPath_UsesConfiguredSettingsPathFirst`
Expected: FAIL because `ResolveInputActionAssetPath` does not exist yet.

- [ ] **Step 3: Write minimal implementation**

```csharp
internal static string ResolveInputActionAssetPath(
    string configuredPath,
    IReadOnlyList<string> projectAssetPaths,
    IReadOnlyList<string> allAssetPaths,
    out bool usedFallback,
    out string warningMessage)
{
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        usedFallback = false;
        warningMessage = null;
        return configuredPath;
    }

    if (projectAssetPaths.Count > 0)
    {
        usedFallback = true;
        warningMessage = $"Input actions are not set in settings. Using project InputActionAsset: {projectAssetPaths[0]}";
        return projectAssetPaths[0];
    }

    if (allAssetPaths.Count > 0)
    {
        usedFallback = true;
        warningMessage = $"Input actions are not set in settings. Using fallback InputActionAsset: {allAssetPaths[0]}";
        return allAssetPaths[0];
    }

    usedFallback = false;
    warningMessage = null;
    return null;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test UnityCodeMcpServer.Tests.PlayMode.csproj --filter ResolveInputActionAssetPath_UsesConfiguredSettingsPathFirst`
Expected: PASS

### Task 3: Wire runtime resolution into the tool and inspector

**Files:**
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/McpTools/PlayUnityGameTool.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Settings/UnityCodeMcpServerSettingsEditor.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Settings/UnityCodeMcpServerSettings.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void ResolveInputActionAssetPath_UsesFirstProjectAssetWhenSettingsUnset()
{
    string resolvedPath = PlayUnityGameTool.ResolveInputActionAssetPath(
        string.Empty,
        new[] { "Assets/Inputs/A.inputactions", "Assets/Inputs/B.inputactions" },
        new[] { "Packages/pkg/C.inputactions" },
        out bool usedFallback,
        out string warningMessage);

    Assert.That(resolvedPath, Is.EqualTo("Assets/Inputs/A.inputactions"));
    Assert.That(usedFallback, Is.True);
    Assert.That(warningMessage, Does.Contain("Assets/Inputs/A.inputactions"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test UnityCodeMcpServer.Tests.PlayMode.csproj --filter ResolveInputActionAssetPath_UsesFirstProjectAssetWhenSettingsUnset`
Expected: FAIL until fallback ordering and warning text are implemented.

- [ ] **Step 3: Write minimal implementation**

```csharp
UnityCodeMcpServerSettings settings = UnityCodeMcpServerSettings.Instance;
InputActionAsset inputAsset = LoadInputActionAsset(settings, out string resolvedPath, out string warningMessage);

if (!string.IsNullOrEmpty(warningMessage))
{
    Debug.LogWarning($"{Protocol.McpProtocol.LogPrefix} {warningMessage}");
}

if (inputAsset == null)
{
    return ToolsCallResult.ErrorResult("Could not find any InputActionAsset for play_unity_game.");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test UnityCodeMcpServer.Tests.PlayMode.csproj --filter ResolveInputActionAssetPath_UsesFirstProjectAssetWhenSettingsUnset`
Expected: PASS

### Task 4: Run focused regressions

**Files:**
- Verify only

- [ ] **Step 1: Run edit-mode tests for touched settings behavior**

Run: `dotnet test UnityCodeMcpServerTestsEditMode.csproj --filter InputActionsAssetPath|SetInputActionsAssetPath`
Expected: PASS

- [ ] **Step 2: Run play-mode tests for touched play tool behavior**

Run: `dotnet test UnityCodeMcpServer.Tests.PlayMode.csproj --filter ResolveInputActionAssetPath`
Expected: PASS

- [ ] **Step 3: Run a broader compile-oriented check**

Run: `dotnet test UnityCodeMcpServer.csproj`
Expected: build completes without introducing compile errors in touched code.
