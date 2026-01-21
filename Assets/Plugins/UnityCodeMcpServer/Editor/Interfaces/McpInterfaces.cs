using System.Collections.Generic;
using System.Text.Json;
using Cysharp.Threading.Tasks;
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

    /// <summary>
    /// Interface for MCP prompts.
    /// Implement this interface to create prompt templates.
    /// Prompts are automatically discovered via reflection.
    /// </summary>
    public interface IPrompt
    {
        /// <summary>
        /// Unique name identifying this prompt
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Human-readable description of what this prompt does
        /// </summary>
        string Description { get; }

        /// <summary>
        /// List of arguments this prompt accepts
        /// </summary>
        List<PromptArgument> Arguments { get; }

        /// <summary>
        /// Get the prompt messages with the provided arguments
        /// </summary>
        /// <param name="arguments">Arguments to substitute into the prompt</param>
        /// <returns>Result containing the prompt messages</returns>
        PromptsGetResult GetMessages(Dictionary<string, string> arguments);
    }

    /// <summary>
    /// Interface for MCP resources.
    /// Implement this interface to expose resources.
    /// Resources are automatically discovered via reflection.
    /// </summary>
    public interface IResource
    {
        /// <summary>
        /// Unique URI identifying this resource
        /// </summary>
        string Uri { get; }

        /// <summary>
        /// Human-readable name of this resource
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of this resource
        /// </summary>
        string Description { get; }

        /// <summary>
        /// MIME type of the resource content
        /// </summary>
        string MimeType { get; }

        /// <summary>
        /// Read the resource content
        /// </summary>
        /// <returns>Result containing the resource content</returns>
        ResourcesReadResult Read();
    }
}
