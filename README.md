# Loop MCP Server for Unity

A Model Context Protocol (MCP) server for the Unity Editor that enables AI assistants to perform **almost** anything in the Editor by executing C# scripts via UnityEngine/UnityEditor APIs (including reflection). This includes scene manipulation, asset management, configuration changes, and more.

## Table of contents

- [Features](#features)
- [Security considerations](#security-considerations)
- [Architecture](#architecture)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Menu commands](#menu-commands)
- [Extending (adding tools)](#extending-adding-tools)
- [Available assemblies](#available-assemblies)
- [STDIO bridge](#stdio-bridge)
- [Testing](#testing)
- [Roadmap](#roadmap)
- [License](#license)

## Features

- **Unity Editor / Unity Engine API access**: Perform tasks available through public APIs or reflection
- **Auto-start**: Server starts automatically with the Unity Editor
- **STDIO transport (via bridge)**: No separate server process required for MCP clients
- **Domain-reload safe**: Handles Unity domain reloads gracefully
- **Extensible**: Add new tools, async tools, resources, or prompts by implementing interfaces anywhere in the codebase

## Security considerations

This package executes LLM-generated C# code (including via reflection) with the same privileges as the Unity Editor process. If your MCP client/LLM is susceptible to **prompt injection** (for example: reading untrusted web pages or using untrusted MCP servers), it may generate destructive or exfiltrating code.

Recommendations:

- Review scripts before executing them.
- Use a separate Unity project and/or run Unity in an isolated environment (VM/container) for higher-risk workflows.

You are responsible for securing your environment and for any changes or data loss caused by executed scripts.

## Architecture

```
┌─────────────────┐      TCP      ┌─────────────────┐     STDIO      ┌─────────────┐
│  Unity Server   │ ◄───────────► │  STDIO Bridge   │ ◄────────────► │  MCP Client │
│  (this package) │               │ (Python script) │                │             │
└─────────────────┘               └─────────────────┘                └─────────────┘
```

## Requirements

- Unity 2022.3 LTS (tested)
- UniTask (async/await integration): https://github.com/Cysharp/UniTask
- `uv` (Python package manager) for the STDIO bridge: https://docs.astral.sh/uv/

## Installation

Install as a Unity package via Git URL:

```
https://github.com/Signal-Loop/Loop4UnityMCPServer.git?path=Assets/Plugins/Loop4UnityMcpServer
```

## Quick start

1. Open your Unity project (the server auto-starts with the Editor).
2. In Unity, run: **Tools/LoopMcpServer/Log MCP Configuration**.
3. Copy the printed MCP configuration into your MCP client.

### MCP client configuration

Example configuration (using `uv` to run the bridge):

```json
{
  "mcpServers": {
    "unity": {
      "command": "uv",
      "args": [
        "run",
        "--directory",
        "C:/Users/YOUR_USERNAME/path/to/Assets/Plugins/Loop4UnityMcpServer/Editor/STDIO~",
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

### Server configuration (Unity)

1. Navigate to the `Resources/` folder in the package.
2. Create the settings asset: **Right Click > Create > LoopMcpServer > Server Settings**
3. Configure the port (default: `21088`) and other options as needed.

## Menu commands

- **Tools/LoopMcpServer/Refresh Registry** — Re-scan for new tools/prompts/resources
- **Tools/LoopMcpServer/Restart Server** — Restart the TCP server
- **Tools/LoopMcpServer/Log MCP Configuration** — Log MCP client configuration to the Unity Console

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

## Available assemblies

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

## STDIO bridge

See the bridge docs at [stdio.md](stdio.md).

## Testing

Unity tests are in `Tests/` and can be run via the Unity Test Runner.

## Roadmap

- Tools for reading Unity Console logs and running tests
- Configurable list of available assemblies

## License

MIT
