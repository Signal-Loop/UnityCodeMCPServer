using System.Text.Json;
using UnityCodeMcpServer.Protocol;

namespace UnityCodeMcpServer.Interfaces
{
    /// <summary>
    /// Interface for synchronous MCP tools.
    /// Implement this interface to create tools that execute synchronously.
    /// Tools are automatically discovered via reflection.
    /// </summary>
    public interface ITool
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
        /// Execute the tool with the provided arguments
        /// </summary>
        /// <param name="arguments">Input arguments matching the InputSchema</param>
        /// <returns>Result containing content items and error status</returns>
        ToolsCallResult Execute(JsonElement arguments);
    }
}