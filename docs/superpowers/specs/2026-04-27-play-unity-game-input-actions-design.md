# Play Unity Game Input Actions Design

## Goal

Remove the hardcoded `PongInputActions` dependency from `PlayUnityGameTool` and resolve the `InputActionAsset` dynamically on every tool invocation.

## Requirements

- Add an input actions section to `UnityCodeMcpServerSettings`.
- Store a configurable input actions asset path string in settings, not an asset reference.
- During every `play_unity_game` call, resolve the asset in this order:
  1. Use the settings path when set.
  2. Otherwise use the first `InputActionAsset` found under `Assets/`.
  3. Otherwise use the first `InputActionAsset` found anywhere.
- For steps 2 and 3, print a warning that settings are not configured and identify the chosen asset.
- Load the `InputActionAsset` only inside the play tool call.

## Architecture

`UnityCodeMcpServerSettings` remains the persistence boundary and stores only a normalized asset path string. The custom settings inspector exposes that path through a text field plus object picker that writes the asset path, not the asset itself.

`PlayUnityGameTool` owns the runtime resolution workflow because the user requires the lookup to run on every call. The tool will resolve an asset path fresh via `AssetDatabase`, load the `InputActionAsset` from that path, warn on fallback cases, and return a clear error if no matching asset can be found or loaded.

## Boundaries

- `UnityCodeMcpServerSettings.cs`: serialized string property and path normalization helper.
- `UnityCodeMcpServerSettingsEditor.cs`: settings inspector UI for selecting or clearing the path.
- `PlayUnityGameTool.cs`: runtime asset-path resolution, warning/error messages, and removal of the hardcoded resource lookup.
- Tests: edit-mode coverage for settings persistence helpers and play-tool resolution ordering.

## Error Handling

- An invalid configured path is treated as unresolved and falls through to the same search order.
- If no `InputActionAsset` exists anywhere, the tool returns an error result instead of attempting input playback.
- Fallback warnings include the resolved asset path so the user can set it explicitly.

## Testing

- Add edit-mode tests for settings path normalization and empty-path behavior.
- Add play-mode or editor-facing tests for resolution order:
  - configured path wins
  - first asset under `Assets/` is used when settings are unset
  - first asset anywhere is used when no asset exists under `Assets/`
  - no asset found returns null or an error precursor
