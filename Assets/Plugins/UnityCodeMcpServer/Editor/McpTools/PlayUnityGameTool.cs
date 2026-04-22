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
    private const string DefaultMimeType = "image/png";
    private const string InputAssetName = "PongInputActions";
    private static readonly TimeSpan ScreenshotTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ScreenshotPollInterval = TimeSpan.FromMilliseconds(50);
    private readonly Dictionary<Keyboard, HashSet<Key>> _active_keys_by_keyboard = new();

    public string Name => "play_unity_game";

    public string Description =>
        @"Advances the Unity game state and simulates player input for a specified duration.
WHAT IT DOES: Temporarily unpauses the game (timeScale=1), triggers specified Input System actions (press/hold), captures Game View screenshot, records console logs, and safely pauses the game (timeScale=0) upon completion.
WHEN TO USE: Use to test gameplay mechanics over time, simulate character movement or UI interactions, and observe visual or log feedback during live gameplay.
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
                    ""description"": ""Duration in milliseconds to run the game in Play Mode. Set to 0 for an instant screenshot, or higher (e.g., 1000 for 1 second) to simulate gameplay over time.""
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
                },
                ""max_height"": {
                    ""type"": ""integer"",
                    ""minimum"": 1,
                    ""description"": ""Maximum pixel height for the returned screenshots. Taller images are proportionally scaled down to save token context limits. Default: 640."",
                    ""default"": 640
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

            ToolsCallResult success_result = null;

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

            UnityCodeMcpServerLogger.Debug("#PlayUnityGameTool: capturing screenshot.");
            CaptureResult capture_result = await CaptureGameViewScreenshotAsync();
            if (capture_result.IsError)
            {
                UnityCodeMcpServerLogger.Warn($"#PlayUnityGameTool: screenshot failed: {capture_result.ErrorMessage}");
                return ToolsCallResult.ErrorResult(capture_result.ErrorMessage ?? "Failed to capture Game View screenshot.");
            }

            CaptureResult scaled_capture = ScaleCaptureToMaxHeight(capture_result, options.MaxHeight);
            if (scaled_capture.IsError)
            {
                return ToolsCallResult.ErrorResult(scaled_capture.ErrorMessage ?? "Failed to scale screenshot.");
            }

            success_result = ToolsCallResult.ImageResult(scaled_capture.Base64Data, scaled_capture.MimeType ?? DefaultMimeType);
            success_result.Content.Add(ContentItem.TextContent($"Logs captured during play:\n{logCapture.GetLogs()}"));

            return success_result ?? ToolsCallResult.ErrorResult("Internal error: no result produced.");
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

        if (!TryGetOptionalInt(arguments, "max_height", 640, out int max_height, out errorMessage))
        {
            return false;
        }

        if (!TryGetOptionalInt(arguments, "max_base64_bytes", 50_000_000, out int max_base64_bytes, out errorMessage))
        {
            return false;
        }

        if (max_height <= 0)
        {
            errorMessage = "Parameter 'max_height' must be greater than 0.";
            return false;
        }

        if (max_base64_bytes <= 0)
        {
            errorMessage = "Parameter 'max_base64_bytes' must be greater than 0.";
            return false;
        }

        if (!TryParseInputs(arguments, out List<InputRequest> inputs, out errorMessage))
        {
            return false;
        }

        options = new PlayOptions(duration_ms, inputs, max_height, max_base64_bytes);
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
    /// Calculates scaled dimensions to fit within max height while preserving aspect ratio.
    /// </summary>
    public static void GetScaledDimensionsToMaxHeight(int width, int height, int maxHeight, out int scaledWidth, out int scaledHeight)
    {
        if (width <= 0 || height <= 0)
        {
            scaledWidth = 1;
            scaledHeight = 1;
            return;
        }

        if (height <= maxHeight)
        {
            scaledWidth = width;
            scaledHeight = height;
            return;
        }

        double scale = (double)maxHeight / height;
        scaledWidth = Math.Max(1, (int)Math.Floor(width * scale));
        scaledHeight = maxHeight;
    }

    /// <summary>
    /// Scales a PNG image to target dimensions.
    /// </summary>
    private static CaptureResult ScalePngImage(byte[] source_bytes, int target_width, int target_height)
    {
        if (source_bytes == null || source_bytes.Length == 0)
        {
            return CaptureResult.Error("Empty screenshot bytes.");
        }

        Texture2D source_texture = new(2, 2, TextureFormat.RGB24, false);
        Texture2D scaled_texture = null;
        RenderTexture temporary_render_texture = null;
        RenderTexture previous_render_texture = RenderTexture.active;

        try
        {
            if (!source_texture.LoadImage(source_bytes, false))
            {
                return CaptureResult.Error("Failed to decode screenshot image.");
            }

            if (source_texture.width == target_width && source_texture.height == target_height)
            {
                // No scaling needed
                return CaptureResult.Success(Convert.ToBase64String(source_bytes), DefaultMimeType);
            }

            temporary_render_texture = RenderTexture.GetTemporary(target_width, target_height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source_texture, temporary_render_texture);
            RenderTexture.active = temporary_render_texture;

            scaled_texture = new Texture2D(target_width, target_height, TextureFormat.RGB24, false);
            scaled_texture.ReadPixels(new Rect(0, 0, target_width, target_height), 0, 0);
            scaled_texture.Apply(false, false);

            byte[] png_bytes = scaled_texture.EncodeToPNG();
            if (png_bytes == null || png_bytes.Length == 0)
            {
                return CaptureResult.Error("Failed to encode screenshot to PNG.");
            }

            return CaptureResult.Success(Convert.ToBase64String(png_bytes), DefaultMimeType);
        }
        finally
        {
            RenderTexture.active = previous_render_texture;

            if (temporary_render_texture != null)
            {
                RenderTexture.ReleaseTemporary(temporary_render_texture);
            }

            if (scaled_texture != null && !ReferenceEquals(scaled_texture, source_texture))
            {
                UnityEngine.Object.DestroyImmediate(scaled_texture);
            }

            UnityEngine.Object.DestroyImmediate(source_texture);
        }
    }

    /// <summary>
    /// Repaints the Game View to ensure fresh frames are rendered.
    /// </summary>
    private static void RepaintGameView()
    {
        try
        {
            Type game_view_type = Type.GetType("UnityEditor.GameView, UnityEditor");
            if (game_view_type == null)
            {
                return;
            }

            EditorWindow game_view = EditorWindow.GetWindow(game_view_type);
            if (game_view != null)
            {
                game_view.Repaint();
            }
        }
        catch
        {
        }
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

    private async UniTask<CaptureResult> CaptureGameViewScreenshotAsync()
    {
        string tempPath = null;
        try
        {
            tempPath = CreateTempScreenshotPath();
            RequestScreenshot(tempPath);

            byte[] pngBytes = await ReadFileWhenReadyAsync(tempPath, ScreenshotTimeout, ScreenshotPollInterval);
            if (pngBytes == null || pngBytes.Length == 0)
            {
                return CaptureResult.Error("Game View screenshot not ready. Ensure the Game View is visible and try again.");
            }

            string base64 = Convert.ToBase64String(pngBytes);
            return CaptureResult.Success(base64, DefaultMimeType);
        }
        catch (Exception ex)
        {
            return CaptureResult.Error($"Failed to capture Game View screenshot: {ex.Message}");
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private static string CreateTempScreenshotPath()
    {
        string tempDir = Path.Combine(Application.dataPath, "..", "Temp", "UnityGameViewScreenshots");
        Directory.CreateDirectory(tempDir);
        return Path.Combine(tempDir, $"game_view_{Guid.NewGuid():N}.png");
    }

    private static void RequestScreenshot(string path)
    {
        ScreenCapture.CaptureScreenshot(path, 1);
    }

    private static async UniTask<byte[]> ReadFileWhenReadyAsync(string path, TimeSpan timeout, TimeSpan pollInterval)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        DateTime start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (File.Exists(path))
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    if (bytes != null && bytes.Length > 0)
                    {
                        return bytes;
                    }
                }
                catch
                {
                }
            }

            await UniTask.Delay(pollInterval);
        }

        return null;
    }

    private static void TryDeleteTempFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    public readonly struct PlayOptions
    {
        public int DurationMs { get; }
        public IReadOnlyList<InputRequest> Inputs { get; }
        public int MaxHeight { get; }
        public int MaxBase64Bytes { get; }

        public PlayOptions(int durationMs, IReadOnlyList<InputRequest> inputs,
            int maxHeight = 640, int maxBase64Bytes = 50_000_000)
        {
            DurationMs = durationMs;
            Inputs = inputs ?? Array.Empty<InputRequest>();
            MaxHeight = maxHeight;
            MaxBase64Bytes = maxBase64Bytes;
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

    public readonly struct CaptureResult
    {
        public bool IsError { get; }
        public string Base64Data { get; }
        public string MimeType { get; }
        public string ErrorMessage { get; }

        private CaptureResult(bool isError, string base64Data, string mimeType, string errorMessage)
        {
            IsError = isError;
            Base64Data = base64Data;
            MimeType = mimeType;
            ErrorMessage = errorMessage;
        }

        public static CaptureResult Success(string base64Data, string mimeType)
        {
            return new CaptureResult(false, base64Data, mimeType, null);
        }

        public static CaptureResult Error(string message)
        {
            return new CaptureResult(true, null, null, message);
        }
    }

    private static CaptureResult ScaleCaptureToMaxHeight(CaptureResult captureResult, int maxHeight)
    {
        if (captureResult.IsError)
        {
            return captureResult;
        }

        if (string.IsNullOrWhiteSpace(captureResult.Base64Data))
        {
            return CaptureResult.Error("Captured screenshot data was empty.");
        }

        byte[] png_bytes;
        try
        {
            png_bytes = Convert.FromBase64String(captureResult.Base64Data);
        }
        catch (Exception ex)
        {
            return CaptureResult.Error($"Captured screenshot data was not valid base64: {ex.Message}");
        }

        Texture2D temp_texture = new(2, 2);
        try
        {
            if (!temp_texture.LoadImage(png_bytes, false))
            {
                return CaptureResult.Error("Failed to decode screenshot image.");
            }

            GetScaledDimensionsToMaxHeight(temp_texture.width, temp_texture.height, maxHeight, out int scaled_width, out int scaled_height);

            if (temp_texture.width == scaled_width && temp_texture.height == scaled_height)
            {
                return captureResult;
            }

            CaptureResult scaled_result = ScalePngImage(png_bytes, scaled_width, scaled_height);
            if (scaled_result.IsError)
            {
                return scaled_result;
            }

            return CaptureResult.Success(scaled_result.Base64Data, captureResult.MimeType ?? DefaultMimeType);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(temp_texture);
        }
    }
}
