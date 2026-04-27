using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tests for PlayUnityGameTool - simplified version using StateEvent approach.
/// </summary>
public class PlayUnityGameToolTests
{
    private static MethodInfo GetPrivateMethod(string name) =>
        typeof(PlayUnityGameTool).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);

    private static MethodInfo GetPrivateStaticMethod(string name) =>
        typeof(PlayUnityGameTool).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);

    private static MethodInfo GetResolverMethod(string name) =>
        Type.GetType("UnityCodeMcpServer.Helpers.InputActionAssetResolver, UnityCodeMcpServer")
            ?.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

    private static FieldInfo GetPrivateField(string name) =>
        typeof(PlayUnityGameTool).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

    private static Keyboard EnsureKeyboardDevice(out bool createdKeyboard)
    {
        Keyboard keyboard = Keyboard.current;
        createdKeyboard = keyboard == null;
        return keyboard ?? InputSystem.AddDevice<Keyboard>();
    }

    private static InputActionAsset CreateKeyboardTestAsset()
    {
        InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
        InputActionMap map = new("TestGameplay");
        map.AddAction("Player1Up", InputActionType.Button, "<Keyboard>/w");
        map.AddAction("Player2Up", InputActionType.Button, "<Keyboard>/upArrow");
        asset.AddActionMap(map);
        return asset;
    }

    [Test]
    public void ResolveInputActionAssetPath_UsesConfiguredSettingsPathFirst()
    {
        MethodInfo resolveMethod = GetResolverMethod("ResolveInputActionAssetPath");
        Assert.IsNotNull(resolveMethod, "Could not find InputActionAssetResolver.ResolveInputActionAssetPath method.");

        object[] args =
        {
            "Assets/Configured.inputactions",
            new[] { "Assets/FallbackProject.inputactions" },
            new[] { "Packages/pkg/FallbackAny.inputactions" },
            null,
            null
        };

        string resolvedPath = (string)resolveMethod.Invoke(null, args);

        Assert.That(resolvedPath, Is.EqualTo("Assets/Configured.inputactions"));
        Assert.That((bool)args[3], Is.False);
        Assert.That(args[4], Is.Null);
    }

    [Test]
    public void ResolveInputActionAssetPath_UsesFirstProjectAssetWhenSettingsUnset()
    {
        MethodInfo resolveMethod = GetResolverMethod("ResolveInputActionAssetPath");
        Assert.IsNotNull(resolveMethod, "Could not find InputActionAssetResolver.ResolveInputActionAssetPath method.");

        object[] args =
        {
            string.Empty,
            new[] { "Assets/Inputs/A.inputactions", "Assets/Inputs/B.inputactions" },
            new[] { "Packages/pkg/FallbackAny.inputactions" },
            null,
            null
        };

        string resolvedPath = (string)resolveMethod.Invoke(null, args);

        Assert.That(resolvedPath, Is.EqualTo("Assets/Inputs/A.inputactions"));
        Assert.That((bool)args[3], Is.True);
        StringAssert.Contains("Assets/Inputs/A.inputactions", (string)args[4]);
    }

    [Test]
    public void ResolveInputActionAssetPath_UsesFirstAvailableAssetWhenProjectHasNone()
    {
        MethodInfo resolveMethod = GetResolverMethod("ResolveInputActionAssetPath");
        Assert.IsNotNull(resolveMethod, "Could not find InputActionAssetResolver.ResolveInputActionAssetPath method.");

        object[] args =
        {
            string.Empty,
            Array.Empty<string>(),
            new[] { "Packages/pkg/FallbackAny.inputactions", "Library/Other.inputactions" },
            null,
            null
        };

        string resolvedPath = (string)resolveMethod.Invoke(null, args);

        Assert.That(resolvedPath, Is.EqualTo("Packages/pkg/FallbackAny.inputactions"));
        Assert.That((bool)args[3], Is.True);
        StringAssert.Contains("Packages/pkg/FallbackAny.inputactions", (string)args[4]);
    }

    [Test]
    public void ResolveInputActionAssetPath_ReturnsNullWhenNoAssetsExist()
    {
        MethodInfo resolveMethod = GetResolverMethod("ResolveInputActionAssetPath");
        Assert.IsNotNull(resolveMethod, "Could not find InputActionAssetResolver.ResolveInputActionAssetPath method.");

        object[] args =
        {
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<string>(),
            null,
            null
        };

        string resolvedPath = (string)resolveMethod.Invoke(null, args);

        Assert.That(resolvedPath, Is.Null);
        Assert.That((bool)args[3], Is.False);
        Assert.That(args[4], Is.Null);
    }

    [Test]
    public void TriggerAction_WithTwoKeyboardActions_BothRemainPressed()
    {
        Keyboard keyboard = EnsureKeyboardDevice(out bool createdKeyboard);
        Assert.IsNotNull(keyboard, "Expected a keyboard device for keyboard action tests.");

        InputActionAsset asset = CreateKeyboardTestAsset();

        InputAction player1Up = asset.FindAction("Player1Up", true);
        InputAction player2Up = asset.FindAction("Player2Up", true);

        player1Up.Enable();
        player2Up.Enable();

        MethodInfo triggerActionMethod = typeof(PlayUnityGameTool).GetMethod("TriggerAction", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(triggerActionMethod, "Could not access PlayUnityGameTool.TriggerAction through reflection.");

        PlayUnityGameTool tool = new();

        try
        {
            triggerActionMethod.Invoke(tool, new object[] { player1Up, 1f });
            InputSystem.Update();

            triggerActionMethod.Invoke(tool, new object[] { player2Up, 1f });
            InputSystem.Update();

            Assert.IsTrue(player1Up.IsPressed(), "Expected Player1Up to stay pressed after Player2Up press.");
            Assert.IsTrue(player2Up.IsPressed(), "Expected Player2Up to be pressed.");
        }
        finally
        {
            triggerActionMethod.Invoke(tool, new object[] { player1Up, 0f });
            triggerActionMethod.Invoke(tool, new object[] { player2Up, 0f });
            InputSystem.Update();

            player1Up.Disable();
            player2Up.Disable();
            UnityEngine.Object.DestroyImmediate(asset);

            if (createdKeyboard)
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }
    }

    [Test]
    public void ResetAllInputDevices_AfterClearingTrackedKeyboards_StillResetsResidualKeyboardState()
    {
        // This test reproduces the bug where the paddle moves when only duration is provided.
        // Root cause: _active_keys_by_keyboard.Clear() at the start of ExecuteAsync wipes
        // the tracking dictionary, so ResetAllKeyboards() in the finally block (or at the
        // start of a new invocation) early-returns without resetting any keyboards.
        // Residual key presses from a previous invocation keep _gameState.VerticalInput != 0.

        Keyboard keyboard = EnsureKeyboardDevice(out bool createdKeyboard);
        Assert.IsNotNull(keyboard, "Expected a keyboard device for keyboard action tests.");

        InputActionAsset asset = CreateKeyboardTestAsset();

        InputAction player1Up = asset.FindAction("Player1Up", true);
        player1Up.Enable();

        MethodInfo triggerAction = GetPrivateMethod("TriggerAction");
        MethodInfo resetAllInputDevices = GetPrivateMethod("ResetAllInputDevices");
        FieldInfo activeKeysField = GetPrivateField("_active_keys_by_keyboard");
        Assert.IsNotNull(triggerAction, "Could not find TriggerAction method.");
        Assert.IsNotNull(resetAllInputDevices, "Could not find ResetAllInputDevices method.");
        Assert.IsNotNull(activeKeysField, "Could not find _active_keys_by_keyboard field.");

        PlayUnityGameTool tool = new();

        try
        {
            // Simulate a previous invocation that pressed a key.
            triggerAction.Invoke(tool, new object[] { player1Up, 1f });
            InputSystem.Update();
            Assert.IsTrue(player1Up.IsPressed(), "Player1Up should be pressed after TriggerAction.");

            // Simulate the start of a NEW invocation: clear the tracking dictionary
            // (this is what ExecuteAsync does at the top).
            Dictionary<Keyboard, HashSet<Key>> activeKeys = (Dictionary<Keyboard, HashSet<Key>>)activeKeysField.GetValue(tool);
            activeKeys.Clear();

            // Call ResetAllInputDevices — it should release the residual key state.
            resetAllInputDevices.Invoke(tool, null);
            InputSystem.Update();

            Assert.IsFalse(player1Up.IsPressed(),
                "Expected Player1Up to be released after ResetAllInputDevices, but it is still pressed. " +
                "Residual keyboard state from a previous invocation causes phantom paddle movement.");
        }
        finally
        {
            triggerAction.Invoke(tool, new object[] { player1Up, 0f });
            InputSystem.Update();
            player1Up.Disable();
            UnityEngine.Object.DestroyImmediate(asset);

            if (createdKeyboard)
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }
    }

    [Test]
    public void ResetAllInputDevices_ResetsResidualGamepadButtonState()
    {
        MethodInfo triggerAction = GetPrivateMethod("TriggerAction");
        MethodInfo resetAllInputDevices = GetPrivateMethod("ResetAllInputDevices");
        Assert.IsNotNull(triggerAction, "Could not find TriggerAction method.");
        Assert.IsNotNull(resetAllInputDevices, "Could not find ResetAllInputDevices method.");

        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        InputAction action = new(name: "TestGamepadButton", type: InputActionType.Button, binding: "<Gamepad>/buttonSouth");
        action.Enable();

        PlayUnityGameTool tool = new();

        try
        {
            triggerAction.Invoke(tool, new object[] { action, 1f });
            InputSystem.Update();

            Assert.IsTrue(action.IsPressed(), "Gamepad button should be pressed after TriggerAction.");

            resetAllInputDevices.Invoke(tool, null);
            InputSystem.Update();

            Assert.IsFalse(action.IsPressed(),
                "Expected the gamepad button state to be cleared between invocations, but it remained pressed.");
        }
        finally
        {
            action.Disable();
            action.Dispose();
            InputSystem.RemoveDevice(gamepad);
        }
    }

    [Test]
    public void ResetAllInputDevices_ResetsResidualGamepadAxisState()
    {
        MethodInfo triggerAction = GetPrivateMethod("TriggerAction");
        MethodInfo resetAllInputDevices = GetPrivateMethod("ResetAllInputDevices");
        Assert.IsNotNull(triggerAction, "Could not find TriggerAction method.");
        Assert.IsNotNull(resetAllInputDevices, "Could not find ResetAllInputDevices method.");

        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        InputAction action = new(name: "TestGamepadAxis", type: InputActionType.Value, binding: "<Gamepad>/leftStick/x");
        action.Enable();

        PlayUnityGameTool tool = new();

        try
        {
            triggerAction.Invoke(tool, new object[] { action, 1f });
            InputSystem.Update();

            Assert.AreEqual(1f, action.ReadValue<float>(), 0.0001f,
                "Gamepad axis should reflect the queued value after TriggerAction.");

            resetAllInputDevices.Invoke(tool, null);
            InputSystem.Update();

            Assert.AreEqual(0f, action.ReadValue<float>(), 0.0001f,
                "Expected the gamepad axis state to be cleared between invocations, but it remained non-zero.");
        }
        finally
        {
            action.Disable();
            action.Dispose();
            InputSystem.RemoveDevice(gamepad);
        }
    }
}
