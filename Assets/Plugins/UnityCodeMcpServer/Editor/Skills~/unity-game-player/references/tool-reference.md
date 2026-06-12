# Tool Reference

Detailed API reference for all tools used by the Unity Game Player skill.

## Table of Contents

1. enter_play_mode
2. play_unity_game
3. execute_csharp_script_in_unity_editor
4. read_unity_console_logs
5. exit_play_mode
6. get_unity_info

---

## 1. enter_play_mode

Enters Unity Play Mode and pauses the game (`Time.timeScale = 0`).

**Parameters:** None.

**Behavior:**

- Sets `EditorApplication.isPlaying = true`.
- Sets `Time.timeScale = 0` so game is frozen until `play_unity_game` is called.
- Call exactly once at the start of a session.

**Returns:** Confirmation text.

---

## 2. play_unity_game

The primary gameplay tool. Unpauses for a specified duration, simulates inputs, captures console logs, then re-pauses.

### Parameters

| Parameter    | Type    | Required | Default | Description                                         |
| ------------ | ------- | -------- | ------- | --------------------------------------------------- |
| `duration`   | integer | Yes      | —       | Milliseconds to run. `0` = instant return. |
| `input`      | array   | No       | `[]`    | Input actions to simulate.                          |

### Input array items

| Field    | Type   | Required | Values              | Description                                                       |
| -------- | ------ | -------- | ------------------- | ----------------------------------------------------------------- |
| `action` | string | Yes      | —                   | Exact InputAction name (e.g., `"MoveUp"`, `"Jump"`).              |
| `type`   | string | Yes      | `"press"`, `"hold"` | `press` = single-frame tap. `hold` = sustained for full duration. |

### Behavior

1. Focuses Unity Editor window and Game View.
2. Sets `Time.timeScale = 1`.
3. Loads the project's `InputActionAsset` from Resources.
4. For each input:
   - Finds the `InputAction` by name.
   - Enables it if disabled.
   - `hold`: Triggers every frame for the full duration.
   - `press`: Triggers once, releases after 1 frame.
5. Waits for `duration` ms (realtime).
6. Captures console logs generated during play.
7. Sets `Time.timeScale = 0`.
8. Releases all inputs and resets keyboard state.

### Returns

Console logs captured during the play duration.

### Supported control types

The tool handles these Unity Input System control types:

- **KeyControl** (keyboard keys) — via `KeyboardState` events.
- **ButtonControl** (gamepad/mouse buttons) — via `QueueDeltaStateEvent`.
- **AxisControl** (analog sticks, triggers) — via `QueueDeltaStateEvent`.
- **Other controls** — via generic `StateEvent`.

### Error conditions

- Not in Play Mode → error message (call `enter_play_mode` first).
- InputActionAsset not found → error message.
- Action name not found → warning logged, action skipped.

---

## 3. execute_csharp_script_in_unity_editor

Executes arbitrary C# code in the live Unity Editor via Roslyn.

### Key rules (summarized)

- **Top-level statements only.** No class or method wrappers.
- **Pre-imported:** `System`, `System.Collections.Generic`, `System.Linq`, `UnityEngine`, `UnityEditor`. Do not re-declare.
- **Synchronous only.** No `async`/`await`.
- **Null checks:** Use `== null`, not `?.` or `??` (Unity operator overrides).
- **Output:** Use `Debug.Log()` — captured directly in tool result.
- **Error output:** Use `Debug.LogError()`.

### Common patterns for game playing

**Read positions and velocities:**

```csharp
var ball = GameObject.Find("Ball");
var rb = ball.GetComponent<Rigidbody2D>();
Debug.Log($"SENSE: pos={ball.transform.position} vel={rb.linearVelocity}");
```

**Read private fields via reflection:**

```csharp
var paddle = GameObject.Find("Paddle");
var controller = paddle.GetComponent<PaddleController>();
var speedField = controller.GetType().GetField("_speed",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
float speed = (float)speedField.GetValue(controller);
Debug.Log($"INIT: paddleSpeed={speed}");
```

**Query all GameObjects:**

```csharp
var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
foreach (var go in allObjects)
    Debug.Log($"{go.name} pos={go.transform.position} active={go.activeSelf}");
```

**Read collider bounds:**

```csharp
var wall = GameObject.Find("TopWall");
var col = wall.GetComponent<Collider2D>();
Debug.Log($"INIT: topWall bounds={col.bounds.min} to {col.bounds.max}");
```

---

## 4. read_unity_console_logs

Reads recent Unity Console log entries.

### Parameters

| Parameter     | Type    | Required | Description                                    |
| ------------- | ------- | -------- | ---------------------------------------------- |
| `max_entries` | integer | No       | Number of recent entries to return. Use 20–50. |

### When to use during gameplay

- **Before INIT scripts:** Check for compilation errors that would prevent script execution.
- **Debugging unexpected behavior:** When tool outputs don't explain the issue.
- **Avoid during normal loop:** Logs from `execute_csharp_script_in_unity_editor` and `play_unity_game` are returned directly in tool output.

---

## 5. exit_play_mode

Exits Unity Play Mode.

**Parameters:** None.

**Behavior:**

- Sets `EditorApplication.isPlaying = false`.
- Restores `Time.timeScale = 1`.
- Call only when done playing.

---

## 6. get_unity_info

Returns Unity Editor metadata.

**Returns:** Unity version, project path, loaded scenes, and other environment info.

**When to use:** During INIT to confirm project context if unsure about the environment.
