using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityCodeMcpServer.AsyncAwait;
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

    public JToken InputSchema => JsonHelper.ParseElement(@"
        {
            ""type"": ""object"",
            ""properties"": {}
        }
        ");

    public async Task<ToolsCallResult> ExecuteAsync(JToken arguments)
    {
        if (!EditorApplication.isPlaying)
        {
            Time.timeScale = 1;
            return ToolsCallResult.TextResult("Unity is already in Edit Mode.");
        }

        UnityCodeMcpServerLogger.Debug("ExitPlayModeTool: triggering exit play mode.");

        EditorApplication.isPlaying = false;
        await UnityEditorAsync.DelayRealtimeAsync(1);
        Time.timeScale = 1;

        return ToolsCallResult.TextResult("Exit Play Mode transition initiated.");
    }
}
