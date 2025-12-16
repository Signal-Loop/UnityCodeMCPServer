using System;
using System.Collections.Generic;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using LoopMcpServer.Protocol;
using LoopMcpServer.Registry;
using LoopMcpServer.Settings;
using UnityEngine;

namespace LoopMcpServer.Handlers
{
    /// <summary>
    /// Handles JSON-RPC message parsing and dispatching to appropriate handlers
    /// </summary>
    public class McpMessageHandler
    {
        private readonly McpRegistry _registry;
        private readonly Dictionary<string, Func<JsonRpcRequest, UniTask<JsonRpcResponse>>> _handlers;

        private bool VerboseLogging => LoopMcpServerSettings.Instance.VerboseLogging;

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
                Debug.LogWarning($"{McpProtocol.LogPrefix} Parse error: {ex.Message}");
                var parseError = JsonRpcResponse.Failure(null, JsonRpcErrorCodes.ParseError, "Parse error");
                return JsonHelper.Serialize(parseError);
            }

            if (request == null || string.IsNullOrEmpty(request.Method))
            {
                var invalidRequest = JsonRpcResponse.Failure(request?.Id, JsonRpcErrorCodes.InvalidRequest, "Invalid request");
                return JsonHelper.Serialize(invalidRequest);
            }

            // Handle notifications (no response expected)
            if (request.IsNotification)
            {
                HandleNotification(request);
                return null;
            }

            var response = await HandleRequestAsync(request);
            return JsonHelper.Serialize(response);
        }

        private void HandleNotification(JsonRpcRequest request)
        {
            if (!VerboseLogging)
            {
                return;
            }

            Debug.Log($"{McpProtocol.LogPrefix} Received notification: {request.Method}");

            switch (request.Method)
            {
                case McpMethods.Initialized:
                    Debug.Log($"{McpProtocol.LogPrefix} Client initialized");
                    break;
                default:
                    Debug.Log($"{McpProtocol.LogPrefix} Unhandled notification: {request.Method}");
                    break;
            }
        }

        private async UniTask<JsonRpcResponse> HandleRequestAsync(JsonRpcRequest request)
        {
            if (VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} Handling request: {request.Method} (id: {request.Id})");
            }

            if (_handlers.TryGetValue(request.Method, out var handler))
            {
                try
                {
                    return await handler(request);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{McpProtocol.LogPrefix} Handler error for {request.Method}: {ex}");
                    return JsonRpcResponse.Failure(request.Id, JsonRpcErrorCodes.InternalError, $"Internal error: {ex.Message}");
                }
            }

            Debug.LogWarning($"{McpProtocol.LogPrefix} Method not found: {request.Method}");
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

            if (VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} Initialize from {initParams?.ClientInfo?.Name ?? "unknown"} (protocol: {initParams?.ProtocolVersion ?? "unknown"})");
            }

            var result = new InitializeResult
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
                    Name = "LoopMcpServer",
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
            if (VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} tools/list request (id: {request.Id})");
            }
            var result = _registry.GetToolsList();
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

            var arguments = callParams.Arguments ?? JsonHelper.ParseElement("{}");
            var result = await _registry.ExecuteToolAsync(callParams.Name, arguments);
            return JsonRpcResponse.Success(request.Id, result);
        }

        private UniTask<JsonRpcResponse> HandlePromptsList(JsonRpcRequest request)
        {
            if (VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} prompts/list request (id: {request.Id})");
            }
            var result = _registry.GetPromptsList();
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

            var result = _registry.GetPromptMessages(getParams.Name, getParams.Arguments ?? new Dictionary<string, string>());
            return UniTask.FromResult(JsonRpcResponse.Success(request.Id, result));
        }

        private UniTask<JsonRpcResponse> HandleResourcesList(JsonRpcRequest request)
        {
            if (VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} resources/list request (id: {request.Id})");
            }
            var result = _registry.GetResourcesList();
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

            var result = _registry.ReadResource(readParams.Uri);
            return UniTask.FromResult(JsonRpcResponse.Success(request.Id, result));
        }

        private UniTask<JsonRpcResponse> HandleResourcesTemplatesList(JsonRpcRequest request)
        {
            // Currently no resource templates supported
            var result = new ResourcesTemplatesListResult();
            return UniTask.FromResult(JsonRpcResponse.Success(request.Id, result));
        }

        private void LogRequestSummary(string kind, string name, object id)
        {
            var displayName = string.IsNullOrEmpty(name) ? "<unknown>" : name;
            Debug.Log($"{McpProtocol.LogPrefix} Received {kind} request: {displayName} (id: {id ?? "notification"})");
        }

        #endregion
    }
}
