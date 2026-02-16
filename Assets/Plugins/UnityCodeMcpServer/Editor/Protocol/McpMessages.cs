using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityCodeMcpServer.Protocol
{
    /// <summary>
    /// JSON-RPC 2.0 Request object
    /// </summary>
    [Serializable]
    public class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = McpProtocol.JsonRpcVersion;

        [JsonPropertyName("id")]
        public object Id { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; }

        [JsonPropertyName("params")]
        public JsonElement? Params { get; set; }

        [JsonIgnore]
        public bool IsNotification => Id == null;
    }

    /// <summary>
    /// JSON-RPC 2.0 Response object
    /// </summary>
    [Serializable]
    public class JsonRpcResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = McpProtocol.JsonRpcVersion;

        [JsonPropertyName("id")]
        public object Id { get; set; }

        [JsonPropertyName("result")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object Result { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonRpcError Error { get; set; }

        public static JsonRpcResponse Success(object id, object result) =>
            new JsonRpcResponse { Id = id, Result = result };

        public static JsonRpcResponse Failure(object id, int code, string message, object data = null) =>
            new JsonRpcResponse { Id = id, Error = new JsonRpcError { Code = code, Message = message, Data = data } };
    }

    /// <summary>
    /// JSON-RPC 2.0 Error object
    /// </summary>
    [Serializable]
    public class JsonRpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object Data { get; set; }
    }

    #region Initialize Messages

    [Serializable]
    public class InitializeParams
    {
        [JsonPropertyName("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonPropertyName("capabilities")]
        public ClientCapabilities Capabilities { get; set; }

        [JsonPropertyName("clientInfo")]
        public ClientInfo ClientInfo { get; set; }
    }

    [Serializable]
    public class ClientCapabilities
    {
        [JsonPropertyName("roots")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RootsCapability Roots { get; set; }

        [JsonPropertyName("sampling")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object Sampling { get; set; }
    }

    [Serializable]
    public class RootsCapability
    {
        [JsonPropertyName("listChanged")]
        public bool ListChanged { get; set; }
    }

    [Serializable]
    public class ClientInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }
    }

    [Serializable]
    public class InitializeResult
    {
        [JsonPropertyName("protocolVersion")]
        public string ProtocolVersion { get; set; } = McpProtocol.Version;

        [JsonPropertyName("capabilities")]
        public ServerCapabilities Capabilities { get; set; } = new ServerCapabilities();

        [JsonPropertyName("serverInfo")]
        public ServerInfo ServerInfo { get; set; } = new ServerInfo();
    }

    [Serializable]
    public class ServerCapabilities
    {
        [JsonPropertyName("prompts")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CapabilityWithListChanged Prompts { get; set; }

        [JsonPropertyName("resources")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ResourcesCapability Resources { get; set; }

        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CapabilityWithListChanged Tools { get; set; }
    }

    [Serializable]
    public class CapabilityWithListChanged
    {
        [JsonPropertyName("listChanged")]
        public bool ListChanged { get; set; }
    }

    [Serializable]
    public class ResourcesCapability
    {
        [JsonPropertyName("subscribe")]
        public bool Subscribe { get; set; }

        [JsonPropertyName("listChanged")]
        public bool ListChanged { get; set; }
    }

    [Serializable]
    public class ServerInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "UnityCodeMcpServer";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";
    }

    #endregion

    #region Tool Messages

    [Serializable]
    public class ToolDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("inputSchema")]
        public JsonElement InputSchema { get; set; }
    }

    [Serializable]
    public class ToolsListResult
    {
        [JsonPropertyName("tools")]
        public List<ToolDefinition> Tools { get; set; } = new List<ToolDefinition>();
    }

    [Serializable]
    public class ToolsCallParams
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("arguments")]
        public JsonElement? Arguments { get; set; }
    }

    [Serializable]
    public class ToolsCallResult
    {
        [JsonPropertyName("content")]
        public List<ContentItem> Content { get; set; } = new List<ContentItem>();

        [JsonPropertyName("isError")]
        public bool IsError { get; set; }

        public static ToolsCallResult TextResult(string text, bool isError = false) =>
            new ToolsCallResult { Content = new List<ContentItem> { ContentItem.TextContent(text) }, IsError = isError };

        public static ToolsCallResult ImageResult(string base64Data, string mimeType) =>
            new ToolsCallResult { Content = new List<ContentItem> { ContentItem.ImageContent(base64Data, mimeType) } };

        public static ToolsCallResult ErrorResult(string errorMessage) =>
            new ToolsCallResult { Content = new List<ContentItem> { ContentItem.TextContent(errorMessage) }, IsError = true };
    }

    [Serializable]
    public class ContentItem
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Text { get; set; }

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Data { get; set; }

        [JsonPropertyName("mimeType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string MimeType { get; set; }

        [JsonPropertyName("resource")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ResourceContent Resource { get; set; }

        public static ContentItem TextContent(string text) =>
            new ContentItem { Type = McpContentTypes.Text, Text = text };

        public static ContentItem ImageContent(string base64Data, string mimeType) =>
            new ContentItem { Type = McpContentTypes.Image, Data = base64Data, MimeType = mimeType };

        public static ContentItem ResourceTextContent(string uri, string mimeType, string text) =>
            new ContentItem { Type = McpContentTypes.Resource, Resource = new ResourceContent { Uri = uri, MimeType = mimeType, Text = text } };
    }

    #endregion

    #region Prompt Messages

    [Serializable]
    public class PromptDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("arguments")]
        public List<PromptArgument> Arguments { get; set; }
    }

    [Serializable]
    public class PromptArgument
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("required")]
        public bool Required { get; set; }
    }

    [Serializable]
    public class PromptsListResult
    {
        [JsonPropertyName("prompts")]
        public List<PromptDefinition> Prompts { get; set; } = new List<PromptDefinition>();
    }

    [Serializable]
    public class PromptsGetParams
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("arguments")]
        public Dictionary<string, string> Arguments { get; set; }
    }

    [Serializable]
    public class PromptsGetResult
    {
        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("messages")]
        public List<PromptMessage> Messages { get; set; } = new List<PromptMessage>();
    }

    [Serializable]
    public class PromptMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("content")]
        public ContentItem Content { get; set; }
    }

    #endregion

    #region Resource Messages

    [Serializable]
    public class ResourceDefinition
    {
        [JsonPropertyName("uri")]
        public string Uri { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; }
    }

    [Serializable]
    public class ResourcesListResult
    {
        [JsonPropertyName("resources")]
        public List<ResourceDefinition> Resources { get; set; } = new List<ResourceDefinition>();
    }

    [Serializable]
    public class ResourcesReadParams
    {
        [JsonPropertyName("uri")]
        public string Uri { get; set; }
    }

    [Serializable]
    public class ResourcesReadResult
    {
        [JsonPropertyName("contents")]
        public List<ResourceContent> Contents { get; set; } = new List<ResourceContent>();
    }

    [Serializable]
    public class ResourceContent
    {
        [JsonPropertyName("uri")]
        public string Uri { get; set; }

        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; }

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Text { get; set; }


    }

    [Serializable]
    public class ResourceTemplate
    {
        [JsonPropertyName("uriTemplate")]
        public string UriTemplate { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; }
    }

    [Serializable]
    public class ResourcesTemplatesListResult
    {
        [JsonPropertyName("resourceTemplates")]
        public List<ResourceTemplate> ResourceTemplates { get; set; } = new List<ResourceTemplate>();
    }

    #endregion
}
