# Loop MCP Server for Unity

A powerful MCP server for the Unity Editor that gives AI Agents ability to perform any action using Unity Editor API. This includes scripts, scenes, prefabs and assets manipulation, configuration changes, and more.

## Real workflow example

User:

```
Add following feature to Unity using execute script tool:

- Read cities list from csv file #file:cities.csv
- Create CitySO ScriptableObject class with properties based on csv columns, place it in Assets/Scripts/ScriptableObjects
- Create assets for each city in the csv file, populate properties with data from the file, place them in Assets/ScriptableObjects
- Create CityComponent MonoBehaviour class with a reference to CitySO ScriptableObject and place it in Assets/Scripts
- Create Map root gameobject in the current scene
- For each city asset, create a City gameobject as a child of Map, set its position based on the coordinates from the asset, add CityComponent and assign the corresponding CitySO asset
- To each City gameObject add SpriteRenderer component and assign a default sprite from Assets/Images/City.png
```

Result:

```
Implemented the full “cities from CSV → assets → scene” pipeline.

- Added `CitySO` ScriptableObject: CitySO.cs
- Added `CityComponent` MonoBehaviour: CityComponent.cs
- Added the CSV data file: cities.csv
- Ran an Editor execution script that:
  - Created 49 `CitySO` assets in ScriptableObjects
  - Created a `Map` root GameObject in the active scene with 49 `City` children
  - Set each city position from `Coordinates`, added `CityComponent` and assigned the matching `CitySO`
  - Added `SpriteRenderer` to each city and assigned the default sprite from City.png
```

Full chat transcript: [ChatTranscript.md](Assets/Plugins/Loop4UnityMcpServer/Documentation~/Examples/ChatTranscript.md). Repository contains solution with result.

## Table of contents

