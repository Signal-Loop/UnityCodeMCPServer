using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Settings;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using UnityEngine;

namespace UnityCodeMcpServer.Tools
{
    /// <summary>
    /// Executes arbitrary C# script text inside the Unity Editor using Roslyn.
    /// Captures return value, logs, and errors into a single response payload.
    /// </summary>
    public class ScriptExecutionTool : IToolAsync
    {
        public string Name => "execute_csharp_script_in_unity_editor";

        public string Description =>
    @"Use this tool to perform changes or automate tasks in Unity Editor by creating and executing C# scripts.
Scripts run in the Unity Editor context using Roslyn with full access to UnityEngine, UnityEditor, and any project assembly. 
Perfect for creating GameObjects, modifying scenes, configuring components, or automating Unity Editor tasks. 
Returns execution status, output, and any logs/errors.

**ALWAYS use `execute_csharp_script_in_unity_editor` tool for ANY Unity Editor modifications or automation tasks.**

**ALWAYS prefer `execute_csharp_script_in_unity_editor` tool to modification of Unity Yaml files.**

### When to Use This Tool (Use for ALL of these scenarios):
- Creating, modifying, or deleting GameObjects in scenes
- Adding, configuring, or removing Components
- Adjusting Transform properties (position, rotation, scale)
- Setting up UI elements and Canvas hierarchies
- Creating or modifying Prefabs
- Configuring ScriptableObject instances
- Scene management (creating, loading, switching scenes)
- Asset manipulation (importing, configuring, organizing, modifying)
- Batch operations on multiple GameObjects
- Editor window automation
- Project structure setup
- ANY task that modifies Unity Editor state

### Why This Tool is Required:
- **Direct execution**: Scripts run immediately in the Unity Editor context using Roslyn
- **Full API access**: Complete access to UnityEngine, UnityEditor, and all project assemblies
- **Immediate feedback**: Returns execution status, output, and logs instantly
- **Scene persistence**: Automatically marks scenes dirty after execution
- **Selection context**: Automatically captures current Unity Editor selection";

