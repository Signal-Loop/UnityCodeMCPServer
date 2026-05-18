using System;
using System.Collections.Generic;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Registry;

namespace UnityCodeMcpServer.Handlers
{
    /// <summary>
    /// Handles JSON-RPC message parsing and dispatching to appropriate handlers
    /// </summary>
    public class McpMessageHandler
    {
        private readonly McpRegistry _registry;
        private readonly Dictionary<string, Func<JsonRpcRequest, UniTask<JsonRpcResponse>>> _handlers;

        public McpMessageHandler(McpRegistry registry)
        {
            _registry = registry;
            _handlers = new Dictionary<string, Func<JsonRpcRequest, UniTask<JsonRpcResponse>>>
            {
                { McpMethods.Initialize, HandleInitialize },
                { McpMethods.Ping, HandlePing },
                { McpMethods.ToolsList, HandleToolsList },
                { McpMethods.ToolsCall, HandleToolsCall },
                { McpMethods.PromptsList, HandlePromptsList },
                { McpMethods.PromptsGet, HandlePromptsGet },
                { McpMethods.ResourcesList, HandleResourcesList },
                { McpMethods.ResourcesRead, HandleResourcesRead },
                { McpMethods.ResourcesTemplatesList, HandleResourcesTemplatesList },
            };
        }

        /// <summary>
        /// Process a raw JSON message and return the response
        /// </summary>
        public async UniTask<string> ProcessMessageAsync(string json)
        {
            JsonRpcRequest request;

            try
            {
                request = JsonHelper.Deserialize<JsonRpcRequest>(json);
            }
            catch (JsonException ex)
            {
                UnityCodeMcpServerLogger.Warn($"Parse error: {ex.Message}");
                JsonRpcResponse parseError = JsonRpcResponse.Failure(null, JsonRpcErrorCodes.ParseError, "Parse error");
                return JsonHelper.Serialize(parseError);
            }

            if (request == null || string.IsNullOrEmpty(request.Method))
            {
                JsonRpcResponse invalidRequest = JsonRpcResponse.Failure(request?.Id, JsonRpcErrorCodes.InvalidRequest, "Invalid request");
                return JsonHelper.Serialize(invalidRequest);
            }

            // Handle notifications (no response expected)
            if (request.IsNotification)
            {
                HandleNotification(request);
                return null;
            }

            JsonRpcResponse response = await HandleRequestAsync(request);
            return JsonHelper.Serialize(response);
        }

        private void HandleNotification(JsonRpcRequest request)
        {
            UnityCodeMcpServerLogger.Debug($"Received notification: {request.Method}");

            switch (request.Method)
            {
                case McpMethods.Initialized:
                    UnityCodeMcpServerLogger.Debug($"Client initialized");
                    break;
                default:
                    UnityCodeMcpServerLogger.Debug($"Unhandled notification: {request.Method}");
                    break;
            }
        }

        private async UniTask<JsonRpcResponse> HandleRequestAsync(JsonRpcRequest request)
        {
            UnityCodeMcpServerLogger.Debug($"Handling request: {request.Method} (id: {request.Id})");

            if (_handlers.TryGetValue(request.Method, out Func<JsonRpcRequest, UniTask<JsonRpcResponse>> handler))
            {
                try
                {
                    return await handler(request);
                }
                catch (Exception ex)
                {
                    UnityCodeMcpServerLogger.Error($"Handler error for {request.Method}: {ex}");
                    return JsonRpcResponse.Failure(request.Id, JsonRpcErrorCodes.InternalError, $"Internal error: {ex.Message}");
                }
            }

            UnityCodeMcpServerLogger.Warn($"Method not found: {request.Method}");
            return JsonRpcResponse.Failure(request.Id, JsonRpcErrorCodes.MethodNotFound, $"Method not found: {request.Method}");
        }

        #region Handler Methods

        private UniTask<JsonRpcResponse> HandleInitialize(JsonRpcRequest request)
        {
            InitializeParams initParams = null;
            if (request.Params.HasValue)
            {
                initParams = request.Params.Value.Deserialize<InitializeParams>();
            }

            UnityCodeMcpServerLogger.Info($"Initialize from {initParams?.ClientInfo?.Name ?? "unknown"} (protocol: {initParams?.ProtocolVersion ?? "unknown"})");

            InitializeResult result = new()
            {
                ProtocolVersion = McpProtocol.Version,
                Capabilities = new ServerCapabilities
                {
                    Tools = new CapabilityWithListChanged { ListChanged = true },
                    Prompts = new CapabilityWithListChanged { ListChanged = true },
                    Resources = new ResourcesCapability { ListChanged = true, Subscribe = false }
                },
                ServerInfo = new ServerInfo
                {
                    Name = "UnityCodeMcpServer",
                    Version = "1.0.0"
                }
            };

            return UniTask.FromResult(JsonRpcResponse.Success(request.Id, result));
        }

