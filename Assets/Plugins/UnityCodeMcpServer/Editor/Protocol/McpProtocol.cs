using System;
using System.Collections.Generic;

namespace UnityCodeMcpServer.Protocol
{
    /// <summary>
    /// MCP Protocol version and constants per specification 2024-11-05
    /// </summary>
    public static class McpProtocol
    {
        public const string Version = "2024-11-05";
        public const string JsonRpcVersion = "2.0";
        public const string LogPrefix = "#UnityCodeMcpServer";
    }

    /// <summary>
    /// MCP method names as defined in the specification
    /// </summary>
    public static class McpMethods
    {
        // Lifecycle
        public const string Initialize = "initialize";
        public const string Initialized = "notifications/initialized";
        public const string Ping = "ping";

        // Tools
        public const string ToolsList = "tools/list";
        public const string ToolsCall = "tools/call";

        // Prompts
        public const string PromptsList = "prompts/list";
        public const string PromptsGet = "prompts/get";

        // Resources
        public const string ResourcesList = "resources/list";
        public const string ResourcesRead = "resources/read";
        public const string ResourcesTemplatesList = "resources/templates/list";
        public const string ResourcesSubscribe = "resources/subscribe";
        public const string ResourcesUnsubscribe = "resources/unsubscribe";

        // Notifications
        public const string ToolsListChanged = "notifications/tools/list_changed";
        public const string PromptsListChanged = "notifications/prompts/list_changed";
        public const string ResourcesListChanged = "notifications/resources/list_changed";
        public const string ResourcesUpdated = "notifications/resources/updated";
    }

    /// <summary>
    /// JSON-RPC 2.0 error codes
    /// </summary>
    public static class JsonRpcErrorCodes
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;

        // Server error range: -32000 to -32099
        public const int ServerError = -32000;

        // MCP-specific
        public const int ResourceNotFound = -32002;
    }

    /// <summary>
    /// Content types for tool results
    /// </summary>
    public static class McpContentTypes
    {
        public const string Text = "text";
        public const string Image = "image";
        public const string Resource = "resource";
    }

    /// <summary>
    /// Prompt message roles
    /// </summary>
    public static class McpRoles
    {
        public const string User = "user";
        public const string Assistant = "assistant";
    }
}
