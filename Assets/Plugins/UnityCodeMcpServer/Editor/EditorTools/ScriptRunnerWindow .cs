using UnityEngine;
using UnityEditor;
using System;
using System.Text.RegularExpressions;
using System.Text.Json;
using UnityEditor.Compilation;

public class ScriptRunnerWindow : EditorWindow
{
    private string _code = "return 10 + 32;";
    private Vector2 _scrollPosition;

    // Create a menu item to open the window. [3]
    [MenuItem("Tools/UnityCodeMcpServer/Script Runner")]
    public static void ShowWindow()
    {
        GetWindow<ScriptRunnerWindow>("UnityCodeMcpServer Script Runner");
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginVertical();

        // Add a text area for code input. [4]
        GUILayout.Label("Enter C# Code to Execute", EditorStyles.boldLabel);
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        _code = EditorGUILayout.TextArea(_code, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("Execute"))
        {
            ExecuteCode();
        }

        if (GUILayout.Button("Request Script Compilation and Reload"))
        {
            CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
        }

        EditorGUILayout.EndVertical();
    }

    private async void ExecuteCode()
    {
        try
        {
            // Use the shared ExecuteCSharpScriptInUnityEditor tool which runs scripts in the Unity Editor context and captures logs/errors.
            var tool = new UnityCodeMcpServer.Tools.ExecuteCSharpScriptInUnityEditor();
            var inputJson = System.Text.Json.JsonSerializer.Serialize(new { script = _code });
            using var doc = JsonDocument.Parse(inputJson);
            var result = await tool.ExecuteAsync(doc.RootElement);

            // Show the tool's formatted response (first content item) if available.
            string output = "(no content)";
            if (result != null && result.Content != null && result.Content.Count > 0)
            {
                output = result.Content[0].Text ?? "(empty)";
            }

            Debug.Log($"ExecuteCSharpScriptInUnityEditor result:\n{output}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error executing script with ExecuteCSharpScriptInUnityEditor: {e}");
        }

        // Repaint the window to show the new result.
        Repaint();
    }
}