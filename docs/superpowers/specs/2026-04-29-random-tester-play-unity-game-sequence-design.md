# Random Tester Play Unity Game Sequence Design

## Summary

Extend the randomized MCP tester in `Dev/Python/mcp_random_tester.py` with one new composite operation that exercises the Unity gameplay flow by calling three existing MCP tools in order: `enter_play_mode`, `play_unity_game`, and `exit_play_mode`.

## Goals

- Randomly include a gameplay-oriented operation in generated test sequences.
- Keep the new behavior local to the existing Python script.
- Preserve the current logging and failure handling style.
- Add self-tests that verify both random inclusion eligibility and exact tool-call sequencing.

## Non-Goals

- No changes to Unity C# MCP tool implementations.
- No new CLI flags or configuration surface.
- No attempt to recover from domain reload or transport interruptions beyond current script behavior.

## Design

### New Operation

Add a new operation name to `AVAILABLE_OPERATIONS`, for example `play_unity_game_sequence`. The random sequence builder will then naturally include it with the same weighting model as the other operations.

### Execution Behavior

Handle the new operation inside `execute_operation()` as a composite flow:

1. Call `enter_play_mode` with no arguments.
2. Call `play_unity_game` with `{"duration": 200}`.
3. Call `exit_play_mode` with no arguments.

Each step should use the existing `call_tool_and_log()` helper so request and response logging remains consistent. If any step fails, stop the sequence early and return a response payload that contains the partial results collected so far.

### Tests

Add script self-tests that cover:

- the new operation appearing in the allowed operation set used by `build_operation_sequence()`
- the composite operation invoking the three MCP tools in the correct order with the correct arguments

The sequencing test should avoid a real MCP session by using a lightweight fake session object and an async test helper.

## Error Handling

The composite operation should treat any failed tool call as a failed operation. It should return a structured payload that identifies the step that failed and the results captured before the failure.

## Verification

- Run the new self-tests first and confirm they fail before implementation.
- Implement the minimal production change.
- Re-run the same self-tests and then the full script self-test suite.