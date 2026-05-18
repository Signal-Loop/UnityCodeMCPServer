# FileServer Transport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a file-based Unity transport and matching Python stdio bridge that preserve the existing MCP-facing behavior while routing requests through `.unityCodeMcpServer/messages`.

**Architecture:** Mirror the current Python bridge surface with a new file-backed client and keep the Unity side thin by reusing `McpMessageHandler` after reading raw JSON-RPC request files. Preserve FIFO ordering on disk by having the Unity side re-scan the directory for the oldest pending request whenever it becomes idle, with a file watcher used only as the idle wake-up signal.

**Tech Stack:** Python 3.10+, pytest, anyio, Unity Editor C#, NUnit, Cysharp UniTask, System.IO.FileSystemWatcher, System.Text.Json.

---

### Task 1: Add Python file-bridge tests

**Files:**
- Create: `Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~/tests/test_bridge_over_file.py`
- Reference: `Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio.py`

- [ ] **Step 1: Write failing tests for file naming, request/response matching, and timeout cleanup**

```python
import json

import pytest

from unity_code_mcp_stdio.unity_code_mcp_stdio import (
    DEFAULT_FILE_REQUEST_TIMEOUT,
    FileBridgePaths,
    UnityFileClient,
)


def test_default_file_request_timeout_is_180_seconds():
    assert DEFAULT_FILE_REQUEST_TIMEOUT == 180.0


def test_build_request_path_uses_timestamp_and_client_id(tmp_path):
    paths = FileBridgePaths(project_root=tmp_path, client_id="client-123")

    request_path = paths.build_request_path("20260504123456789")

    assert request_path.name == "20260504123456789_request_client-123.json"
    assert request_path.parent == tmp_path / ".unityCodeMcpServer" / "messages"


@pytest.mark.asyncio
async def test_send_request_writes_request_and_returns_matching_response(tmp_path):
    client = UnityFileClient(project_root=tmp_path, client_id="client-123", request_timeout=1.0)
    request = {"jsonrpc": "2.0", "id": "tools", "method": "tools/list", "params": {}}

    async def fake_wait(request_path, response_path):
        response_path.write_text(
            json.dumps({"jsonrpc": "2.0", "id": "tools", "result": {"tools": []}}),
            encoding="utf-8",
        )

    client._wait_for_response = fake_wait

    response = await client.send_request(request)

    assert response["result"] == {"tools": []}
    assert not any(paths.name.endswith("request_client-123.json") for paths in client.paths.messages_dir.iterdir())


@pytest.mark.asyncio
async def test_send_request_returns_timeout_error_and_removes_request_file(tmp_path):
    client = UnityFileClient(project_root=tmp_path, client_id="client-123", request_timeout=0.01)
    request = {"jsonrpc": "2.0", "id": "tools", "method": "tools/list", "params": {}}

    async def fake_wait(request_path, response_path):
        raise TimeoutError("timed out")

    client._wait_for_response = fake_wait

    response = await client.send_request(request)

    assert response["error"]["message"].startswith("Timed out waiting for Unity file response")
    assert list(client.paths.messages_dir.glob("*_request_client-123.json")) == []
```

- [ ] **Step 2: Run the targeted Python tests to verify they fail**

Run: `cd Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~; .\.venv\Scripts\python.exe -m pytest tests/test_bridge_over_file.py -v`
Expected: FAIL because `unity_code_mcp_stdio` and its file transport types do not exist yet.

### Task 2: Implement the Python file-backed bridge

**Files:**
- Create: `Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~/src/unity_code_mcp_stdio/unity_code_mcp_stdio.py`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~/src/unity_code_mcp_stdio/__init__.py`
- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~/pyproject.toml`
- Test: `Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~/tests/test_bridge_over_file.py`

- [ ] **Step 1: Implement the new file bridge module with the same MCP server surface as the HTTP bridge**

