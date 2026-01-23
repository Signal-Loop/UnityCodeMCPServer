# Unity Code MCP Server for Unity

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
- **STDIO transport (via bridge)**: No separate server process required for MCP clients. Domain-reload safe, retries if domain reload is in progress. `uv` (Python package manager) is required.
- **Streamable HTTP transport**: Alternative to STDIO bridge for MCP clients that support HTTP. No separate server process required. No uv required. Responds with error if domain reload is in progress.
- **Extensible**: Add new tools, async tools, resources, or prompts by implementing interfaces anywhere in the codebase

## Security considerations

This package executes LLM-generated C# code (including via reflection) with the same privileges as the Unity Editor process.

Recommendations:

- Review scripts before executing them.
- Use a separate Unity project and/or run Unity in an isolated environment (VM/container).

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
https://github.com/Signal-Loop/UnityCodeMCPServer.git?path=Assets/Plugins/UnityCodeMcpServer
```

## Quick start

1. Open your Unity project (the server auto-starts with the Editor).
2. In Unity, run: **Tools/UnityCodeMcpServer/Log MCP Configuration**.
3. Copy the printed MCP configuration into your MCP client.

### MCP client configuration

### STDIO

Example configuration (using `uv` to run the bridge):

```json
{
  "mcpServers": {
    "unity-code-mcp-stdio": {
      "command": "uv",
      "args": [
        "run",
        "--directory",
        "C:/path/to/UnityCodeMCPServer/Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~",
        "unity-code-mcp-stdio",
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
    "unity-code-mcp-http": {
      "url": "http://127.0.0.1:3001/mcp/",
      "type": "http"
    }
  }
}
```

### Server configuration (Unity)

Access settings via **Tools/UnityCodeMcpServer/Show Settings** or create manually:

1. Navigate to the `Assets/Resources/` folder
2. Create the settings asset: **Right Click > Create > UnityCodeMcpServer > Server Settings**
3. Configure options:
   - **Server Selection**: Choose STDIO (TCP) or HTTP server for auto-start
   - **Verbose Logging**: Enable detailed logging for debugging

- **TCP Server**: Port (default: `21088`), backlog, timeouts (changing the port auto-restarts the STDIO server when selected)
- **HTTP Server**: Port (default: `3001`), session timeout, SSE keep-alive interval

## Menu commands

### General

- **Tools/UnityCodeMcpServer/Show Settings** — Open the server settings asset in the inspector

### STDIO Server (TCP)

- **Tools/UnityCodeMcpServer/STDIO/Refresh Registry** — Re-scan for new tools/prompts/resources
- **Tools/UnityCodeMcpServer/STDIO/Restart Server** — Restart the TCP server
- **Tools/UnityCodeMcpServer/STDIO/Print MCP configuration to console** — Log MCP client configuration for STDIO bridge

### HTTP Server

- **Tools/UnityCodeMcpServer/HTTP/Refresh Registry** — Re-scan for new tools/prompts/resources
- **Tools/UnityCodeMcpServer/HTTP/Restart Server** — Restart the HTTP server
- **Tools/UnityCodeMcpServer/HTTP/Log Server Status** — Display current HTTP server status
- **Tools/UnityCodeMcpServer/HTTP/Print MCP configuration to console** — Log MCP client configuration for HTTP server

## Built-in tools and resources

### Tools

- **execute_csharp_script_in_unity_editor** — Execute C# scripts in Unity Editor context using Roslyn. Full access to UnityEngine, UnityEditor APIs, and reflection. Automatically captures logs, errors, and return values.
- **read_unity_console_logs** — Read Unity Editor Console logs with configurable entry limits (1-1000, default 200)
- **run_unity_tests** — Run Unity tests via TestRunnerApi. Supports EditMode, PlayMode, or both. Can run all tests or filter by fully qualified test names.

### Resources

- **unity://console/logs** — Unity Console Logs resource that provides access to Editor console logs via reflection

## Extending (adding tools)

Add Tools, Prompts, Resources, or Async Tools by implementing the relevant interfaces (ITool, IToolAsync, IPrompt, IResource) anywhere in your codebase. The server will automatically detect and register them.

### Synchronous tool

```csharp
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
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
using UnityCodeMcpServer.Interfaces;
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
- UnityEngine.TestRunner
- UnityEditor.TestRunner
- UniTask

## STDIO bridge

See the bridge docs at [stdio.md](stdio.md).

## Testing

Unity tests are in `Tests/` and can be run via the Unity Test Runner.

## Roadmap

- Configurable list of available assemblies

## License

MIT
