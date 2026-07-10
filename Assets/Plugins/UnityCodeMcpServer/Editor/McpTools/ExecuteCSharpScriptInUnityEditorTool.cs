using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Services;
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

        public string Description =>
    @"Executes a C# script in the Unity Editor context using Roslyn. Use this for scene, GameObject, component, prefab, asset and Unity Editor modifications or automation. Provide raw C# top-level statements in the script argument.";

        public JToken InputSchema => JsonHelper.ParseElement(@"
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

        public async Task<ToolsCallResult> ExecuteAsync(JToken arguments)
        {
            string script = arguments.GetStringOrDefault("script", string.Empty)?.Trim();
            if (string.IsNullOrWhiteSpace(script))
            {
                return CreateToolCallResult(isError: true, status: "error", resultText: null, logs: null, errors: "Script is empty or missing.");
            }

            ToolsCallResult compilationBlockedResult = GetCompilationBlockedResult();
            if (compilationBlockedResult != null)
            {
                return compilationBlockedResult;
            }

            ScriptExecutionService executionService = new();
            ScriptExecutionService.ExecutionResult result = await executionService.ExecuteScriptAsync(script);

            ToolsCallResult toolCallResult = CreateToolCallResult(
                isError: !result.IsSuccess,
                status: result.Status,
                resultText: result.ResultText,
                logs: result.Logs,
                errors: result.Errors,
                assemblies: result.LoadedAssemblies);

            LogToolCallResult(toolCallResult);
            return toolCallResult;
        }

        public static ToolsCallResult BuildCompilationBlockedResult(bool isCompiling, bool hasCompileErrors)
        {
            string message = EditorCompilationGate.BuildBlockedMessage("execute C# scripts", isCompiling, hasCompileErrors);
            return CreateToolCallResult(isError: true, status: "error", resultText: null, logs: null, errors: message);
        }

        private static ToolsCallResult GetCompilationBlockedResult()
        {
            if (!EditorCompilationGate.TryGetBlockedMessage("execute C# scripts", out string message))
            {
                return null;
            }

            return CreateToolCallResult(isError: true, status: "error", resultText: null, logs: null, errors: message);
        }

        private static ToolsCallResult CreateToolCallResult(bool isError, string status, string resultText, string logs, string errors, string[] assemblies = null)
        {
            StringBuilder response = new();
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

            response.AppendLine("### Logs");
            response.AppendLine(string.IsNullOrWhiteSpace(logs) ? "(none)" : logs.TrimEnd());

            response.AppendLine("### Errors");
            response.Append(string.IsNullOrWhiteSpace(errors) ? "(none)" : errors.TrimEnd());

            if (assemblies != null && assemblies.Length > 0)
            {
                response.AppendLine();
                response.AppendLine("### Loaded Assemblies");
                foreach (string assembly in assemblies)
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
                ContentItem first = result.Content[0];
                text = first != null ? first.Text ?? string.Empty : string.Empty;
            }

            if (result.IsError)
            {
                UnityCodeMcpServerLogger.Error($"ExecuteCSharpScriptInUnityEditor result:\n{text}");
            }
            else
            {
                UnityCodeMcpServerLogger.Debug($"ExecuteCSharpScriptInUnityEditor result:\n{text}");
            }
        }
    }
}
