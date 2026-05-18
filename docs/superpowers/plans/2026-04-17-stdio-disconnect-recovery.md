# STDIO Disconnect Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Python STDIO bridge fail fast and recover cleanly when Unity drops the TCP connection mid-request.

**Architecture:** Keep the existing bridge structure, but tighten the Unity TCP client state machine so a broken established connection fails the current request immediately and leaves the next request to reconnect. Also treat normal stream shutdown as non-fatal during bridge teardown.

**Tech Stack:** Python 3.10+, asyncio, anyio, pytest, pytest-asyncio, MCP Python SDK.

---

### Task 1: Lock In Reproduction With Tests

**Files:**

- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~/tests/test_bridge.py`

- [ ] **Step 1: Write the failing test**

```python
@pytest.mark.asyncio
async def test_send_request_fails_fast_after_established_connection_breaks(self):
    client = UnityTcpClient(
        host="localhost",
        port=21088,
        retry_time=0.0,
        retry_count=4,
    )

    client.reader = AsyncMock(spec=MockStreamReader)
    client.writer = AsyncMock(spec=MockStreamWriter)
    client.writer.drain.side_effect = ConnectionResetError("Reset")

    response = await client.send_request({"jsonrpc": "2.0", "id": "test", "method": "ping"})

    assert response["error"]["code"] == -32001
    assert "Unity connection dropped" in response["error"]["message"]
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~ ; .\.venv\Scripts\python.exe -m pytest tests/test_bridge.py -k fails_fast_after_established_connection_breaks -v`
Expected: FAIL because the bridge currently retries and returns the existing generic retry-exhausted error.

- [ ] **Step 3: Write the second failing test**

```python
@pytest.mark.asyncio
async def test_send_request_reconnects_on_next_request_after_drop(self):
    client = UnityTcpClient(
        host="localhost",
        port=21088,
        retry_time=0.0,
        retry_count=2,
    )

    client.reader = AsyncMock(spec=MockStreamReader)
    client.writer = AsyncMock(spec=MockStreamWriter)
    client.writer.drain.side_effect = ConnectionResetError("Reset")

    first = await client.send_request({"jsonrpc": "2.0", "id": "first", "method": "ping"})

    mock_reader = MockStreamReader()
    mock_reader.set_response({"jsonrpc": "2.0", "id": "second", "result": {}})
    mock_writer = MockStreamWriter()

    with patch("asyncio.open_connection", new_callable=AsyncMock) as mock_open:
        mock_open.return_value = (mock_reader, mock_writer)
        second = await client.send_request({"jsonrpc": "2.0", "id": "second", "method": "ping"})

    assert first["error"]["code"] == -32001
    assert second["result"] == {}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `cd Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~ ; .\.venv\Scripts\python.exe -m pytest tests/test_bridge.py -k "fails_fast_after_established_connection_breaks or reconnects_on_next_request_after_drop" -v`
Expected: FAIL with current retry behavior.

### Task 2: Implement Fail-Fast Disconnect Recovery

**Files:**

- Modify: `Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~/src/unity_code_mcp_stdio/unity_code_mcp_bridge_stdio.py`

- [ ] **Step 1: Add minimal implementation**

```python
# Track whether a request failure happened after a previously established TCP session.
# If so, disconnect and return a deterministic upstream disconnect error immediately.
```

- [ ] **Step 2: Keep next-request reconnect behavior intact**

```python
# After fail-fast error handling, leave reader/writer cleared so the next request
# uses the existing connect() path.
```

- [ ] **Step 3: Suppress expected closed-stream shutdown errors**

```python
# Treat anyio.ClosedResourceError during shutdown / writer closure as expected
# teardown rather than an unhandled server failure.
```

- [ ] **Step 4: Run focused tests**

Run: `cd Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~ ; .\.venv\Scripts\python.exe -m pytest tests/test_bridge.py -k "fails_fast_after_established_connection_breaks or reconnects_on_next_request_after_drop" -v`
Expected: PASS.

### Task 3: Verify Full Regression Surface

**Files:**

- Modify: `README_STDIO.md`

- [ ] **Step 1: Document behavior briefly**

```markdown
On Unity TCP disconnect, the current request now fails fast and the next request establishes a fresh TCP connection.
```

- [ ] **Step 2: Run full Python bridge tests**

Run: `cd Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~ ; .\.venv\Scripts\python.exe -m pytest tests/test_bridge.py -v`
Expected: PASS.

- [ ] **Step 3: Read Unity console logs**

Run the Unity console log reader and confirm there are no new compilation errors caused by this change.

- [ ] **Step 4: Report verification evidence**

Capture the focused tests, full bridge test run, and Unity console status in the final summary.
