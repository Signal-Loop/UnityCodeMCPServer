using UnityCodeMcpServer.Protocol;

namespace UnityCodeMcpServer.Interfaces
{
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