---
name: unity-game-player
description: >-
  Autonomous Unity game-playing skill for AI agents. Provides the complete
  closed-loop workflow (INIT → SENSE+COMPUTE → ACT → REPEAT) for dynamically
  discovering game mechanics, reading live state via C# script execution,
  computing mathematically precise actions, and simulating player input.
  Use when an agent must play, test, or demonstrate any Unity game in the Editor
  — regardless of genre (paddle, platformer, shooter, puzzle, etc.).
  Covers: dynamic scene discovery, trajectory prediction, input simulation
  (press/hold) and adaptive re-sensing.
---

# Unity Game Player

Skill for AI agents that play Unity games autonomously in the Editor via MCP tools.

## Table of Contents

1. Prerequisites
2. Available Tools
3. Core Loop Overview
4. Phase 0 — INIT
5. Phases 1+2 — SENSE + COMPUTE
6. Phase 3 — ACT
7. Phase 4 — REPEAT
8. Critical Rules
9. Common Pitfalls

For detailed tool API parameters and trajectory math patterns, see:

- [Tool Reference](references/tool-reference.md)
- [Trajectory & Math Patterns](references/trajectory-math.md)

---

## 1. Prerequisites

- Unity Editor must be running and the project open.
- Read `executing-csharp-scripts-in-unity-editor` skill - it is critical to properly play Unity games in the Editor.
- The game scene must be loaded.
- Call `enter_play_mode` **once** before starting the loop. This enters Play Mode with `Time.timeScale = 0` (paused).
- All subsequent gameplay is driven by `play_unity_game`, which temporarily sets `timeScale = 1` for the requested duration, then re-pauses.

---

## 2. Available Tools

The `executing-csharp-scripts-in-unity-editor` skill is essential to properly execute C# scripts in the Unity Editor. It must be read before using the `execute_csharp_script_in_unity_editor` tool.

| Tool                                    | Purpose                                                                                                             |
| --------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `enter_play_mode`                       | Enter Play Mode (paused). Call once at start.                                                                       |
| `play_unity_game`                       | Unpause for N ms, simulate inputs, capture console logs.                                                            |
| `execute_csharp_script_in_unity_editor` | Run C# in the Editor to query/modify scene state. Read `executing-csharp-scripts-in-unity-editor` skill before use. |
| `read_unity_console_logs`               | Read recent console entries (use sparingly — prefer logs from tool output).                                         |
| `exit_play_mode`                        | Leave Play Mode. Call only when done.                                                                               |
| `get_unity_info`                        | Query Unity version, project path, scene list.                                                                      |

---

## 3. Core Loop Overview

```
INIT  →  SENSE+COMPUTE  →  ACT  →  REPEAT
 ↑                                    │
 └────── on game reset/restart ───────┘
```

**Never skip COMPUTE.** Every ACT must be preceded by a fresh SENSE+COMPUTE.

---

## 4. Phase 0 — INIT (run once)

Discover everything dynamically. Never hardcode scene structure, component names, or values.

### 4a. Read source code

- List scripts under `Assets/Scripts/` (all subdirectories).
- Read controllers, managers, and service classes to learn:
  - Component types on player-controlled entities and targets (ball, enemies, etc.).
  - Fields controlling movement speed, bounds, physics behavior.
  - How velocity/position are stored (Rigidbody2D, custom field, etc.).
  - Win/loss/scoring logic and what triggers state transitions.
  - Input action names defined in the InputActionAsset.

### 4b. Query the live scene

Write a single `execute_csharp_script_in_unity_editor` script to:

```
For each critical GameObject (player, target, walls, triggers, UI):
    Log: name, transform.position, collider bounds, attached component types
Read movement speeds from controller components (use reflection for private fields)
Derive spatial boundaries from wall/collider positions + half-extents
Derive clamp limits for controllable entities
Log all values with clear labels
```

### 4c. Store constants in working memory

Organize discovered values in a mental table:

| Category         | Examples                                               |
| ---------------- | ------------------------------------------------------ |
| Entity positions | `playerPos`, `targetPos`, `wallPositions`              |
| Movement params  | `playerSpeed`, `targetSpeed`                           |
| Spatial bounds   | `topBound`, `bottomBound`, `leftBound`, `rightBound`   |
| Input mapping    | `upAction`, `downAction`, `leftAction`, `rightAction`  |
| State accessors  | How to read velocity, score, health at runtime         |
| Game rules       | Miss/death conditions, score mechanics, reset behavior |

Re-run INIT if the game restarts, reloads, or if discovered values appear stale.

---

