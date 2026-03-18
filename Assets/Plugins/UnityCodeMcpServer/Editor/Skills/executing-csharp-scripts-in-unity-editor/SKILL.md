---
name: executing-csharp-scripts-in-unity-editor
description: Use this skill always when you need to use execute_csharp_script_in_unity_editor tool to modify scenes, add, remove or modify game objects, components, scriptable objects or perform any other task in Unity Editor using C# scripts. Also covers when and how to use read_unity_console_logs and run_unity_tests tools as part of the same workflow.
---

# Executing C# Scripts in Unity Editor

## Table of Contents

1. Available Tools
2. Core Principles
3. Forbidden Patterns
4. Usage Workflow
5. Debugging Loop
6. Script Context and APIs
7. Common Scripting Patterns

---

## Available Tools

This skill coordinates three tools. Use them together as a pipeline — never in isolation.

| Tool                                    | Purpose                                                 | When to Use                                                                                                                                                                                        |
| --------------------------------------- | ------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `execute_csharp_script_in_unity_editor` | Executes C# code in the live Unity Editor (Roslyn)      | Modifying scenes, GameObjects, Components, Prefabs, ScriptableObjects, or running Editor automation. Script output and logs are returned directly in the tool result.                              |
| `read_unity_console_logs`               | Reads recent entries from the Unity Editor Console      | **Before** executing a script, to check whether prior C# file edits have caused compilation errors that would prevent the script from running. Also useful when debugging unexpected Editor state. |
| `run_unity_tests`                       | Runs EditMode or PlayMode Unity tests via TestRunnerApi | After editing C# source files to verify logic correctness; **never** to run ad-hoc code and **never** after `execute_csharp_script_in_unity_editor` (scripts do not change compiled code)          |

---

## Core Principles

- **Top-Level Statements Only:** Write flat code. Do **not** wrap code in a class, method, or `[MenuItem]`. Provide only the sequence of statements and any required `using` directives.
- **Pre-Imported Namespaces:** `System`, `System.Collections.Generic`, `System.Linq`, `UnityEngine`, `UnityEditor` are always available. Do **not** redeclare them with `using`.
- **Explicit Usings for Others:** Declare any namespace not in the pre-imported list (e.g., `using UnityEngine.UI;`).
- **Idempotency:** Scripts must be safe to rerun. Check for existence before creating or adding to avoid duplicates. Use helper patterns like `GetOrAddComponent` and `CreateOrGetGameObject`.
- **Synchronous Only:** Do **not** use `async`/`await`, `Task`, or `Task.Run`. All Unity Editor APIs are main-thread-only and synchronous.
- **Null Checks:** Always use `== null` for Unity objects. Do **not** use `??` or `?.` — Unity objects override the `==` operator in ways that break null-conditional operators.
- **Specificity:** Prefer fully qualified names (e.g., `UnityEngine.GameObject`, `UnityEditor.AssetDatabase`) to avoid ambiguity.
- **Script Feedback:** Use `UnityEngine.Debug.Log()` to report what the script did. Use `UnityEngine.Debug.LogError()` for failures. The tool captures both.
- **Clarity & Error Handling:** Comment non-obvious logic. Wrap risky operations in `try-catch` and log errors with context.
- **Object class ambiguity:** Always use `UnityEngine.Object` when referring to Unity objects to avoid confusion with `System.Object`.

---

## Forbidden Patterns

**CRITICAL:** Never simulate an action or fabricate output if the API is unavailable.

- **Do NOT** log messages claiming to "simulate" or "pretend" a modification was made.
- **Do NOT** create placeholder logic that does nothing and logs fake success.
- **Do NOT** use `execute_csharp_script_in_unity_editor` to create or edit C# source files — use file editing tools instead.
- **Do NOT** use `execute_csharp_script_in_unity_editor` to read/write plain text, JSON, or YAML files — use file tools instead.
- **Do NOT** use `execute_csharp_script_in_unity_editor` to run tests — use `run_unity_tests` instead.
- **Do NOT** use background threads (`Thread`, `Task.Run`) — all Unity Editor APIs require the main thread.
- **Do NOT** use `async`/`await` inside scripts.

