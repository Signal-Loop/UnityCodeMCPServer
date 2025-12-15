# Loop MCP STDIO Bridge

A Python package that bridges MCP (Model Context Protocol) over STDIO to a Unity TCP Server.

## Overview

This bridge enables MCP client to communicate with the LoopMcpServer running inside Unity Editor via TCP. The bridge:

1. Receives MCP messages via STDIO
2. Forwards them to the Unity TCP Server
3. Returns responses back via STDIO

## Prerequisites

- **Python 3.10+** - Required for the bridge
- **uv** - Fast Python package manager ([install uv](https://docs.astral.sh/uv/getting-started/installation/))
- **Unity Editor** - With LoopMcpServer running (auto-starts when Unity opens)

### Installing uv

**Windows (PowerShell):**

```powershell
powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
```

**macOS/Linux:**

```bash
curl -LsSf https://astral.sh/uv/install.sh | sh
```

## Installation

### Using uv (Recommended)

No installation needed! uv runs the package directly:

```bash
uv run --directory /path/to/STDIO~ loop-mcp-stdio --host localhost --port 21088
```

### Using pip (Alternative)

```bash
pip install -e /path/to/STDIO~
loop-mcp-stdio --host localhost --port 21088
```

## Usage

### Command Line Arguments

| Argument        | Default     | Description                          |
| --------------- | ----------- | ------------------------------------ |
| `--host`        | `localhost` | Unity TCP Server host                |
| `--port`        | `21088`     | Unity TCP Server port                |
| `--retry-time`  | `2`         | Seconds between connection retries   |
| `--retry-count` | `5`         | Maximum number of connection retries |

### Examples

```bash
# Basic usage (from STDIO directory)
uv run loop-mcp-stdio

# Run from any directory using --directory
uv run --directory "C:/path/to/STDIO~" loop-mcp-stdio

# Custom host and port
uv run --directory "C:/path/to/STDIO~" loop-mcp-stdio --host 127.0.0.1 --port 12345

# With retry configuration
uv run --directory "C:/path/to/STDIO~" loop-mcp-stdio --retry-time 3 --retry-count 10
```

## MCP Configuration

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

> **Note:** Replace `C:/Users/YOUR_USERNAME/path/to/...` with the actual path to your Unity project's STDIO folder.

## Architecture

```
┌─────────────────┐      TCP       ┌─────────────────┐     STDIO      ┌─────────────────┐
│                 │                │                 │                │                 │
│  Unity Editor   │ ◄────────────► │  STDIO Bridge   │ ◄────────────► │  MCP Client     │
│                 │                │                 │                │                 │
└─────────────────┘                └─────────────────┘                └─────────────────┘
```

### Communication Flow

1. **MCP Client → Bridge (STDIO):** MCP Client sends JSON-RPC 2.0 messages via stdin
2. **Bridge → Unity (TCP):** Bridge forwards messages to Unity Tcp Server
3. **Unity → Bridge (TCP):** Unity Tcp Server responds back to Bridge
4. **Bridge → MCP Client (STDIO):** Bridge writes response to stdout

## Development

### Running Tests

```bash
cd /path/to/STDIO

# On Windows, use the venv Python directly (avoids uv script canonicalization issues):
.\.venv\Scripts\python.exe -m pytest tests/ -v

# On macOS/Linux, uv run works directly:
uv run --extra dev pytest tests/
```

> **Windows Note:** If you encounter "Failed to canonicalize script path" errors with `uv run`, use the venv Python directly as shown above.

### Development Install

```bash
# Sync dependencies including dev extras
uv sync --extra dev

# Alternative: pip install
uv pip install -e ".[dev]"
```

## Testing with Postman

Postman supports MCP (Model Context Protocol) natively, including STDIO transport. You can use Postman to test and debug the STDIO Bridge.

### Prerequisites

- **Postman Desktop App** (v11.35+) - [Download here](https://www.postman.com/downloads/)
- **Unity Editor** running with LoopMcpServer active

### Step-by-Step Guide

1. **Open Postman** and create or select a workspace

2. **Create a new MCP request:**

   - Click **New** → **MCP**
   - Select **STDIO** as the transport type

3. **Configure the STDIO command:**

   ```
   uv run --directory "C:/Users/YOUR_USERNAME/path/to/Assets/Plugins/Loop4UnityMcpServer/Editor/STDIO" loop-mcp-stdio --host localhost --port 21088
   ```

   > **Tip:** You can also paste JSON configuration directly:
   >
   > ```json
   > {
   >   "command": "uv",
   >   "args": [
   >     "run",
   >     "--directory",
   >     "C:/Users/YOUR_USERNAME/path/to/Assets/Plugins/Loop4UnityMcpServer/Editor/STDIO~",
   >     "loop-mcp-stdio",
   >     "--host",
   >     "localhost",
   >     "--port",
   >     "21088"
   >   ]
   > }
   > ```

4. **Click "Connect"** - Postman will connect and discover available tools, prompts, and resources

### Reference

For more details, see the official Postman documentation:

- [Create MCP Requests](https://learning.postman.com/docs/postman-ai-developer-tools/mcp-requests/create/)
- [MCP Server Catalog](https://www.postman.com/explore/mcp-servers)

## License

MIT
