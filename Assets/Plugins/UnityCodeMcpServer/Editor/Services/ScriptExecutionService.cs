using System;
using System.Linq;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Settings;
using UnityCodeMcpServer.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using UnityEngine;
using UnityEditor;

namespace UnityCodeMcpServer.Services
{
    /// <summary>
    /// Service for executing C# scripts in the Unity Editor context.
    /// Handles assembly resolution, script compilation, and execution.
    /// </summary>
    public class ScriptExecutionService
    {
        /// <summary>
        /// Result of script execution containing all relevant information.
        /// </summary>
        public class ExecutionResult
        {
            public bool IsSuccess { get; set; }
            public string Status { get; set; }
            public object ResultValue { get; set; }
            public string ResultText { get; set; }
            public string Logs { get; set; }
            public string Errors { get; set; }
            public string[] LoadedAssemblies { get; set; }
        }

        /// <summary>
        /// Executes a C# script in the Unity Editor context using Roslyn.
        /// </summary>
        /// <param name="script">The C# script code to execute</param>
        /// <returns>Execution result containing status, output, and errors</returns>
        public async UniTask<ExecutionResult> ExecuteScriptAsync(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                return new ExecutionResult
                {
                    IsSuccess = false,
                    Status = "ERROR",
                    Errors = "Script is empty or missing."
                };
            }

            // Strip accidental markdown formatting if present
            script = StripMarkdownFormatting(script);

            var assemblies = ResolveAssemblies();
            var options = CreateScriptOptions(assemblies);
            var assembliesDisplay = GetLoadedAssembliesDisplay(assemblies);

            var logCapture = new LogCapture();
            string errorDetails;

            try
            {
                logCapture.Start();

                // Execute the script
                object executionResult = await CSharpScript.EvaluateAsync(script, options);

                logCapture.Stop();

                // Mark scene dirty after successful execution
                MarkActiveSceneDirtyIfNeeded();

                return CreateSuccessResult(executionResult, logCapture, assembliesDisplay);
            }
            catch (CompilationErrorException compilationError)
            {
                logCapture.Stop();
                errorDetails = string.Join(Environment.NewLine, compilationError.Diagnostics);
                LoopLogger.Error($"{McpProtocol.LogPrefix} Script execution compilation error:\n{errorDetails}");

                return CreateErrorResult("COMPILATION_ERROR", errorDetails, logCapture, assembliesDisplay);
            }
            catch (Exception ex)
            {
                logCapture.Stop();
                errorDetails = ex.ToString();
                LoopLogger.Error($"{McpProtocol.LogPrefix} Script execution runtime error:\n{errorDetails}");

                return CreateErrorResult("EXECUTION_ERROR", errorDetails, logCapture, assembliesDisplay);
            }
            finally
            {
                logCapture.Dispose();
            }
        }

        /// <summary>
        /// Executes a C# script synchronously in the Unity Editor context using Roslyn.
        /// </summary>
        /// <param name="script">The C# script code to execute</param>
        /// <returns>Execution result containing status, output, and errors</returns>
        public ExecutionResult ExecuteScript(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                return new ExecutionResult
                {
                    IsSuccess = false,
                    Status = "ERROR",
                    Errors = "Script is empty or missing."
                };
            }

            script = StripMarkdownFormatting(script);

            var assemblies = ResolveAssemblies();
            var options = CreateScriptOptions(assemblies);
            var assembliesDisplay = GetLoadedAssembliesDisplay(assemblies);

            var logCapture = new LogCapture();
            string errorDetails;

