# Unity Same-Port Rebinding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Unity Streamable HTTP server transport so the configured HTTP port can be stopped and rebound on the same port during Unity-owned reloads on Windows.

**Architecture:** Keep `UnityCodeMcpHttpServer` as the lifecycle entry point, replace the underlying `HttpListener` transport with a loopback `TcpListener` transport, and refactor the HTTP request handler to operate on transport-agnostic request and response abstractions. Preserve the existing `/mcp/` endpoint behavior and bounded bridge retry behavior.

**Tech Stack:** Unity Editor C#, `TcpListener`/`TcpClient`, existing MCP registry and handler stack, NUnit EditMode tests, Python pytest bridge tests.

---

### Task 1: Add transport-facing server tests

**Files:**

- Modify: `Assets/Tests/EditMode/UnityCodeMcpHttpServerTests.cs`
- Test: `Assets/Tests/EditMode/UnityCodeMcpHttpServerTests.cs`

- [ ] **Step 1: Write failing tests for same-port transport state**

Add tests that assert:

- a dedicated loopback transport can start, stop, and start again on the same port
- `UnityCodeMcpHttpServer` exposes lifecycle state needed to prevent overlapping starts and stops

- [ ] **Step 2: Run the focused EditMode tests and verify failure**

Run: Unity EditMode tests for `UnityCodeMcpHttpServerTests`
Expected: FAIL because the new transport and lifecycle state do not exist yet.

- [ ] **Step 3: Implement the minimal lifecycle surface to satisfy the tests**

Create the transport type and add the minimum lifecycle state and public hooks required by the test.

- [ ] **Step 4: Re-run the focused EditMode tests**

Run: Unity EditMode tests for `UnityCodeMcpHttpServerTests`
Expected: PASS

### Task 2: Refactor request handling away from HttpListener

**Files:**

- Create: `Assets/Plugins/UnityCodeMcpServer/Editor/Servers/StreamableHttp/LoopbackHttpContext.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Servers/StreamableHttp/HttpRequestHandler.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Servers/StreamableHttp/SseStreamWriter.cs`
- Test: `Assets/Tests/EditMode/UnityCodeMcpHttpServerTests.cs`

- [ ] **Step 1: Write failing tests for handler behavior on transport-agnostic request/response types**

Cover:

- method rejection for non-POST
- empty body rejection
- notification returns 202
- JSON request returns JSON response

- [ ] **Step 2: Run focused EditMode tests and verify failure**

Run: Unity EditMode tests for the new handler-focused cases
Expected: FAIL because the handler still depends on `HttpListenerContext`.

- [ ] **Step 3: Implement transport-agnostic request and response abstractions and port the handler**

Add a narrow context abstraction and change the handler and SSE writer to depend on it.

- [ ] **Step 4: Re-run the focused EditMode tests**

Run: Unity EditMode tests for `UnityCodeMcpHttpServerTests`
Expected: PASS

### Task 3: Replace HttpListener transport with TcpListener transport

**Files:**

- Create: `Assets/Plugins/UnityCodeMcpServer/Editor/Servers/StreamableHttp/LoopbackHttpServerTransport.cs`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Servers/StreamableHttp/UnityCodeMcpHttpServer.cs`
- Test: `Assets/Tests/EditMode/UnityCodeMcpHttpServerTests.cs`

- [ ] **Step 1: Write failing tests for same-port restart without port drift**

Add tests that assert:

- restart does not allocate a new port
- stop and immediate restart on the same port succeeds
- overlapping starts are ignored or rejected cleanly

- [ ] **Step 2: Run the focused EditMode tests and verify failure**

Run: Unity EditMode tests for `UnityCodeMcpHttpServerTests`
Expected: FAIL because the server still wraps `HttpListener`.

- [ ] **Step 3: Implement the `TcpListener` transport and wire it into `UnityCodeMcpHttpServer`**

Use loopback-only binding, explicit socket close, bounded same-port retry for Unity-owned shutdown races, and preserve existing logs and menu actions.

- [ ] **Step 4: Re-run the focused EditMode tests**

Run: Unity EditMode tests for `UnityCodeMcpHttpServerTests`
Expected: PASS

### Task 4: Preserve settings semantics around reload and port changes

**Files:**

- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/Settings/UnityCodeMcpServerSettings.cs`
- Test: `Assets/Tests/EditMode/UnityCodeMcpServerSettingsTests.cs`

- [ ] **Step 1: Write failing tests for deferred reload startup and explicit port change restart behavior**

Cover:

- first deserialize after reload does not start HTTP
- explicit HTTP port changes still restart HTTP when HTTP is selected

- [ ] **Step 2: Run focused settings tests and verify failure if behavior regressed**

Run: Unity EditMode tests for `UnityCodeMcpServerSettingsTests`
Expected: FAIL only if the transport refactor broke existing startup deferral behavior.

- [ ] **Step 3: Apply the minimal settings changes needed to preserve behavior**

Keep the startup deferral behavior while ensuring controlled restarts still work with the new transport.

- [ ] **Step 4: Re-run focused settings tests**

Run: Unity EditMode tests for `UnityCodeMcpServerSettingsTests`
Expected: PASS

### Task 5: Verify bridge compatibility and document the result

**Files:**

- Modify: `README_STDIO.md`
- Test: `Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~/tests/test_bridge_over_http.py`

- [ ] **Step 1: Add or update a failing bridge test only if transport changes require new expectations**

Keep the bridge pointed at the configured Unity port and verify no fallback-port behavior is introduced for Unity-owned reloads.

- [ ] **Step 2: Run focused Python bridge tests**

Run: `pytest Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~/tests/test_bridge_over_http.py -q`
Expected: PASS

- [ ] **Step 3: Update docs for same-port recovery behavior**

Document that Unity now prefers deterministic same-port recovery and does not require manual port changes for Unity-owned reload conflicts.

- [ ] **Step 4: Run final targeted verification**

Run the focused Unity EditMode tests for HTTP server and settings plus the focused Python bridge tests.
Expected: PASS
