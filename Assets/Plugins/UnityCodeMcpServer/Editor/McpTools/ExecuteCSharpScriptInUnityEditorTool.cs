using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Services;
using UnityEngine;
using UnityEditor;

namespace UnityCodeMcpServer.McpTools
{
    /// <summary>
    /// Executes arbitrary C# script text inside the Unity Editor using Roslyn.
    /// Captures return value, logs, and errors into a single response payload.
    /// </summary>
    public class ExecuteCSharpScriptInUnityEditor : IToolAsync
    {
        public string Name => "execute_csharp_script_in_unity_editor";

        // Engineered Prompt: Uses XML structure for progressive disclosure, explicitly lists
        // pre-imported namespaces, and enforces strict behavioral rules for the LLM.
        public string Description =>
    @"<tool_description>
Executes a C# script in the Unity Editor context using Roslyn scripting. Use this tool to interact with, query, or modify the Unity Editor and its loaded project.
</tool_description>

<when_to_use>
- Modifying scene objects, GameObjects, Components, Transforms, or UI elements.
- Querying the current Editor or scene state (e.g., listing objects, reading component values).
- Creating or modifying Prefabs, ScriptableObjects, or other assets via AssetDatabase.
- Batch-processing or automating Editor tasks.
- Computing values using Unity math APIs (Mathf, Vector3, Quaternion, Physics) to avoid calculation errors.
</when_to_use>

<when_not_to_use>
- Do NOT use to edit C# source files — use file editing tools instead.
- Do NOT use to read/write plain text/JSON/YAML files — use file tools instead.
- Do NOT use to install packages or change ProjectSettings — requires dedicated tools.
</when_not_to_use>

<environment>
- PRE-IMPORTED NAMESPACES: `System`, `System.Collections.Generic`, `System.Linq`, `UnityEngine`, `UnityEditor`. Do NOT add `using` statements for these.
- Full access to all project assemblies is available.
- Synchronous host environment (Main Thread).
- The active scene is automatically marked dirty after successful execution in edit mode.
</environment>

<rules>
1. TOP-LEVEL STATEMENTS ONLY: Write flat code. Do not wrap code in a class or a method body.
2. EXPLICIT USINGS: Only declare namespaces NOT in the pre-imported list (e.g., `using UnityEngine.UI;`).
3. NO ASYNC/AWAIT: Do not use async methods or Task-based APIs.
4. NO BACKGROUND THREADS: All Editor API calls must occur on the main thread. Do not use Task.Run.
5. OUTPUT CAPTURE: The tool captures `Debug.Log()`, `Debug.LogError()`, and the final evaluated expression. Rely primarily on `Debug.Log()` to return structured data to yourself.
</rules>

<examples>
<example>
<intent>Find a player, handle null, and get position</intent>
<script>
var go = GameObject.Find(""Player"");
if (go == null) { 
    Debug.LogError(""Player not found""); 
    return; 
}
Debug.Log($""Player position: {go.transform.position}"");
</script>
</example>
</examples>";

        // Explicit JSON schema instruction to prevent markdown wrapping
        public JsonElement InputSchema => JsonHelper.ParseElement(@"
        {
            ""type"": ""object"",
            ""properties"": {
                ""script"": {
                    ""type"": ""string"",
                    ""description"": ""The raw C# script to execute. MUST NOT be wrapped in markdown blocks (do not use ```csharp). Provide the raw text only.""
                }
            },
            ""required"": [""script""]
        }
        ");

        public async UniTask<ToolsCallResult> ExecuteAsync(JsonElement arguments)
        {
            var script = arguments.GetStringOrDefault("script", string.Empty)?.Trim();
            if (string.IsNullOrWhiteSpace(script))
            {
                return CreateToolCallResult(isError: true, status: "error", resultText: null, logs: null, errors: "Script is empty or missing.", script: script);
            }

            var executionService = new ScriptExecutionService();
            var result = await executionService.ExecuteScriptAsync(script);

            var toolCallResult = CreateToolCallResult(
                isError: !result.IsSuccess,
                status: result.Status,
                resultText: result.ResultText,
                logs: result.Logs,
                errors: result.Errors,
                script: script,
                assemblies: result.LoadedAssemblies);

            LogToolCallResult(toolCallResult);
            return toolCallResult;
        }


        private static ToolsCallResult CreateToolCallResult(bool isError, string status, string resultText, string logs, string errors, string script, string[] assemblies = null)
        {
            var response = new StringBuilder();
            string mode = EditorApplication.isPlaying ? "Play Mode" : "Edit Mode";
            response.AppendLine($"**Unity Editor is in {mode}**");
            response.AppendLine();
            response.AppendLine($"### Status: {status}");

            if (!string.IsNullOrWhiteSpace(resultText))
            {
                response.AppendLine("### Result");
                response.AppendLine("```text");
                response.AppendLine(resultText);
                response.AppendLine("```");
            }

            response.AppendLine("### Script");
            response.AppendLine("```csharp");
            response.AppendLine(string.IsNullOrWhiteSpace(script) ? "(not provided)" : script);
            response.AppendLine("```");

            response.AppendLine("### Logs");
            response.AppendLine(string.IsNullOrWhiteSpace(logs) ? "(none)" : logs.TrimEnd());

            response.AppendLine("### Errors");
            response.Append(string.IsNullOrWhiteSpace(errors) ? "(none)" : errors.TrimEnd());

            if (assemblies != null && assemblies.Length > 0)
            {
                response.AppendLine();
                response.AppendLine("### Loaded Assemblies");
                foreach (var assembly in assemblies)
                {
                    response.AppendLine($"- {assembly}");
                }
            }

            return ToolsCallResult.TextResult(response.ToString(), isError);
        }

        private static void LogToolCallResult(ToolsCallResult result)
        {
            string text = string.Empty;
            if (result.Content != null && result.Content.Count > 0)
            {
                var first = result.Content[0];
                text = first != null ? first.Text ?? string.Empty : string.Empty;
            }

            if (result.IsError)
            {
                LoopLogger.Error($"{McpProtocol.LogPrefix} ExecuteCSharpScriptInUnityEditor result:\n{text}");
            }
            else
            {
                LoopLogger.Debug($"{McpProtocol.LogPrefix} ExecuteCSharpScriptInUnityEditor result:\n{text}");
            }
        }
    }
}