```python
DEFAULT_FILE_REQUEST_TIMEOUT = 180.0


class FileBridgePaths:
    def __init__(self, project_root: Path, client_id: str):
        self.project_root = Path(project_root)
        self.client_id = client_id
        self.messages_dir = self.project_root / ".unityCodeMcpServer" / "messages"

    def ensure_messages_dir(self) -> Path:
        self.messages_dir.mkdir(parents=True, exist_ok=True)
        return self.messages_dir

    def build_request_path(self, timestamp: str) -> Path:
        return self.messages_dir / f"{timestamp}_request_{self.client_id}.json"

    def build_response_path(self, timestamp: str) -> Path:
        return self.messages_dir / f"{timestamp}_response_{self.client_id}.json"
```

```python
class UnityFileClient:
    async def send_request(self, request_payload: dict[str, Any]) -> dict[str, Any]:
        async with self._lock:
            timestamp = _timestamp_now()
            self.paths.ensure_messages_dir()
            request_path = self.paths.build_request_path(timestamp)
            response_path = self.paths.build_response_path(timestamp)
            request_path.write_text(json.dumps(request_payload), encoding="utf-8")
            try:
                await self._wait_for_response(request_path, response_path)
                payload = json.loads(response_path.read_text(encoding="utf-8"))
                request_path.unlink(missing_ok=True)
                response_path.unlink(missing_ok=True)
                return payload
            except TimeoutError:
                request_path.unlink(missing_ok=True)
                return self._build_error(
                    request_payload,
                    REQUEST_UNAVAILABLE_ERROR_CODE,
                    "Timed out waiting for Unity file response",
                )
```

- [ ] **Step 2: Export a dedicated CLI entry point**

```toml
[project.scripts]
unity-code-mcp-stdio = "unity_code_mcp_stdio:main"
unity-code-mcp-stdio = "unity_code_mcp_stdio:main"
```

- [ ] **Step 3: Run the targeted Python tests to verify they pass**

Run: `cd Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~; .\.venv\Scripts\python.exe -m pytest tests/test_bridge_over_file.py -v`
Expected: PASS with 4 passing tests.

### Task 3: Add Unity request-selection tests first

**Files:**
- Create: `Assets/Tests/EditMode/FileServer/FileServerRequestStoreTests.cs`
- Reference: `Assets/Plugins/UnityCodeMcpServer/Editor/Protocol/McpMessages.cs`

- [ ] **Step 1: Write failing tests for FIFO selection and startup recovery filtering**

```csharp
[Test]
public void TryGetNextPendingRequest_SelectsOldestRequestWithoutResponse()
{
    // create three request files, create a response for the oldest one, expect the next-oldest request
}

[Test]
public void TryGetNextPendingRequest_ReevaluatesDirectoryAfterNewerFilesAppear()
{
    // select oldest request, then create a new file, then expect next call to rescan and pick the correct oldest remaining file
}
```

- [ ] **Step 2: Run the narrow Unity tests to verify they fail**

Run: Unity EditMode tests for `UnityCodeMcpServer.Tests.EditMode.FileServerRequestStoreTests`
Expected: FAIL because the request-store class does not exist yet.

### Task 4: Implement Unity request selection and file metadata helpers

**Files:**
- Create: `Assets/Plugins/UnityCodeMcpServer/Editor/FileServer/FileServerRequestStore.cs`
- Test: `Assets/Tests/EditMode/FileServer/FileServerRequestStoreTests.cs`

- [ ] **Step 1: Implement a small helper that owns directory creation and next-request selection**

```csharp
internal sealed class FileServerRequestStore
{
    public bool TryGetNextPendingRequest(out FileServerRequestFile request)
    {
        foreach (string path in Directory.GetFiles(_messagesDirectory, "*_request_*.json").OrderBy(Path.GetFileName))
        {
            if (HasMatchingResponse(path))
            {
                continue;
            }

            request = FileServerRequestFile.Parse(path);
            return true;
        }

        request = default;
        return false;
    }
}
```

