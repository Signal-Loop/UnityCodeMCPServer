using System.Text.Json;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityEditor;
using UnityEngine;
/// <summary>
/// Tool that exits Unity Play Mode in the Editor.
/// This async tool waits for the play mode transition to complete before resetting Time.timeScale.
/// </summary>
public class ExitPlayModeTool : IToolAsync
{
    public string Name => "exit_play_mode";

    public string Description => "Exits Unity Play Mode in the Editor. Returns immediately after triggering exit. Note: Unity will perform a domain reload which may briefly disconnect the MCP server.";

    public JsonElement InputSchema => JsonHelper.ParseElement(@"
        {
            ""type"": ""object"",
            ""properties"": {}
        }
        ");

    public async UniTask<ToolsCallResult> ExecuteAsync(JsonElement arguments)
    {
        if (!EditorApplication.isPlaying)
        {
            return ToolsCallResult.ErrorResult("Unity is not in Play Mode.");
        }

        UnityCodeMcpServerLogger.Debug($"ExitPlayModeTool: triggering exit play mode.");

        EditorApplication.isPlaying = false;
        await UniTask.Delay(1, DelayType.Realtime);
        Time.timeScale = 1;

        return ToolsCallResult.TextResult("Exit Play Mode transition initiated.");
    }
}