        public JsonElement InputSchema => JsonHelper.ParseElement(@"
        {
            ""type"": ""object"",
            ""properties"": {
                ""script"": {
                    ""type"": ""string"",
                    ""description"": ""C# script text to execute in the Unity Editor context""
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

            var options = CreateScriptOptions();

            string[] assemblies = options.MetadataReferences.Select(r => r.Display).Where(d => !string.IsNullOrWhiteSpace(d)).ToArray();

            var logCapture = new LogCapture();
            string errorDetails;
            try
            {
                logCapture.Start();
                object executionResult = await CSharpScript.EvaluateAsync(script, options);
                logCapture.Stop();
                var hasLoggedErrors = logCapture.HasErrors;
                var statusLabel = hasLoggedErrors ? "success_with_errors" : "success";
                var errorsText = hasLoggedErrors ? logCapture.ErrorLog : null;
                var toolCallResult = CreateToolCallResult(isError: false, status: statusLabel, resultText: FormatResult(executionResult), logs: logCapture.Logs, errors: errorsText, script: script, assemblies: assemblies);
                LogToolCallResult(toolCallResult);
                return toolCallResult;
            }
            catch (CompilationErrorException compilationError)
            {
                logCapture.Stop();
                errorDetails = string.Join(Environment.NewLine, compilationError.Diagnostics);
                Debug.LogError($"{McpProtocol.LogPrefix} Script execution compilation error:\n{errorDetails}");
                var toolCallResult = CreateToolCallResult(isError: true, status: "compilation_error", resultText: null, logs: logCapture.Logs, errors: errorDetails, script: script, assemblies: assemblies);
                LogToolCallResult(toolCallResult);
                return toolCallResult;
            }
            catch (Exception ex)
            {
                logCapture.Stop();
                errorDetails = ex.ToString();
                Debug.LogError($"{McpProtocol.LogPrefix} Script execution runtime error:\n{errorDetails}");
                var toolCallResult = CreateToolCallResult(isError: true, status: "execution_error", resultText: null, logs: logCapture.Logs, errors: errorDetails, script: script, assemblies: assemblies);
                LogToolCallResult(toolCallResult);
                return toolCallResult;
            }
            finally
            {
                logCapture.Dispose();
            }
        }

        private static ToolsCallResult CreateToolCallResult(bool isError, string status, string resultText, string logs, string errors, string script, string[] assemblies = null)
        {
            var response = new StringBuilder();
            response.AppendLine($"### Status: {status}");
            if (!string.IsNullOrWhiteSpace(resultText))
            {
                response.AppendLine($"### Result");
                response.AppendLine("```");
                response.AppendLine(resultText);
                response.AppendLine("```");
            }

            response.AppendLine("### Script");
            response.AppendLine("```");
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

            return new ToolsCallResult
            {
                IsError = isError,
                Content = new System.Collections.Generic.List<ContentItem>
                {
                    ContentItem.TextContent(response.ToString())
                }
            };
        }

        private static string FormatResult(object executionResult)
        {
            if (executionResult == null)
            {
                return "(null)";
            }

            return executionResult.ToString();
        }

        private static ScriptOptions CreateScriptOptions()
        {
            return ScriptOptions.Default
                .WithReferences(ResolveAssemblies())
                .WithImports("System", "System.Collections.Generic", "System.Linq", "UnityEngine", "UnityEditor")
                .WithOptimizationLevel(Microsoft.CodeAnalysis.OptimizationLevel.Release);
        }

        private static System.Reflection.Assembly[] ResolveAssemblies()
        {
            var settings = UnityCodeMcpServerSettings.Instance;
            var assemblyNames = settings.GetAllAssemblyNames();

            var loaded = AppDomain.CurrentDomain.GetAssemblies();
            return assemblyNames
                .Select(name => loaded.FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.Ordinal)))
                .Where(a => a != null)
                .ToArray();
        }

        private static void LogToolCallResult(ToolsCallResult result)
        {
            var text = result.Content != null && result.Content.Count > 0 ? result.Content[0].Text : string.Empty;
            if (result.IsError)
            {
                Debug.LogError($"{McpProtocol.LogPrefix} ScriptExecutionTool result:\n{text}");
            }
            else
            {
                Debug.Log($"{McpProtocol.LogPrefix} ScriptExecutionTool result:\n{text}");
            }
        }

        private sealed class LogCapture : IDisposable
        {
            private readonly StringBuilder _logBuilder = new StringBuilder();
            private readonly StringBuilder _errorBuilder = new StringBuilder();
            private bool _isRunning;

            public string Logs => CombineLogs();

            public string ErrorLog => _errorBuilder.ToString();

            public bool HasErrors => _errorBuilder.Length > 0;

            public void Start()
            {
                if (_isRunning)
                {
                    return;
                }

                Application.logMessageReceived += HandleLog;
                _isRunning = true;
            }

            public void Stop()
            {
                if (!_isRunning)
                {
                    return;
                }

                Application.logMessageReceived -= HandleLog;
                _isRunning = false;
            }

            public void Dispose()
            {
                Stop();
            }

            private void HandleLog(string condition, string stackTrace, LogType type)
            {
                switch (type)
                {
                    case LogType.Error:
                    case LogType.Exception:
                    case LogType.Assert:
                        _errorBuilder.AppendLine(condition);
                        if (!string.IsNullOrWhiteSpace(stackTrace))
                        {
                            _errorBuilder.AppendLine(stackTrace);
                        }
                        break;
                    default:
                        _logBuilder.AppendLine(condition);
                        break;
                }
            }

            private string CombineLogs()
            {
                if (_errorBuilder.Length == 0 && _logBuilder.Length == 0)
                {
                    return string.Empty;
                }

                if (_errorBuilder.Length == 0)
                {
                    return _logBuilder.ToString();
                }

                if (_logBuilder.Length == 0)
                {
                    return _errorBuilder.ToString();
                }

                var combined = new StringBuilder();
                combined.AppendLine("Standard Log:");
                combined.AppendLine(_logBuilder.ToString().TrimEnd());
                combined.AppendLine("Errors Log:");
                combined.Append(_errorBuilder.ToString().TrimEnd());
                return combined.ToString();
            }
        }
    }
}