---

## Usage Workflow

Execute these steps in strict order for every task:

1. **Analyze Requirements:** Understand what needs to be created, modified, or queried. Identify target GameObjects, Components, assets, and expected outcome.
2. **Analyze Unity Context:** Check scene or prefab hierarchy, asset structure, and existing components using `execute_csharp_script_in_unity_editor` to determine how to achieve the goal. Identify potential edge cases (e.g., object already exists, missing dependencies).
3. **Plan the Script:** Determine which Unity APIs are needed. Verify the approach handles edge cases (object not found, component already exists, asset path incorrect).
4. **Check for Compilation Errors:** Call `read_unity_console_logs` (max_entries: 20) to confirm the project compiles cleanly. Prior C# file edits may have broken the build, which would cause the script to fail. Do not proceed if compilation errors are present — fix them first.
5. **Write the Script:** Apply all Core Principles. Add idempotency guards, null checks, `Debug.Log` for each significant action, and `try-catch` for risky operations.
6. **Execute the Script:** Call `execute_csharp_script_in_unity_editor`. All `Debug.Log` and `Debug.LogError` output is returned directly in the tool result — no separate log read is needed.
7. **Fix and Retry:** If the result contains errors, apply the Debugging Loop before reporting completion.

---

## Debugging Loop

When a script execution returns errors or unexpected results:

1. Read the error message and stack trace returned directly in the tool result.
2. If the error suggests a compilation failure (e.g., type not found, missing member), call `read_unity_console_logs` (max_entries: 30) to identify the source C# file causing the build error, fix it, then re-execute.
3. Otherwise, identify the root cause from the script output: missing object, bad asset path, wrong API usage, or null reference.
4. Fix the script and re-execute.
5. Repeat until the tool result confirms success with no errors.

---

## Script Context and APIs

### Pre-Imported Namespaces (do NOT add `using` for these)

- `System`
- `System.Collections.Generic`
- `System.Linq`
- `UnityEngine`
- `UnityEditor`

### Commonly Needed Explicit Usings

```csharp
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using TMPro;
```

### Loaded Assemblies

Only loaded assemblies are available.

#### Assemblies included by default:

- System.Core,
- UnityEditor.CoreModule,
- UnityEngine.CoreModule,
- Assembly-CSharp,
- Assembly-CSharp-Editor

#### Loading Additional Assemblies:

Additional assemblies are defined in `AdditionalAssemblyNames` list in `Assets/Plugins/UnityCodeMcpServer/Editor/UnityCodeMcpServerSettings.asset`. Add any assembly name there (e.g., "MyCustomAssembly") and it will be loaded and available in the script context.

#### Forcing Settings Reload After File Edit:

After editing `UnityCodeMcpServerSettings.asset` directly via file tools, the new assemblies are **not** available until Unity reprocesses the asset. After the file edit, execute this script to force a reload before using the new assemblies:

```csharp
var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Plugins/UnityCodeMcpServer/Editor/UnityCodeMcpServerSettings.asset");
EditorUtility.SetDirty(settings);
AssetDatabase.SaveAssets();
Debug.Log("Settings reloaded — new assemblies now available");
```

### Key API Reference

| Task                         | API                                                                                       |
| ---------------------------- | ----------------------------------------------------------------------------------------- |
| Find active scene GameObject | `UnityEngine.GameObject.Find("Name")`                                                     |
| Find all objects of type     | `UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None)`                       |
| Load asset from path         | `UnityEditor.AssetDatabase.LoadAssetAtPath<T>("Assets/...")`                              |
| Save all dirty assets        | `UnityEditor.AssetDatabase.SaveAssets()`                                                  |
| Refresh asset database       | `UnityEditor.AssetDatabase.Refresh()`                                                     |
| Mark object dirty            | `UnityEditor.EditorUtility.SetDirty(obj)`                                                 |
| Open scene                   | `UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MyScene.unity")` |
| Save open scene              | `UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes()`                         |

