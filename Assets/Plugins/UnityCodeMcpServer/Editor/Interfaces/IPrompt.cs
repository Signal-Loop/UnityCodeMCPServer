using System.Collections.Generic;
using UnityCodeMcpServer.Protocol;

namespace UnityCodeMcpServer.Interfaces
{
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
}