            try
            {
                logCapture.Start();

                object executionResult = CSharpScript.EvaluateAsync(script, options).GetAwaiter().GetResult();

                logCapture.Stop();

                MarkActiveSceneDirtyIfNeeded();

                return CreateSuccessResult(executionResult, logCapture, assembliesDisplay);
            }
            catch (CompilationErrorException compilationError)
            {
                logCapture.Stop();
                errorDetails = string.Join(Environment.NewLine, compilationError.Diagnostics);
                LoopLogger.Error($"{McpProtocol.LogPrefix} Script execution compilation error:\n{errorDetails}");

                return CreateErrorResult("COMPILATION_ERROR", errorDetails, logCapture, assembliesDisplay);
            }
            catch (Exception ex)
            {
                logCapture.Stop();
                errorDetails = ex.ToString();
                LoopLogger.Error($"{McpProtocol.LogPrefix} Script execution runtime error:\n{errorDetails}");

                return CreateErrorResult("EXECUTION_ERROR", errorDetails, logCapture, assembliesDisplay);
            }
            finally
            {
                logCapture.Dispose();
            }
        }

        /// <summary>
        /// Resolves assemblies that should be available for script execution.
        /// </summary>
        /// <returns>Array of loaded assemblies</returns>
        public System.Reflection.Assembly[] ResolveAssemblies()
        {
            var settings = UnityCodeMcpServerSettings.Instance;
            var assemblyNames = settings.GetAllAssemblyNames();

            var loaded = AppDomain.CurrentDomain.GetAssemblies();
            return assemblyNames
                .Select(name => loaded.FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.Ordinal)))
                .Where(a => a != null)
                .ToArray();
        }

        /// <summary>
        /// Creates Roslyn script options with appropriate references and imports.
        /// </summary>
        /// <param name="assemblies">Assemblies to include in script options</param>
        /// <returns>Configured ScriptOptions</returns>
        public ScriptOptions CreateScriptOptions(System.Reflection.Assembly[] assemblies)
        {
            return ScriptOptions.Default
                .WithReferences(CreateInMemoryReferences(assemblies))
                .WithImports("System", "System.Collections.Generic", "System.Linq", "UnityEngine", "UnityEditor")
                .WithOptimizationLevel(Microsoft.CodeAnalysis.OptimizationLevel.Release);
        }

        /// <summary>
        /// Gets a formatted display of currently loaded assemblies.
        /// </summary>
        /// <param name="assemblies">Assemblies to format</param>
        /// <returns>Array of formatted assembly information strings</returns>
        public string[] GetLoadedAssembliesDisplay(System.Reflection.Assembly[] assemblies)
        {
            return assemblies
                .Select(a => a?.GetName())
                .Where(n => n != null)
                .Select(n => $"{n.Name}, Version={n.Version}")
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToArray();
        }

        /// <summary>
        /// Marks the active scene as dirty if not in play mode.
        /// </summary>
        public void MarkActiveSceneDirtyIfNeeded()
        {
            if (!EditorApplication.isPlaying)
            {
                var sceneToMakeDirty = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (sceneToMakeDirty.IsValid())
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sceneToMakeDirty);
                }
            }
        }

        /// <summary>
        /// Formats an execution result value to string.
        /// </summary>
        /// <param name="executionResult">The result value to format</param>
        /// <returns>Formatted string representation</returns>
        public string FormatResult(object executionResult)
        {
            return executionResult == null ? "(null)" : executionResult.ToString();
        }

        private ExecutionResult CreateSuccessResult(object executionResult, LogCapture logCapture, string[] assembliesDisplay)
        {
            var hasLoggedErrors = logCapture.HasErrors;
            var statusLabel = hasLoggedErrors ? "SUCCESS_WITH_ERRORS" : "SUCCESS";
            var errorsText = hasLoggedErrors ? logCapture.ErrorLog : null;

            return new ExecutionResult
            {
                IsSuccess = true,
                Status = statusLabel,
                ResultValue = executionResult,
                ResultText = FormatResult(executionResult),
                Logs = logCapture.GetLogs(),
                Errors = errorsText,
                LoadedAssemblies = assembliesDisplay
            };
        }

        private ExecutionResult CreateErrorResult(string status, string errorDetails, LogCapture logCapture, string[] assembliesDisplay)
        {
            return new ExecutionResult
            {
                IsSuccess = false,
                Status = status,
                Errors = errorDetails,
                Logs = logCapture.GetLogs(),
                LoadedAssemblies = assembliesDisplay
            };
        }

        /// <summary>
        /// Creates metadata references from in-memory assembly images.
        /// </summary>
        /// <param name="assemblies">Assemblies to create references for</param>
        /// <returns>Array of MetadataReferences</returns>
        private MetadataReference[] CreateInMemoryReferences(System.Reflection.Assembly[] assemblies)
        {
            if (assemblies == null || assemblies.Length == 0)
            {
                return Array.Empty<MetadataReference>();
            }

            var references = new List<MetadataReference>(assemblies.Length);
            foreach (var assembly in assemblies)
            {
                if (assembly == null || assembly.IsDynamic) continue;

                var location = assembly.Location;
                if (string.IsNullOrWhiteSpace(location)) continue;

                try
                {
                    var image = System.IO.File.ReadAllBytes(location);
                    references.Add(MetadataReference.CreateFromImage(image));
                }
                catch (Exception ex)
                {
                    LoopLogger.Warn($"{McpProtocol.LogPrefix} Failed to load metadata for assembly '{assembly.GetName().Name}' at '{location}': {ex.Message}");
                }
            }

            return references.ToArray();
        }

        /// <summary>
        /// Strips markdown code block formatting from script text if present.
        /// </summary>
        /// <param name="script">Script text that may contain markdown formatting</param>
        /// <returns>Script text without markdown formatting</returns>
        private string StripMarkdownFormatting(string script)
        {
            if (!script.StartsWith("```"))
            {
                return script;
            }

            var lines = script.Split('\n').ToList();
            if (lines.Count > 0 && lines[0].StartsWith("```"))
                lines.RemoveAt(0);
            if (lines.Count > 0 && lines.Last().Trim() == "```")
                lines.RemoveAt(lines.Count - 1);

            return string.Join("\n", lines).Trim();
        }
    }
}
