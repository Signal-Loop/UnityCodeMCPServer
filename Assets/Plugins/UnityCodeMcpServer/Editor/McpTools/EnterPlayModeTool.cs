using System.Collections.Generic;
using System.Text.Json;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityEditor;
using UnityEngine;
/// <summary>
/// Tool that enters Unity Play Mode in the Editor.
/// NOTE: This is a synchronous tool that returns immediately after triggering play mode.
/// It does NOT wait for play mode to complete because Unity performs a domain reload
/// during play mode transition which would kill the async context and TCP connection.
/// </summary>
public class EnterPlayModeTool : ITool
{
    public string Name => "enter_play_mode";

    public string Description => "Enters Unity Play Mode in the Editor.";

    public JsonElement InputSchema => JsonHelper.ParseElement(@"
        {
            ""type"": ""object"",
            ""properties"": {}
        }
        ");

    public ToolsCallResult Execute(JsonElement arguments)
    {
        if (EditorApplication.isPlaying)
        {
            return ToolsCallResult.TextResult("Already in Play Mode.");
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return ToolsCallResult.ErrorResult("Play Mode transition already in progress.");
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return ToolsCallResult.ErrorResult("Unity is compiling or updating. Try again once the Editor is idle.");
        }

        LoopLogger.Debug($"{McpProtocol.LogPrefix} EnterPlayModeTool: triggering play mode.");

        // Set isPlaying directly. The property assignment is synchronous but the actual
        // play mode transition (and domain reload) happens on the next editor update.
        // This gives time for the response to be sent before the connection drops.
        // Note: delayCall was avoided because it requires editor focus to execute.
        EditorApplication.isPlaying = true;

        Time.timeScale = 0;

        return ToolsCallResult.TextResult("Play Mode transition initiated.");
    }
}
