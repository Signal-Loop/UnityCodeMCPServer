using System;

namespace UnityCodeMcpServer.Protocol
{
    /// <summary>
    /// Streamable HTTP transport constants per MCP specification 2025-03-26.
    /// Placed alongside HTTP server implementation for cohesion.
    /// </summary>
    public static class McpHttpTransport
    {
        /// <summary>
        /// Protocol version for Streamable HTTP transport
        /// </summary>
        public const string ProtocolVersion = "2025-03-26";

        /// <summary>
        /// Header name for session identifier
        /// </summary>
        public const string SessionIdHeader = "Mcp-Session-Id";

        /// <summary>
        /// Header name for protocol version
        /// </summary>
        public const string ProtocolVersionHeader = "MCP-Protocol-Version";

        /// <summary>
        /// Content type for JSON responses
        /// </summary>
        public const string ContentTypeJson = "application/json";

        /// <summary>
        /// Content type for Server-Sent Events streams
        /// </summary>
        public const string ContentTypeSse = "text/event-stream";

        /// <summary>
        /// Accept header value that clients must send
        /// </summary>
        public const string AcceptHeaderValue = "application/json, text/event-stream";

        /// <summary>
        /// SSE event type for messages
        /// </summary>
        public const string SseEventMessage = "message";

        /// <summary>
        /// Default MCP endpoint path
        /// </summary>
        public const string EndpointPath = "/mcp/";
    }
}
