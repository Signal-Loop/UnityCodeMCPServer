# Unity Same-Port Rebinding Design

**Goal:** Keep the Unity HTTP MCP endpoint on one configured port across assembly reloads by replacing the current `HttpListener` transport with a loopback socket transport that can stop and rebind the same port deterministically on Windows.

## Problem

The current Streamable HTTP server uses `HttpListener`. In the observed Windows reload failure mode, shutting down the Unity-owned listener does not reliably make the same port reusable, even after waiting. When that happens, the bridge blocks on the dead port until its request timeout expires, and the only manual recovery is changing Unity to a different port. That breaks the goal of keeping the MCP client working without operator intervention.

## Constraints

- Solve only Unity-owned reload conflicts on the configured port.
- The bridge may block while Unity reloads, but only within bounded request timeout and retry windows.
- The configured HTTP port remains canonical and should not auto-increment for Unity-owned reload failures.
- Existing MCP endpoint behavior stays at `http://127.0.0.1:<port>/mcp/`.

## Decision

Replace `HttpListener` with a loopback-only socket transport built on `TcpListener` and lightweight HTTP parsing/writing for the existing MCP POST flow.

## Why

- `HttpListener` delegates binding behavior to HTTP.sys, which is the likely reason the same port remains unavailable in the failing reload path.
- A socket-based transport gives direct control over bind and close semantics.
- The current request-processing surface is narrow: one POST endpoint, JSON body, JSON or 202 response, and existing SSE helper code. This keeps the transport replacement bounded.

## Architecture

### Transport Layer

Add a focused transport class under `Editor/Servers/StreamableHttp/` responsible for:

- binding `127.0.0.1:<configured port>` with `TcpListener`
- accepting client sockets
- parsing one HTTP request per accepted connection
- writing HTTP responses directly to the network stream
- shutting down cleanly on editor quit, restart, and assembly reload

This transport exposes request and response abstractions so the higher-level handler no longer depends on `HttpListenerContext`.

### Request Handling Layer

Refactor `HttpRequestHandler` to operate on custom request/response abstractions instead of `HttpListenerRequest` and `HttpListenerResponse`.

Supported behavior remains:

- only `POST /mcp/`
- Accept validation for JSON or SSE
- Origin validation against localhost loopback origins
- JSON-RPC body read and dispatch through `McpMessageHandler`
- `202 Accepted` for notifications
- `200 application/json` for ordinary responses

### Lifecycle

`UnityCodeMcpHttpServer` remains the lifecycle entry point. It will:

- create the registry and handlers
- create the new socket transport
- start and stop the transport on editor quit, restart, and assembly reload
- preserve the current logging and menu surface

### Settings Behavior

`UnityCodeMcpServerSettings.OnValidate` must not start HTTP on the first deserialize after a reload. Real operator port changes still trigger controlled restarts. Reload-time deserialization remains deferred to avoid racing shutdown.

### Bridge Behavior

The Python bridge keeps targeting the configured Unity port. For Unity-owned reload interruptions, it blocks only within bounded timeout and retry windows. No fallback port logic is added for this scenario because the design goal is same-port recovery.

## Failure Handling

- If the port is unavailable because Unity is still shutting down its own socket, the Unity server retries the same configured port for a short bounded window.
- If that window expires, Unity logs the server as unavailable on the configured port instead of silently changing ports.
- Requests arriving while Unity is unavailable are handled by the existing bridge timeout and retry budget.

## Testing

- transport test proving stop then immediate start reuses the same port
- server lifecycle tests proving same-port restarts are attempted and no port drift is introduced
- settings tests proving reload-time validation does not eagerly start HTTP
- existing Python bridge tests remain green

## Non-Goals

- Handling unrelated external processes that truly own the configured port
- Adding automatic fallback to a different HTTP port for Unity-owned reload conflicts
- Changing the public MCP endpoint path or bridge protocol