---

## Common Scripting Patterns

### Safe Get-or-Add Component

```csharp
T GetOrAddComponent<T>(UnityEngine.GameObject go) where T : UnityEngine.Component {
    var c = go.GetComponent<T>();
    if (c == null) c = go.AddComponent<T>();
    return c;
}
```

### Safe Create-or-Get GameObject

```csharp
UnityEngine.GameObject CreateOrGetGameObject(string name, UnityEngine.Transform parent = null) {
    var existing = UnityEngine.GameObject.Find(name);
    if (existing != null) return existing;
    var go = new UnityEngine.GameObject(name);
    if (parent != null) go.transform.SetParent(parent, false);
    return go;
}
```

### Creating and Parenting Objects

```csharp
var parent = UnityEngine.GameObject.Find("Canvas");
if (parent == null) { UnityEngine.Debug.LogError("Canvas not found"); return; }

var panel = CreateOrGetGameObject("HUD_Panel", parent.transform);
using UnityEngine.UI;
var image = GetOrAddComponent<Image>(panel);
image.color = new UnityEngine.Color(0f, 0f, 0f, 0.5f);
UnityEngine.Debug.Log("HUD_Panel configured");
```

### Modifying Prefab Assets

**CRITICAL:** Always use the Load → Modify → Save → Unload cycle. Never edit a prefab in-scene to affect the asset.

```csharp
string path = "Assets/Prefabs/MyPrefab.prefab";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
if (root == null) { UnityEngine.Debug.LogError("Prefab not found: " + path); return; }

try {
    var rb = GetOrAddComponent<UnityEngine.Rigidbody2D>(root);
    rb.gravityScale = 0f;
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
    UnityEngine.Debug.Log("Prefab saved: " + path);
} finally {
    UnityEditor.PrefabUtility.UnloadPrefabContents(root);
}
```

### Querying Scene State

```csharp
var allObjects = UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(UnityEngine.FindObjectsSortMode.None);
foreach (var go in allObjects) {
    UnityEngine.Debug.Log($"{go.name} | Layer: {UnityEngine.LayerMask.LayerToName(go.layer)} | Active: {go.activeSelf}");
}
```

### Creating a ScriptableObject Asset

```csharp
var so = UnityEngine.ScriptableObject.CreateInstance<MyScriptableObjectType>();
so.someField = 42;
UnityEditor.AssetDatabase.CreateAsset(so, "Assets/Data/MyAsset.asset");
UnityEditor.AssetDatabase.SaveAssets();
UnityEngine.Debug.Log("ScriptableObject created at Assets/Data/MyAsset.asset");
```

### Deleting a GameObject

Store the name before destroying, since the object becomes invalid immediately after.

```csharp
var target = UnityEngine.GameObject.Find("ToDestroy");
if (target != null) {
    string name = target.name;
    UnityEngine.Object.DestroyImmediate(target);
    UnityEngine.Debug.Log("Destroyed: " + name);
} else {
    UnityEngine.Debug.Log("Object not found — nothing to destroy");
}
```

### Missing Namespace or Assembly Errors

When encountering errors about missing types or namespaces, like `error CS0234: The type or namespace name 'UI' does not exist in the namespace 'UnityEngine' (are you missing an assembly reference?)`:

1. Identify the required assembly and namespace for the API you are trying to use.
2. Check if that assembly is included in the loaded assemblies or `AdditionalAssemblyNames` in `UnityCodeMcpServerSettings.asset`.
3. If it is not, add it to `AdditionalAssemblyNames` in `UnityCodeMcpServerSettings.asset` via file tools.
4. **Force a settings reload** by executing the reload script from the _Forcing Settings Reload After File Edit_ section above. The new assembly will not be available until this is done.
5. Ensure you have the correct `using` directive for the namespace at the top of your script.
