# DLL Lock Issue Reproduction Tools

This directory contains Python scripts to diagnose and reproduce the DLL locking issue that occurs when Unity's MCP server loads assemblies via Roslyn during a rebuild.

## Status: Fixed

The DLL locking issue has been resolved by switching from file-based metadata references to in-memory byte array references in the `ExecuteCSharpScriptInUnityEditor`.

## The Problem (Fixed)

When using the `execute_csharp_script_in_unity_editor` MCP tool:

1. The tool uses Roslyn to execute C# scripts
2. Roslyn loaded project assemblies (Assembly-CSharp.dll, etc.) into memory via file paths
3. If Unity attempted to rebuild while these DLLs were loaded by Roslyn, the copy operation failed
4. Error: `Library\ScriptAssemblies\Assembly-CSharp.dll: Copying the file failed: The process cannot access the file because it is being used by another process.`

## The Solution

The issue was fixed in [Assets/Plugins/UnityCodeMcpServer/Editor/Tools/ExecuteCSharpScriptInUnityEditorTool.cs](../../Assets/Plugins/UnityCodeMcpServer/Editor/Tools/ExecuteCSharpScriptInUnityEditorTool.cs) by:

- Reading assembly DLLs directly into a byte array.
- Creating `MetadataReference` from the image (bytes) rather than the file path.
- This ensures that Roslyn does not hold an OS-level file lock on the DLL files during or after script execution, allowing Unity to overwrite them freely during recompilation.

## Prerequisites

- Unity Editor running with the MCP server enabled
- Python 3.12+ with `uv` package manager
- The workspace must contain `Assets/Scripts/DllLock/DllLock.cs`

## Usage

### Fast Reproduction

```powershell
# Run from this directory
uv run reproduce_dll_lock --steps focus,wait,wait,timestamp,execute,rebuild,timestamp,rebuild
```

This sequence:

1.  **focus**: Focuses the Unity window.
2.  **wait**: Gives Unity time to process input.
3.  **timestamp**: Updates a comment in `DllLock.cs` to trigger a rebuild (without changing logic).
4.  **execute**: Calls the MCP tool (loads DLLs).
5.  **rebuild**: Forces a compilation refresh via MCP.

### Available Steps

Steps can be combined in any order via `--steps`:

- `focus`: Focus the Unity Editor window (using `pygetwindow`).
- `wait`: Pause execution for the duration of `--delay`.
- `modify`: Change `linearVelocity` to `velocity` in `DllLock.cs` (triggers API Updater).
- `timestamp`: Add/update a `// dll-lock-timestamp` comment in `DllLock.cs`.
- `rebuild`: Call `CompilationPipeline.RequestScriptCompilation()` via MCP.
- `execute`: Execute a `Debug.Log` script via MCP (loads assemblies into Roslyn).

### Options

| Option            | Description                                                        |
| :---------------- | :----------------------------------------------------------------- |
| `--steps STEPS`   | Comma-separated steps (default: `modify,rebuild,wait,execute`).    |
| `--delay SECONDS` | Wait duration for the `wait` step (default: `0.5`).                |
| `--revert`        | When using `modify`, revert code changes instead of applying them. |
| `--host HOST`     | Unity MCP Server host (default: `localhost`).                      |
| `--port PORT`     | Unity MCP Server port (default: `21088`).                          |
| `--timestamp`     | Shortcut for `--steps timestamp` (updates comment only).           |
| `--modify-only`   | Shortcut for `--steps modify` (file change only).                  |
| `--rebuild-only`  | Shortcut for `--steps rebuild` (trigger compilation only).         |
| `--no-modify`     | Shortcut for `--steps execute` (run script only).                  |

### Examples

```powershell
# Cleanup sequence to confirm error is gone
uv run reproduce_dll_lock --steps focus,wait,timestamp,rebuild

# Complex sequence to pressure tests
uv run reproduce_dll_lock --steps focus,timestamp,execute,timestamp,execute,rebuild
```

### Revert Changes

Revert `DllLock.cs` to its original state:

```powershell
uv run reproduce_dll_lock --revert --modify-only
```

## Observed Behavior

After the fix, the DLL lock error is no longer produced during recompilation cycles even if tools are executing.

## Root Cause Analysis (Resolved)

The issue was in `ExecuteCSharpScriptInUnityEditorTool.cs`:

- Previously, `ResolveAssemblies()` returned `Assembly` locations that Roslyn used to create file-backed references.
- These references prevented the DLLs from being released until the script state was cleared or the process ended.
- Unity was unable to write to the DLL files because of these locks.

## Implemented Solution

- **In-memory metadata references**: Assemblies are read into memory once and passed as byte images to Roslyn. This releases the file handle immediately, preventing any conflicts with Unity's build system.