## 5. Phases 1+2 — SENSE + COMPUTE (single script)

**Always combine** SENSE and COMPUTE into one `execute_csharp_script_in_unity_editor` call. Separate calls waste context and time.

### Script structure

```csharp
// --- SENSE ---
// Read live positions, velocities, states of all relevant entities
// Use the accessor patterns discovered in INIT

// --- COMPUTE ---
// Using INIT constants + sensed state, calculate:
//   1. Target position or interception point
//   2. Delta between current state and target
//   3. Exact input duration: durationMs = abs(delta) / speed * 1000
//   4. Input direction/action name

// --- OUTPUT ---
Debug.Log($"SENSE: player=({px},{py}) target=({tx},{ty}) vel=({vx},{vy})");
Debug.Log($"COMPUTE: action={dir} duration={ms}ms delta={delta}");
```

### Key principles

- Use **exact math**. Never guess durations or add arbitrary minimums.
- `durationMs = abs(delta) / speed * 1000` — this is the fundamental formula.
- For trajectory prediction with bouncing, see [Trajectory & Math Patterns](references/trajectory-math.md).
- If the entity is already aligned (`abs(delta) < threshold`), output `COMPUTE: idle` instead.

---

## 6. Phase 3 — ACT

Parse the `Debug.Log` output from SENSE+COMPUTE and call `play_unity_game`.

### Input format

```yaml
# Move an entity
play_unity_game:
  duration: <exact_computed_ms>
  input:
    - action: "<action_name_from_INIT>"
      type: hold

# Idle / observe (no input needed)
play_unity_game:
  duration: <wait_ms>
```

### Duration rules

| Situation                                       | Duration                                 |
| ----------------------------------------------- | ---------------------------------------- |
| Move to target                                  | **Exact** `abs(delta) / speed * 1000` ms |
| Already aligned (`abs(delta) < threshold`)      | Skip move; idle 100–200 ms               |
| Target moving away / no immediate action needed | Idle 200–500 ms, then re-sense           |
| After miss / reset / state transition           | Idle 300–500 ms for physics to settle    |

**Never add arbitrary minimum durations.** A 0.2-unit delta at speed 8 requires only 25 ms — forcing 200 ms will overshoot.

### Simultaneous inputs

Multiple actions can be combined in a single call:

```yaml
input:
  - action: "MoveRight"
    type: hold
  - action: "Jump"
    type: press
```

- `hold` — sustained for the full duration (re-triggered every frame).
- `press` — single-frame tap at the start.

---

## 7. Phase 4 — REPEAT

1. After every `play_unity_game` call, check the **returned logs** for game events (score changes, misses, resets, game-over).
2. Return immediately to **SENSE+COMPUTE**. Never chain multiple ACTs without re-sensing.
3. If game restarted or state changed drastically, re-run **INIT**.

### Fine-tune correction

After positioning, re-sense and check residual delta. If `abs(residual) > small_threshold`, apply one more exact correction before idling. This two-pass approach eliminates timing drift.

---

## 8. Critical Rules

1. **Read `executing-csharp-scripts-in-unity-editor`** skill first — it is essential for proper scene interaction.
2. **Always use Input actions to move entities** — never directly modify transforms or velocities.
3. **Zero assumptions** — Discover everything dynamically. Scene structure, names, speeds, bounds.
4. **Mathematical precision** — All durations from exact `delta / speed * 1000`. No guessing.
5. **Always re-sense** — After every ACT. Game state changes continuously.
6. **Single-script SENSE+COMPUTE** — Never separate them into two tool calls.
7. **Exact durations** — No rounding, no minimums, no maximums unless physics-dictated.
8. **Use `<thinking>` blocks** — Plan script logic and analyze outputs before every tool call.
9. **Logs over console reads** — Rely on `Debug.Log()` output in tool results. Avoid separate `read_unity_console_logs` calls during gameplay.
10. **Graceful recovery** — On unexpected state (NaN, missing object, error), re-run INIT before continuing.

---

## 9. Common Pitfalls

| Pitfall                        | Fix                                                       |
| ------------------------------ | --------------------------------------------------------- |
| Overshooting target position   | Use exact computed duration, not rounded/minimum values   |
| Stale state after bounce/reset | Always re-sense; never chain actions blindly              |
| `Action not found` error       | Verify action names during INIT from the InputActionAsset |
| Game not advancing             | Confirm Play Mode is active (`enter_play_mode` called)    |
| Wrong entity moves             | Double-check action→entity mapping in INIT                |
| Physics jitter after reset     | Idle 300–500 ms before re-sensing                         |
