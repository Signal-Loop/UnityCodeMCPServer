# PlayUnityGameTool Input Reliability Notes

## Current finding

- `InputSystem.settings.backgroundBehavior = IgnoreFocus` is not enough by itself. InputSystem also checks the runtime `runInBackground` state when the Game View or editor loses focus.
- `Application.runInBackground` is still wrapped in `McpRegistry`, but `PlayUnityGameTool` also needs to own it directly. The play tool depends on that setting for correctness, and direct tool execution/tests otherwise run under different prerequisites than real MCP calls.
- Keeping the setting in both places is intentional: during normal MCP execution the tool restores to the registry's temporary `true`, then the registry restores the original caller value.
- The remaining post-focus-cycle failure was caused by InputSystem devices marked internally as `DisabledWhileInBackground` after Unity regained and then lost focus. In the editor, `device.enabled` can be misleading during editor updates, so only checking `!device.enabled` did not reliably re-enable those devices before queuing synthetic input.

## Fixes that did not reliably solve the focus-loss bug by themselves

- `51c19ed` / `ab2d260`: manually flushing queued input with `InputSystem.Update()` after resets, initial trigger, held refresh, and final release. This was removed; the tool now relies on the normal player-loop InputSystem update.
- `f879297` / `ab2d260`: replacing UniTask waits with `UnityPlayerLoopAsync` and refreshing held inputs every player-loop tick. `UnityPlayerLoopAsync` was removed again; current verification uses `UnityEditorAsync` waits plus focus-disabled device recovery.
- Re-enabling disabled devices and resetting all devices is useful cleanup, but it does not replace keeping `Application.runInBackground` true while the tool is actively simulating input.
- Checking only `!device.enabled` before `InputSystem.EnableDevice(device)` did not cover editor focus states where the internal `DisabledWhileInBackground` flag remains set.

## Regression scenario to keep covering

- Enter Play Mode.
- First input works.
- Unity regains focus.
- Unity loses focus to the agent/VS Code.
- Second input must still work.
- Covered by `PlayUnityGameToolTests.TriggerAction_FirstInputWorksThenFocusRegainLossSecondInputWorks`.
