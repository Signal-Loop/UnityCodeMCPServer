# Random Tester Play Unity Game Sequence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a randomized tester operation that enters play mode, plays the Unity game for 200 ms, and exits play mode, with self-tests covering selection and sequencing.

**Architecture:** Keep the change inside `Dev/Python/mcp_random_tester.py` by modeling the new behavior as a named composite operation in `execute_operation()`. Reuse the existing MCP call helper so the new path inherits the current logging, timeout, and failure conventions.

**Tech Stack:** Python 3, `asyncio`, `unittest`, MCP Python client types

---

### Task 1: Add Failing Tests

**Files:**
- Modify: `Dev/Python/mcp_random_tester.py`
- Test: `Dev/Python/mcp_random_tester.py`

- [ ] **Step 1: Write the failing test**

```python
    def test_build_operation_sequence_can_include_play_unity_game_sequence(self) -> None:
        sequence = build_operation_sequence(200, 0)
        names = [operation.name for operation in sequence]

        self.assertIn("play_unity_game_sequence", names)

    def test_execute_operation_play_unity_game_sequence_calls_tools_in_order(self) -> None:
        class FakeSession:
            def __init__(self) -> None:
                self.calls: list[tuple[str, dict[str, Any] | None]] = []

            async def call_tool(self, tool_name: str, arguments: dict[str, Any] | None = None, read_timeout_seconds: Any = None) -> types.CallToolResult:
                self.calls.append((tool_name, arguments))
                return types.CallToolResult(
                    content=[types.TextContent(type="text", text=f"ok:{tool_name}")],
                    isError=False,
                )

        session = FakeSession()
        args = argparse.Namespace(
            console_log_limit=2,
            request_timeout_seconds=20.0,
            long_script_seconds=5,
            domain_reload_file=DEFAULT_DOMAIN_RELOAD_FILE,
            workspace_dir=WORKSPACE_ROOT,
        )

        success, payload = asyncio.run(
            execute_operation(session, Operation(name="play_unity_game_sequence"), args)
        )

        self.assertTrue(success)
        self.assertEqual(
            session.calls,
            [
                ("enter_play_mode", None),
                ("play_unity_game", {"duration": 200}),
                ("exit_play_mode", None),
            ],
        )
```

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run Dev/Python/mcp_random_tester.py --self-test`
Expected: FAIL because the new operation is not yet in `AVAILABLE_OPERATIONS` and `execute_operation()` does not yet handle it.

### Task 2: Implement Composite Operation

**Files:**
- Modify: `Dev/Python/mcp_random_tester.py`
- Test: `Dev/Python/mcp_random_tester.py`

- [ ] **Step 1: Write minimal implementation**

```python
TOOL_ENTER_PLAY_MODE = "enter_play_mode"
TOOL_PLAY_UNITY_GAME = "play_unity_game"
TOOL_EXIT_PLAY_MODE = "exit_play_mode"

AVAILABLE_OPERATIONS = (
    ...,
    "play_unity_game_sequence",
)
```

```python
    if operation.name == "play_unity_game_sequence":
        responses: list[dict[str, Any]] = []

        for tool_name, arguments in (
            (TOOL_ENTER_PLAY_MODE, None),
            (TOOL_PLAY_UNITY_GAME, {"duration": 200}),
            (TOOL_EXIT_PLAY_MODE, None),
        ):
            success, result = await call_tool_and_log(
                session,
                tool_name,
                arguments,
                args.request_timeout_seconds,
            )
            responses.append(
                {
                    "tool": tool_name,
                    "arguments": arguments,
                    "response": normalize_for_json(result),
                }
            )
            if not success:
                return False, {
                    "operation": operation.name,
                    "failed_tool": tool_name,
                    "responses": responses,
                }

        return True, {"operation": operation.name, "responses": responses}
```

- [ ] **Step 2: Run test to verify it passes**

Run: `uv run Dev/Python/mcp_random_tester.py --self-test`
Expected: PASS for the new tests.

### Task 3: Verify Full Script Suite

**Files:**
- Modify: `Dev/Python/mcp_random_tester.py`
- Test: `Dev/Python/mcp_random_tester.py`

- [ ] **Step 1: Run the full self-test suite**

Run: `uv run Dev/Python/mcp_random_tester.py --self-test`
Expected: all script self-tests pass.