- [Features](#features)
- [Tools](#tools)
- [Security considerations](#security-considerations)
- [Quick start](#quick-start)
- [Built-in tools](#built-in-tools)
- [Extending (adding tools)](#extending-adding-tools)
- [STDIO bridge](#stdio-bridge)
- [Testing](#testing)
- [Roadmap](#roadmap)
- [License](#license)

## Features

- **Unity Editor / Unity Engine API access**: Perform any tasks available through public APIs or reflection
- **STDIO transport (via bridge)**: No separate server process required for MCP clients. Domain-reload safe, retries if domain reload is in progress. `uv` (Python package manager) is required.
- **Streamable HTTP transport**: Alternative to STDIO bridge for MCP clients that support HTTP. No separate server process required. No uv required. Responds with error if domain reload is in progress.
- **Extensible**: Add new tools, async tools, resources, or prompts by implementing interfaces anywhere in the codebase

## Tools

### execute_csharp_script_in_unity_editor

Perform any task by executing generated C# scripts in Unity Editor context. Full access to UnityEngine, UnityEditor APIs, and reflection. Automatically captures logs, errors, and return values.

### read_unity_console_logs

Read Unity Editor Console logs with configurable entry limits (1-1000, default 200)

### run_unity_tests

Run Unity tests via TestRunnerApi. Supports EditMode, PlayMode, or both. Can run all tests or filter by fully qualified test names.

## Security considerations

This package executes LLM-generated C# code (including reflection code) with the same privileges as the Unity Editor process.

Recommendations:

- Review scripts before executing them.
- Use a separate Unity project and/or run Unity in an isolated environment (VM/container).

You are responsible for securing your environment and for any changes or data loss caused by executed scripts.

## Quick start

### Requirements

- Unity 2022.3 LTS (tested)
- UniTask (async/await integration): https://github.com/Cysharp/UniTask
- `uv` (Python package manager) for the STDIO bridge: https://docs.astral.sh/uv/

### Installation

Install as a Unity package via Git URL:

```
https://github.com/Signal-Loop/Loop4UnityMCPServer.git?path=Assets/Plugins/Loop4UnityMcpServer
```

### First Run

### MCP client configuration

1. Open your Unity project (the server auto-starts with the Editor).
2. In Unity, run: **Tools/LoopMcpServer/STDIO|HTTP/Print MCP Configuration to Console**.
3. Copy the printed MCP configuration into your MCP client.

#### STDIO

Example configuration (using `uv` to run the bridge):

```json
{
  "mcpServers": {
    "loop-unity-stdio": {
      "command": "uv",
      "args": [
        "run",
        "--directory",
        "C:/Users/tbory/source/Workspaces/Loop/Loop4UnityMCPServer/Assets/Plugins/Loop4UnityMcpServer/Editor/STDIO~",
        "loop-mcp-stdio",
        "--host",
        "localhost",
        "--port",
        "21088"
      ]
    }
  }
}
```

Replace `C:/Users/YOUR_USERNAME/path/to/...` with the actual path to your Unity project.

#### Streamable HTTP

```json
{
  "mcpServers": {
    "loop-unity-http": {
      "url": "http://127.0.0.1:3001/mcp/",
      "type": "http"
    }
  }
}
```

### Server configuration (Unity)

Access settings via **Tools/LoopMcpServer/Show Settings** or create manually:

1. Navigate to the `Assets/Resources/` (or any Resources folder) folder
2. Create the settings asset: **Right Click > Create > LoopMcpServer > Server Settings**
3. Configure options:
   - **Server Selection**: Choose STDIO (TCP) or HTTP server for auto-start
   - **Verbose Logging**: Enable detailed logging for debugging
   - **TCP Server**: Port (default: `21088`), backlog, timeouts
   - **HTTP Server**: Port (default: `3001`), session timeout, SSE keep-alive interval

### Menu commands

#### General

- **Tools/LoopMcpServer/Show Settings** — Open the server settings asset in the inspector

#### STDIO Server (TCP)

- **Tools/LoopMcpServer/STDIO/Refresh Registry** — Re-scan for new tools/prompts/resources
- **Tools/LoopMcpServer/STDIO/Restart Server** — Restart the TCP server
- **Tools/LoopMcpServer/STDIO/Print MCP configuration to console** — Log MCP client configuration for STDIO bridge

#### HTTP Server

- **Tools/LoopMcpServer/HTTP/Refresh Registry** — Re-scan for new tools/prompts/resources
- **Tools/LoopMcpServer/HTTP/Restart Server** — Restart the HTTP server
- **Tools/LoopMcpServer/HTTP/Log Server Status** — Display current HTTP server status
- **Tools/LoopMcpServer/HTTP/Print MCP configuration to console** — Log MCP client configuration for HTTP server

## Built-in tools

### execute_csharp_script_in_unity_editor

```
Use this tool to perform changes or automate tasks in Unity Editor by creating and executing C# scripts.
Scripts run in the Unity Editor context using Roslyn with full access to UnityEngine, UnityEditor, and any project assembly.
Perfect for creating GameObjects, modifying scenes, configuring components, or automating Unity Editor tasks.
Returns execution status, output, and any logs/errors.

**ALWAYS use `execute_csharp_script_in_unity_editor` tool for ANY Unity Editor modifications or automation tasks.**

**ALWAYS prefer `execute_csharp_script_in_unity_editor` tool to modification of Unity Yaml files.**

### When to Use This Tool (Use for ALL of these scenarios):
- Creating, modifying, or deleting GameObjects in scenes
- Adding, configuring, or removing Components
- Adjusting Transform properties (position, rotation, scale)
- Setting up UI elements and Canvas hierarchies
- Creating or modifying Prefabs
- Configuring ScriptableObject instances
- Scene management (creating, loading, switching scenes)
- Asset manipulation (importing, configuring, organizing, modifying)
- Batch operations on multiple GameObjects
- Editor window automation
- Project structure setup
- ANY task that modifies Unity Editor state

### Why This Tool is Required:
- **Direct execution**: Scripts run immediately in the Unity Editor context using Roslyn
- **Full API access**: Complete access to UnityEngine, UnityEditor, and all project assemblies
- **Immediate feedback**: Returns execution status, output, and logs instantly
- **Scene persistence**: Automatically marks scenes dirty after execution
- **Selection context**: Automatically captures current Unity Editor selection
```

### read_unity_console_logs

```
Reads Unity Editor Console logs. Returns recent log entries as text with an optional max_entries limit.
```

### run_unity_tests

```
Runs Unity tests using the TestRunnerApi. Can run all tests or specific tests by name.
Returns the test results including status and logs.
```

## Extending (adding tools)

Add Tools, Prompts, Resources, or Async Tools by implementing the relevant interfaces (ITool, IToolAsync, IPrompt, IResource) anywhere in your codebase. The server will automatically detect and register them.

### Synchronous tool

```csharp
using LoopMcpServer.Interfaces;
using LoopMcpServer.Protocol;
using Newtonsoft.Json.Linq;

public class MyTool : ITool
{
    public string Name => "my_tool";
    public string Description => "Description of my tool";

    public JObject InputSchema => JObject.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""param1"": { ""type"": ""string"" }
        },
        ""required"": [""param1""]
    }");

    public ToolsCallResult Execute(JObject arguments)
    {
        var param1 = arguments["param1"]?.ToString();
        return new ToolsCallResult
        {
            IsError = false,
            Content = new List<ContentItem> { ContentItem.TextContent($"Result: {param1}") }
        };
    }
}
```

### Asynchronous tool

```csharp
using Cysharp.Threading.Tasks;
using LoopMcpServer.Interfaces;
using Newtonsoft.Json.Linq;

public class MyAsyncTool : IToolAsync
{
    public string Name => "my_async_tool";
    public string Description => "An async tool";

    public JObject InputSchema => new JObject { ["type"] = "object" };

    public async UniTask<ToolsCallResult> ExecuteAsync(JObject arguments)
    {
        await UniTask.Delay(1000);
        return new ToolsCallResult { /* ... */ };
    }
}
```

### Available assemblies

The Script Execution Tool currently allows a fixed set of assemblies. Future versions may allow configuring this list.

- Assembly-CSharp
- Assembly-CSharp-Editor
- System.Core
- System.Text.Json
- Unity.InputSystem
- UnityEngine.CoreModule
- UnityEngine.Physics2DModule
- UnityEngine.TextRenderingModule
- UnityEngine.UI
- UnityEngine.UIElementsModule
- UnityEngine.UIModule
- UnityEditor.CoreModule
- UnityEngine.TestRunner
- UnityEditor.TestRunner
- UniTask

## STDIO bridge

See the bridge docs at [stdio.md](stdio.md).

## Testing

Unity tests are in `Tests/` and can be run via the Unity Test Runner.

## Roadmap

- Configurable list of available assemblies

## Known Issues

- Loop4Unity MCP Server includes dll files in its package. If those files are already present in your project, you may see GUID conflicts. In our test cases it does not cause any issues, but if you encounter problems, please fill issue: [Issues](https://github.com/Signal-Loop/Loop4UnityMCPServer/issues). Removing duplicate dlls from your project may resolve the conflicts.
```
GUID [eb9c83041c7a89c46bb6e20e7b4484df] for asset 'Packages/com.signal-loop.loop4unitymcpserver/Editor/Bin/Microsoft.CodeAnalysis.CSharp.dll' conflicts with:
  '[Path to dll file in your project]/Microsoft.CodeAnalysis.CSharp.dll' (current owner)
We can't assign a new GUID because the asset is in an immutable folder. The asset will be ignored.
```

## License

MIT