- [ ] **Step 2: Run the narrow Unity tests to verify they pass**

Run: Unity EditMode tests for `UnityCodeMcpServer.Tests.EditMode.FileServerRequestStoreTests`
Expected: PASS.

### Task 5: Add Unity FileServer lifecycle tests first

**Files:**
- Create: `Assets/Tests/EditMode/FileServer/UnityCodeMcpFileServerTests.cs`
- Reference: `Assets/Plugins/UnityCodeMcpServer/Editor/HttpServer/UnityCodeMcpHttpServer.cs`

- [ ] **Step 1: Write failing tests for server startup path, watcher presence, and response writing through `McpMessageHandler`**

```csharp
[Test]
public void StartServer_CreatesMessagesDirectory()
{
    // start against temp project path and assert directory exists
}

[Test]
public void ProcessNextRequestAsync_WritesJsonRpcResponseFile()
{
    // write tools/list request file, run one processing pass, assert response file contains JSON-RPC result
}
```

- [ ] **Step 2: Run the narrow Unity tests to verify they fail**

Run: Unity EditMode tests for `UnityCodeMcpServer.Tests.EditMode.UnityCodeMcpFileServerTests`
Expected: FAIL because the server class does not exist yet.

### Task 6: Implement Unity FileServer transport

**Files:**
- Create: `Assets/Plugins/UnityCodeMcpServer/Editor/FileServer/UnityCodeMcpFileServer.cs`
- Test: `Assets/Tests/EditMode/FileServer/UnityCodeMcpFileServerTests.cs`

- [ ] **Step 1: Implement the server with watcher-driven idle wake-up and stateless rescanning**

```csharp
[InitializeOnLoad]
public static class UnityCodeMcpFileServer
{
    private static FileSystemWatcher _watcher;
    private static FileServerRequestStore _requestStore;
    private static McpMessageHandler _messageHandler;
    private static int _processing;

    private static void OnWatcherChanged(object sender, FileSystemEventArgs args)
    {
        TryProcessNextRequest().Forget();
    }

    internal static async UniTask TryProcessNextRequest()
    {
        if (Interlocked.Exchange(ref _processing, 1) == 1)
        {
            return;
        }

        try
        {
            while (_requestStore.TryGetNextPendingRequest(out FileServerRequestFile request))
            {
                string requestJson = File.ReadAllText(request.RequestPath);
                string responseJson = await _messageHandler.ProcessMessageAsync(requestJson);
                if (responseJson != null)
                {
                    File.WriteAllText(request.ResponsePath, responseJson);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _processing, 0);
        }
    }
}
```

- [ ] **Step 2: Run the narrow Unity tests to verify they pass**

Run: Unity EditMode tests for `UnityCodeMcpServer.Tests.EditMode.FileServerRequestStoreTests` and `UnityCodeMcpServer.Tests.EditMode.UnityCodeMcpFileServerTests`
Expected: PASS.

### Task 7: Run focused end-to-end verification

**Files:**
- Test: `Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~/tests/test_bridge_over_file.py`
- Test: `Assets/Tests/EditMode/FileServer/FileServerRequestStoreTests.cs`
- Test: `Assets/Tests/EditMode/FileServer/UnityCodeMcpFileServerTests.cs`

- [ ] **Step 1: Run Python bridge tests**

Run: `cd Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~; .\.venv\Scripts\python.exe -m pytest tests/test_bridge_over_file.py -v`
Expected: PASS.

- [ ] **Step 2: Run Unity EditMode tests for the FileServer slice**

Run: Unity EditMode tests for `UnityCodeMcpServer.Tests.EditMode.FileServerRequestStoreTests` and `UnityCodeMcpServer.Tests.EditMode.UnityCodeMcpFileServerTests`
Expected: PASS.

- [ ] **Step 3: Re-check Unity console logs for compile/runtime regressions**

Run: Unity console log read after the test run.
Expected: no fresh compile errors introduced by the FileServer changes.
