using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Handlers;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Tool that plays the game in the Unity Editor for a specified duration while triggering input actions.
/// Uses direct StateEvent approach to simulate inputs.
/// </summary>
public class PlayUnityGameTool : IToolAsync
{
    private const string InputAssetName = "PongInputActions";
    private readonly Dictionary<Keyboard, HashSet<Key>> _active_keys_by_keyboard = new();

    public string Name => "play_unity_game";

    public string Description =>
        @"Advances the Unity game state and simulates player input for a specified duration.
WHAT IT DOES: Temporarily unpauses the game (timeScale=1), triggers specified Input System actions (press/hold), records console logs, and safely pauses the game (timeScale=0) upon completion.
WHEN TO USE: Use to test gameplay mechanics over time and simulate character movement or UI interactions.
WHEN NOT TO USE: Do NOT use to edit scripts, modify scene architecture, or inspect static scene data.
PREREQUISITES: Unity MUST already be in Play Mode (use the 'enter_play_mode' tool first).
SIDE EFFECTS: Alters Time.timeScale, overrides active Input System states, and consumes in-game time.";

    public JsonElement InputSchema => JsonHelper.ParseElement(@"
        {
            ""type"": ""object"",
            ""properties"": {
                ""duration"": {
                    ""type"": ""integer"",
                    ""minimum"": 0,
                    ""description"": ""Duration in milliseconds to run the game in Play Mode. Set to 0 for instant completion, or higher (e.g., 1000 for 1 second) to simulate gameplay over time.""
                },
                ""input"": {
                    ""type"": ""array"",
                    ""description"": ""Array of input actions to simulate during the play duration. Multiple actions can be specified to simulate simultaneous inputs (e.g., holding 'MoveRight' and 'Jump' together)."",
                    ""items"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""action"": { ""type"": ""string"", ""description"": ""The exact string name of the InputAction to trigger (e.g., 'Move', 'Jump'). Must match the actions defined in the project's InputActionAsset."" },
                            ""type"": { ""type"": ""string"", ""enum"": [""press"", ""hold""], ""description"": ""Specifies the input behavior: 'press' (triggers the action once for a single frame, like tapping a button) or 'hold' (keeps the action continuously engaged for the entire play duration)."" }
                        },
                        ""required"": [""action"", ""type""]
                    }
                }
            },
            ""required"": [""duration""]
        }
        ");

    public async UniTask<ToolsCallResult> ExecuteAsync(JsonElement arguments)
    {
        if (!TryParseArguments(arguments, out PlayOptions options, out string errorMessage))
        {
            UnityCodeMcpServerLogger.Warn($"#PlayUnityGameTool: invalid arguments: {errorMessage}");
            return ToolsCallResult.ErrorResult(errorMessage);
        }

        UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: duration={options.DurationMs}ms inputs={options.Inputs.Count}");

        if (!EditorApplication.isPlaying)
        {
            return ToolsCallResult.ErrorResult("Unity is not in Play Mode. Use the enter_play_mode tool first.");
        }

        List<InputAction> held_actions = new();
        List<InputAction> actions_to_release = new();

        LogCapture logCapture = new();

        InputSettings.BackgroundBehavior previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;

        try
        {
            logCapture.Start();

            _active_keys_by_keyboard.Clear();
            Time.timeScale = 1f;

            // Bypass Input System focus gating so input works without window focus.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            // Re-enable devices that were disabled when Unity lost focus.
            // OnFocusChanged(false) disables devices with DisabledWhileInBackground flag
            // BEFORE our IgnoreFocus setting takes effect, so we must re-enable them.
            int reenabledCount = 0;
            foreach (InputDevice device in InputSystem.devices)
            {
                if (!device.enabled)
                {
                    UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: re-enabling disabled device: {device.name} (id={device.deviceId})");
                    InputSystem.EnableDevice(device);
                    reenabledCount++;
                }
            }
            if (reenabledCount > 0)
                UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: re-enabled {reenabledCount} device(s).");

            // Reset all input devices to clear residual state from previous invocations.
            // Without this, a key, button, or stick value left active (e.g., due to focus
            // gating dropping release events) can keep gameplay input non-zero even when
            // no input is specified for the current run.
            ResetAllInputDevices();

            InputActionAsset input_asset = Resources.Load<InputActionAsset>(InputAssetName);
            if (input_asset == null)
            {
                return ToolsCallResult.ErrorResult($"Could not find InputActionAsset '{InputAssetName}' in Resources folder.");
            }

            UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: triggering {options.Inputs.Count} input(s). " +
                $"App.isFocused={Application.isFocused}, devices={InputSystem.devices.Count}");

            TriggerInputs(input_asset, options.Inputs, held_actions, actions_to_release);

            // Log post-trigger action states (events processed on next frame).
            foreach (InputAction heldAction in held_actions)
            {
                UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: post-trigger action '{heldAction.name}' phase={heldAction.phase}, " +
                    $"IsPressed={heldAction.IsPressed()}, triggered={heldAction.triggered}");
            }

            if (options.DurationMs > 0)
            {
                UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: running for {options.DurationMs}ms.");

                if (held_actions.Count == 0)
                {
                    await UniTask.Delay(options.DurationMs, DelayType.Realtime, PlayerLoopTiming.Update);
                }
                else
                {
                    float end_time = Time.realtimeSinceStartup + (options.DurationMs / 1000f);
                    while (Time.realtimeSinceStartup < end_time)
                    {
                        TriggerHeldInputs(held_actions);
                        await UniTask.Yield(PlayerLoopTiming.Update);
                    }
                }
            }

            string logs = logCapture.GetLogs();
            return ToolsCallResult.TextResult($"Logs captured during play:\n{logs}");
        }
        catch (Exception ex)
        {
            logCapture.Stop();
            UnityCodeMcpServerLogger.Error($"#PlayUnityGameTool: exception: {ex}");
            return ToolsCallResult.ErrorResult($"Failed to play Unity game: {ex.Message}\n\nLogs:\n{logCapture.GetLogs()}");
        }
        finally
        {
            logCapture.Stop();
            logCapture.Dispose();
            Time.timeScale = 0f;
            // Release actions and reset devices BEFORE restoring InputSettings.
            // Restoring settings first re-enables focus gating, which may silently
            // drop the queued release/reset events, leaving keys stuck pressed.
            ReleaseActions(actions_to_release);
            ReleaseActions(held_actions);
            ResetAllInputDevices();
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
        }
    }

    /// <summary>
    /// Triggers inputs using the StateEvent approach (similar to PongInputTester).
    /// </summary>
    private void TriggerInputs(
        InputActionAsset asset,
        IReadOnlyList<InputRequest> inputs,
        List<InputAction> heldActions,
        List<InputAction> actionsToRelease)
    {
        foreach (InputRequest input in inputs)
        {
            InputAction action = asset.FindAction(input.ActionName, false);
            if (action == null)
            {
                UnityCodeMcpServerLogger.Warn($"#PlayUnityGameTool: Action '{input.ActionName}' not found in asset.");
                continue;
            }

            if (!action.enabled)
            {
                action.Enable();
                UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: Enabled action '{action.name}'.");
            }

            InputControl control = action.controls.Count > 0 ? action.controls[0] : null;
            UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: Action '{action.name}' -> control={control?.path ?? "NONE"}, " +
                $"device={control?.device?.name ?? "NONE"}, deviceEnabled={control?.device?.enabled}, " +
                $"phase={action.phase}, type={input.Type}");

            TriggerAction(action, 1.0f);

            // Always add to safety release list. (Press releases are still scheduled for a short "tap".)
            actionsToRelease.Add(action);

            if (input.Type == InputType.Hold)
            {
                heldActions.Add(action);
            }

            else
            {
                UniTask.DelayFrame(1).ContinueWith(() => TriggerAction(action, 0.0f)).Forget();
            }
        }
    }

    /// <summary>
    /// Triggers an action with the specified value using StateEvent.
    /// </summary>
    private void TriggerAction(InputAction action, float value)
    {
        if (action == null || action.controls.Count == 0)
        {
            UnityCodeMcpServerLogger.Warn($"#PlayUnityGameTool: TriggerAction skipped — action={action?.name ?? "null"}, controls={action?.controls.Count ?? 0}");
            return;
        }

        InputControl control = action.controls[0];

        if (control is KeyControl keyControl && keyControl.device is Keyboard keyboard)
        {
            UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: Queuing keyboard state for key={keyControl.keyCode}, " +
                $"pressed={value > 0f}, device={keyboard.name}, deviceEnabled={keyboard.enabled}");
            QueueKeyboardStateEvent(keyboard, keyControl.keyCode, value > 0f);
            return;
        }

        if (control is ButtonControl buttonControl)
        {
            using (StateEvent.From(buttonControl.device, out InputEventPtr eventPtr))
            {
                buttonControl.WriteValueIntoEvent(value > 0f ? 1f : 0f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }

            UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: Queued button state event for {control.path} with value {(value > 0f ? "pressed" : "released")}.");
            return;
        }

        if (control is AxisControl axisControl)
        {
            InputSystem.QueueDeltaStateEvent(axisControl, value);
            UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: Queued axis event for {control.path} with value {value}.");
            return;
        }

        using (StateEvent.From(control.device, out InputEventPtr eventPtr))
        {
            control.WriteValueIntoEvent(value, eventPtr);
            InputSystem.QueueEvent(eventPtr);
            UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: Queued state event for {control.path} with value {value}.");
        }
    }

    private void QueueKeyboardStateEvent(Keyboard keyboard, Key key, bool isPressed)
    {
        if (!_active_keys_by_keyboard.TryGetValue(keyboard, out HashSet<Key> activeKeys))
        {
            activeKeys = new HashSet<Key>();
            _active_keys_by_keyboard[keyboard] = activeKeys;
        }

        if (isPressed)
        {
            activeKeys.Add(key);
        }
        else
        {
            activeKeys.Remove(key);
        }

        Key[] keysArray = new Key[activeKeys.Count];
        activeKeys.CopyTo(keysArray);
        KeyboardState keyboardState = new(keysArray);
        InputSystem.QueueStateEvent(keyboard, keyboardState);
        UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: Queued keyboard event for {key} ({(isPressed ? "pressed" : "released")}). Active keys: {string.Join(", ", activeKeys)}");
    }

    private void ReleaseActions(List<InputAction> actions)
    {
        if (actions == null || actions.Count == 0)
        {
            return;
        }

        foreach (InputAction action in actions)
        {
            TriggerAction(action, 0.0f);
        }

        actions.Clear();
    }

    private void ResetAllInputDevices()
    {
        // Reset all devices so keyboard, gamepad, and other controller state cannot leak
        // between invocations. A soft device reset cancels in-progress actions and clears
        // pressed/button-like state without requiring per-device state event code.
        foreach (InputDevice device in InputSystem.devices)
        {
            if (device == null)
            {
                continue;
            }

            InputSystem.ResetDevice(device);
            UnityCodeMcpServerLogger.Debug($"#PlayUnityGameTool: Reset state for device {device.name} ({device.deviceId}).");
        }

        _active_keys_by_keyboard.Clear();
    }

    public static bool TryParseArguments(JsonElement arguments, out PlayOptions options, out string errorMessage)
    {
        options = default;
        errorMessage = null;

        if (!TryGetRequiredInt(arguments, "duration", out int duration_ms, out errorMessage))
        {
            return false;
        }

        if (duration_ms < 0)
        {
            errorMessage = "Missing required parameter: 'duration' (milliseconds).";
            return false;
        }

        if (!TryParseInputs(arguments, out List<InputRequest> inputs, out errorMessage))
        {
            return false;
        }

        options = new PlayOptions(duration_ms, inputs);
        return true;
    }

    private static bool TryGetRequiredInt(JsonElement arguments, string propertyName, out int value, out string errorMessage)
    {
        value = default;
        errorMessage = null;

        if (!arguments.TryGetProperty(propertyName, out JsonElement element))
        {
            errorMessage = $"Missing required parameter: '{propertyName}'.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out value))
        {
            errorMessage = $"Parameter '{propertyName}' must be an integer.";
            return false;
        }

        return true;
    }

    private static bool TryGetOptionalInt(JsonElement arguments, string propertyName, int defaultValue, out int value, out string errorMessage)
    {
        value = defaultValue;
        errorMessage = null;

        if (!arguments.TryGetProperty(propertyName, out JsonElement element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out value))
        {
            errorMessage = $"Parameter '{propertyName}' must be an integer.";
            return false;
        }

        return true;
    }

    private static bool TryGetOptionalBool(JsonElement arguments, string propertyName, out bool value, out string errorMessage)
    {
        value = false;
        errorMessage = null;

        if (!arguments.TryGetProperty(propertyName, out JsonElement element))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        errorMessage = $"Parameter '{propertyName}' must be a boolean.";
        return false;
    }

    private static bool TryParseInputs(JsonElement arguments, out List<InputRequest> inputs, out string errorMessage)
    {
        inputs = new List<InputRequest>();
        errorMessage = null;

        if (!arguments.TryGetProperty("input", out JsonElement inputElement))
        {
            return true;
        }

        if (inputElement.ValueKind != JsonValueKind.Array)
        {
            errorMessage = "Parameter 'input' must be an array.";
            return false;
        }

        foreach (JsonElement item in inputElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "Each input entry must be an object with 'action' and 'type'.";
                return false;
            }

            if (!item.TryGetProperty("action", out JsonElement actionElement) || actionElement.ValueKind != JsonValueKind.String)
            {
                errorMessage = "Each input entry must contain string property 'action'.";
                return false;
            }

            if (!item.TryGetProperty("type", out JsonElement typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                errorMessage = "Each input entry must contain string property 'type'.";
                return false;
            }

            string actionName = (actionElement.GetString() ?? string.Empty).Trim();
            string typeRaw = (typeElement.GetString() ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(actionName))
            {
                errorMessage = "Input action name cannot be empty.";
                return false;
            }

            if (!TryParseInputType(typeRaw, out InputType inputType))
            {
                errorMessage = "Input type must be 'press' or 'hold'.";
                return false;
            }

            inputs.Add(new InputRequest(actionName, inputType));
        }

        return true;
    }

    private static bool TryParseInputType(string value, out InputType inputType)
    {
        inputType = InputType.Press;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Equals("press", StringComparison.OrdinalIgnoreCase))
        {
            inputType = InputType.Press;
            return true;
        }

        if (value.Equals("hold", StringComparison.OrdinalIgnoreCase))
        {
            inputType = InputType.Hold;
            return true;
        }

        return false;
    }





    /// <summary>
    /// Triggers all held inputs.
    /// </summary>
    private void TriggerHeldInputs(List<InputAction> heldActions)
    {
        foreach (InputAction heldAction in heldActions)
        {
            TriggerAction(heldAction, 1.0f);
        }
    }



    public readonly struct PlayOptions
    {
        public int DurationMs { get; }
        public IReadOnlyList<InputRequest> Inputs { get; }

        public PlayOptions(int durationMs, IReadOnlyList<InputRequest> inputs)
        {
            DurationMs = durationMs;
            Inputs = inputs ?? Array.Empty<InputRequest>();
        }
    }

    public readonly struct InputRequest
    {
        public string ActionName { get; }
        public InputType Type { get; }

        public InputRequest(string actionName, InputType type)
        {
            ActionName = actionName;
            Type = type;
        }
    }

    public enum InputType
    {
        Press,
        Hold
    }

}