        private UniTask<JsonRpcResponse> HandlePing(JsonRpcRequest request)
        {
            return UniTask.FromResult(JsonRpcResponse.Success(request.Id, new { }));
        }

        private UniTask<JsonRpcResponse> HandleToolsList(JsonRpcRequest request)
        {
            UnityCodeMcpServerLogger.Debug($"tools/list request (id: {request.Id})");
            ToolsListResult result = _registry.GetToolsList();
            return UniTask.FromResult(JsonRpcResponse.Success(request.Id, result));
        }

        private async UniTask<JsonRpcResponse> HandleToolsCall(JsonRpcRequest request)
        {
            ToolsCallParams callParams = null;
            if (request.Params.HasValue)
            {
                callParams = request.Params.Value.Deserialize<ToolsCallParams>();
            }

            if (callParams == null || string.IsNullOrEmpty(callParams.Name))
            {
                return JsonRpcResponse.Failure(request.Id, JsonRpcErrorCodes.InvalidParams, "Missing tool name");
            }

            LogRequestSummary("tool", callParams.Name, request.Id);

            if (!_registry.HasTool(callParams.Name))
            {
                return JsonRpcResponse.Failure(request.Id, JsonRpcErrorCodes.InvalidParams, $"Tool not found: {callParams.Name}");
            }

            JsonElement arguments = callParams.Arguments ?? JsonHelper.ParseElement("{}");
            ToolsCallResult result = await _registry.ExecuteToolAsync(callParams.Name, arguments);
            return JsonRpcResponse.Success(request.Id, result);
        }

        private UniTask<JsonRpcResponse> HandlePromptsList(JsonRpcRequest request)
        {
            UnityCodeMcpServerLogger.Debug($"prompts/list request (id: {request.Id})");
            PromptsListResult result = _registry.GetPromptsList();
            return UniTask.FromResult(JsonRpcResponse.Success(request.Id, result));
        }

        private UniTask<JsonRpcResponse> HandlePromptsGet(JsonRpcRequest request)
        {
            PromptsGetParams getParams = null;
            if (request.Params.HasValue)
            {
                getParams = request.Params.Value.Deserialize<PromptsGetParams>();
            }

            if (getParams == null || string.IsNullOrEmpty(getParams.Name))
            {
                return UniTask.FromResult(JsonRpcResponse.Failure(request.Id, JsonRpcErrorCodes.InvalidParams, "Missing prompt name"));
            }

            LogRequestSummary("prompt", getParams.Name, request.Id);

            if (!_registry.HasPrompt(getParams.Name))
            {
                return UniTask.FromResult(JsonRpcResponse.Failure(request.Id, JsonRpcErrorCodes.InvalidParams, $"Prompt not found: {getParams.Name}"));
            }

            PromptsGetResult result = _registry.GetPromptMessages(getParams.Name, getParams.Arguments ?? new Dictionary<string, string>());
            return UniTask.FromResult(JsonRpcResponse.Success(request.Id, result));
        }

        private UniTask<JsonRpcResponse> HandleResourcesList(JsonRpcRequest request)
        {
            UnityCodeMcpServerLogger.Debug($"resources/list request (id: {request.Id})");
            ResourcesListResult result = _registry.GetResourcesList();
            return UniTask.FromResult(JsonRpcResponse.Success(request.Id, result));
        }

        private UniTask<JsonRpcResponse> HandleResourcesRead(JsonRpcRequest request)
        {
            ResourcesReadParams readParams = null;
            if (request.Params.HasValue)
            {
                readParams = request.Params.Value.Deserialize<ResourcesReadParams>();
            }

            if (readParams == null || string.IsNullOrEmpty(readParams.Uri))
            {
                return UniTask.FromResult(JsonRpcResponse.Failure(request.Id, JsonRpcErrorCodes.InvalidParams, "Missing resource URI"));
            }

            LogRequestSummary("resource", readParams.Uri, request.Id);

            if (!_registry.HasResource(readParams.Uri))
            {
                return UniTask.FromResult(JsonRpcResponse.Failure(request.Id, JsonRpcErrorCodes.ResourceNotFound, $"Resource not found: {readParams.Uri}"));
            }

            ResourcesReadResult result = _registry.ReadResource(readParams.Uri);
            return UniTask.FromResult(JsonRpcResponse.Success(request.Id, result));
        }

        private UniTask<JsonRpcResponse> HandleResourcesTemplatesList(JsonRpcRequest request)
        {
            // Currently no resource templates supported
            ResourcesTemplatesListResult result = new();
            return UniTask.FromResult(JsonRpcResponse.Success(request.Id, result));
        }

        private void LogRequestSummary(string kind, string name, object id)
        {
            string displayName = string.IsNullOrEmpty(name) ? "<unknown>" : name;
            UnityCodeMcpServerLogger.Debug($"Received {kind} request: {displayName} (id: {id ?? "notification"})");
        }

        #endregion
    }
}
