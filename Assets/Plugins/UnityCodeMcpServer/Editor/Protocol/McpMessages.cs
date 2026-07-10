using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityCodeMcpServer.Protocol
{
    [Serializable]
    public class JsonRpcRequest
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = McpProtocol.JsonRpcVersion;

        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public JToken Params { get; set; }

        [JsonIgnore]
        public bool IsNotification => Id == null;
    }

    [Serializable]
    public class JsonRpcResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = McpProtocol.JsonRpcVersion;

        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public object Result { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public JsonRpcError Error { get; set; }

        public static JsonRpcResponse Success(object id, object result) =>
            new()
            {
                Id = id,
                Result = result
            };

        public static JsonRpcResponse Failure(object id, int code, string message, object data = null) =>
            new()
            {
                Id = id,
                Error = new JsonRpcError
                {
                    Code = code,
                    Message = message,
                    Data = data
                }
            };
    }

    [Serializable]
    public class JsonRpcError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; set; }
    }

    #region Initialize Messages

    [Serializable]
    public class InitializeParams
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonProperty("capabilities")]
        public ClientCapabilities Capabilities { get; set; }

        [JsonProperty("clientInfo")]
        public ClientInfo ClientInfo { get; set; }
    }

    [Serializable]
    public class ClientCapabilities
    {
        [JsonProperty("roots", NullValueHandling = NullValueHandling.Ignore)]
        public RootsCapability Roots { get; set; }

        [JsonProperty("sampling", NullValueHandling = NullValueHandling.Ignore)]
        public object Sampling { get; set; }
    }

    [Serializable]
    public class RootsCapability
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    [Serializable]
    public class ClientInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    [Serializable]
    public class InitializeResult
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; } = McpProtocol.Version;

        [JsonProperty("capabilities")]
        public ServerCapabilities Capabilities { get; set; } = new();

        [JsonProperty("serverInfo")]
        public ServerInfo ServerInfo { get; set; } = new();
    }

    [Serializable]
    public class ServerCapabilities
    {
        [JsonProperty("prompts", NullValueHandling = NullValueHandling.Ignore)]
        public CapabilityWithListChanged Prompts { get; set; }

        [JsonProperty("resources", NullValueHandling = NullValueHandling.Ignore)]
        public ResourcesCapability Resources { get; set; }

        [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
        public CapabilityWithListChanged Tools { get; set; }
    }

    [Serializable]
    public class CapabilityWithListChanged
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    [Serializable]
    public class ResourcesCapability
    {
        [JsonProperty("subscribe")]
        public bool Subscribe { get; set; }

        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    [Serializable]
    public class ServerInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "UnityCodeMcpServer";

        [JsonProperty("version")]
        public string Version { get; set; } = "1.0.0";
    }

    #endregion

    #region Tool Messages

    [Serializable]
    public class ToolDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("inputSchema")]
        public JToken InputSchema { get; set; }
    }

    [Serializable]
    public class ToolsListResult
    {
        [JsonProperty("tools")]
        public List<ToolDefinition> Tools { get; set; } = new();
    }

    [Serializable]
    public class ToolsCallParams
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("arguments")]
        public JToken Arguments { get; set; }
    }

    [Serializable]
    public class ToolsCallResult
    {
        [JsonProperty("content")]
        public List<ContentItem> Content { get; set; } = new();

        [JsonProperty("isError")]
        public bool IsError { get; set; }

        public static ToolsCallResult TextResult(string text, bool isError = false) =>
            new()
            {
                Content = new List<ContentItem> { ContentItem.TextContent(text) },
                IsError = isError
            };

        public static ToolsCallResult ImageResult(string base64Data, string mimeType) =>
            new()
            {
                Content = new List<ContentItem> { ContentItem.ImageContent(base64Data, mimeType) }
            };

        public static ToolsCallResult ErrorResult(string errorMessage) =>
            new()
            {
                Content = new List<ContentItem> { ContentItem.TextContent(errorMessage) },
                IsError = true
            };
    }

    [Serializable]
    public class ContentItem
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public string Data { get; set; }

        [JsonProperty("mimeType", NullValueHandling = NullValueHandling.Ignore)]
        public string MimeType { get; set; }

        [JsonProperty("resource", NullValueHandling = NullValueHandling.Ignore)]
        public ResourceContent Resource { get; set; }

        public static ContentItem TextContent(string text) =>
            new() { Type = McpContentTypes.Text, Text = text };

        public static ContentItem ImageContent(string base64Data, string mimeType) =>
            new() { Type = McpContentTypes.Image, Data = base64Data, MimeType = mimeType };

        public static ContentItem ResourceTextContent(string uri, string mimeType, string text) =>
            new()
            {
                Type = McpContentTypes.Resource,
                Resource = new ResourceContent
                {
                    Uri = uri,
                    MimeType = mimeType,
                    Text = text
                }
            };
    }

    #endregion

    #region Prompt Messages

    [Serializable]
    public class PromptDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("arguments")]
        public List<PromptArgument> Arguments { get; set; }
    }

    [Serializable]
    public class PromptArgument
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("required")]
        public bool Required { get; set; }
    }

    [Serializable]
    public class PromptsListResult
    {
        [JsonProperty("prompts")]
        public List<PromptDefinition> Prompts { get; set; } = new();
    }

    [Serializable]
    public class PromptsGetParams
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("arguments")]
        public Dictionary<string, string> Arguments { get; set; }
    }

    [Serializable]
    public class PromptsGetResult
    {
        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("messages")]
        public List<PromptMessage> Messages { get; set; } = new();
    }

    [Serializable]
    public class PromptMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content")]
        public ContentItem Content { get; set; }
    }

    #endregion

    #region Resource Messages

    [Serializable]
    public class ResourceDefinition
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }
    }

    [Serializable]
    public class ResourcesListResult
    {
        [JsonProperty("resources")]
        public List<ResourceDefinition> Resources { get; set; } = new();
    }

    [Serializable]
    public class ResourcesReadParams
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }
    }

    [Serializable]
    public class ResourcesReadResult
    {
        [JsonProperty("contents")]
        public List<ResourceContent> Contents { get; set; } = new();
    }

    [Serializable]
    public class ResourceContent
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }
    }

    [Serializable]
    public class ResourceTemplate
    {
        [JsonProperty("uriTemplate")]
        public string UriTemplate { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }
    }

    [Serializable]
    public class ResourcesTemplatesListResult
    {
        [JsonProperty("resourceTemplates")]
        public List<ResourceTemplate> ResourceTemplates { get; set; } = new();
    }

    #endregion
}
