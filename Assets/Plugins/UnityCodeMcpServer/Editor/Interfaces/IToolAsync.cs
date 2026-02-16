using System.Text.Json;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Protocol;

namespace UnityCodeMcpServer.Interfaces
{
    /// <summary>
    /// Interface for asynchronous MCP tools.
    /// Implement this interface to create tools that execute asynchronously using UniTask.
    /// Tools are automatically discovered via reflection.
    /// </summary>
    public interface IToolAsync
    {
        /// <summary>
        /// Unique name identifying this tool
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Human-readable description of what this tool does
        /// </summary>
        string Description { get; }

        /// <summary>
        /// JSON Schema defining the input parameters for this tool
        /// </summary>
        JsonElement InputSchema { get; }

        /// <summary>
        /// Execute the tool asynchronously with the provided arguments
        /// </summary>
        /// <param name="arguments">Input arguments matching the InputSchema</param>
        /// <returns>Async result containing content items and error status</returns>
        UniTask<ToolsCallResult> ExecuteAsync(JsonElement arguments);
    }
}