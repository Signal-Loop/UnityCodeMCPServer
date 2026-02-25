using UnityEngine;
using UnityEditor;
using System;
using System.Text.RegularExpressions;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Services;
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
            var executionService = new ScriptExecutionService();
            var result = await executionService.ExecuteScriptAsync(_code);

            var output = FormatExecutionOutput(result);
            LoopLogger.Info($"Script execution result:\n{output}");
        }
        catch (Exception e)
        {
            LoopLogger.Error($"Error executing script: {e}");
        }

        // Repaint the window to show the new result.
        Repaint();
    }

    private string FormatExecutionOutput(ScriptExecutionService.ExecutionResult result)
    {
        var output = new System.Text.StringBuilder();
        output.AppendLine($"Status: {result.Status}");

        if (!string.IsNullOrWhiteSpace(result.ResultText))
        {
            output.AppendLine($"Result: {result.ResultText}");
        }

        if (!string.IsNullOrWhiteSpace(result.Logs))
        {
            output.AppendLine($"Logs:\n{result.Logs}");
        }

        if (!string.IsNullOrWhiteSpace(result.Errors))
        {
            output.AppendLine($"Errors:\n{result.Errors}");
        }

        return output.ToString();
    }
}