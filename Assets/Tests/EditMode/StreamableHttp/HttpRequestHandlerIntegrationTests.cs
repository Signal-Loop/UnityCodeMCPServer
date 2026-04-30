using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityCodeMcpServer.Handlers;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Registry;
using UnityCodeMcpServer.Servers.StreamableHttp;
using UnityEngine.TestTools;

namespace UnityCodeMcpServer.Tests.EditMode.StreamableHttp
{
    /// <summary>
    /// Integration tests for the HTTP server components.
    /// These tests verify the interaction between HttpRequestHandler and McpMessageHandler.
    /// </summary>
    [TestFixture]
    public class HttpRequestHandlerIntegrationTests
    {
        private McpRegistry _registry;
        private McpMessageHandler _messageHandler;
        private HttpRequestHandler _requestHandler;

        [SetUp]
        public void SetUp()
        {
            _registry = new McpRegistry();
            _registry.DiscoverAndRegisterAll();
            _messageHandler = new McpMessageHandler(_registry);
            _requestHandler = new HttpRequestHandler(_messageHandler);
        }

        #region Constructor Tests

        [Test]
        public void Constructor_NullMessageHandler_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new HttpRequestHandler(null));
        }

        [Test]
        public void Constructor_ValidParameters_CreatesHandler()
        {
            HttpRequestHandler handler = new(_messageHandler);
            Assert.That(handler, Is.Not.Null);
        }

        [Test]
        public void HandleRequestAsync_LoopbackPingRequest_WritesJsonResponse()
        {
            string requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}";
            MemoryStream outputStream = new();
            LoopbackHttpContext context = new(
                new LoopbackHttpRequest(
                    "POST",
                    "/mcp/",
                    new Dictionary<string, string>
                    {
                        ["Accept"] = "application/json, text/event-stream"
                    },
                    new MemoryStream(Encoding.UTF8.GetBytes(requestJson))),
                new LoopbackHttpResponse(outputStream));

            _requestHandler.HandleRequestAsync(context, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(context.Response.StatusCode, Is.EqualTo(200));
            Assert.That(context.Response.ContentType, Is.EqualTo(McpHttpTransport.ContentTypeJson));
            Assert.That(Encoding.UTF8.GetString(outputStream.ToArray()), Does.Contain("\"id\":1"));
        }

        [Test]
        public void HandleRequestAsync_LoopbackNotification_ReturnsAccepted()
        {
            string requestJson = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}";
            MemoryStream outputStream = new();
            LoopbackHttpContext context = new(
                new LoopbackHttpRequest(
                    "POST",
                    "/mcp/",
                    new Dictionary<string, string>
                    {
                        ["Accept"] = "application/json"
                    },
                    new MemoryStream(Encoding.UTF8.GetBytes(requestJson))),
                new LoopbackHttpResponse(outputStream));

            _requestHandler.HandleRequestAsync(context, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(context.Response.StatusCode, Is.EqualTo(202));
            Assert.That(outputStream.Length, Is.EqualTo(0));
        }

        [Test]
        public void HandleRequestAsync_LoopbackWrongPath_ReturnsNotFound()
        {
            string requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}";
            MemoryStream outputStream = new();
            LoopbackHttpContext context = new(
                new LoopbackHttpRequest(
                    "POST",
                    "/wrong-path",
                    new Dictionary<string, string>
                    {
                        ["Accept"] = "application/json"
                    },
                    new MemoryStream(Encoding.UTF8.GetBytes(requestJson))),
                new LoopbackHttpResponse(outputStream));

            _requestHandler.HandleRequestAsync(context, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        }

        #endregion

        #region Message Handler Integration Tests

        [Test]
        public void MessageHandler_InitializeRequest_ReturnsCapabilities()
        {
            InitializeParams initParams = new()
            {
                ProtocolVersion = McpProtocol.Version,
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            };

            JsonRpcRequest request = new()
            {
                Id = 1,
                Method = McpMethods.Initialize,
                Params = JsonHelper.ParseElement(JsonHelper.Serialize(initParams))
            };

            string result = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();
            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            InitializeResult initResult = JsonHelper.Deserialize<InitializeResult>(response.Result);
            Assert.That(initResult.ProtocolVersion, Is.EqualTo(McpProtocol.Version));
            Assert.That(initResult.Capabilities.Tools, Is.Not.Null);
        }

        [Test]
        public void MessageHandler_ToolsListRequest_ReturnsList()
        {
            JsonRpcRequest request = new()
            {
                Id = 1,
                Method = McpMethods.ToolsList
            };

            string result = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();
            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            ToolsListResult toolsResult = JsonHelper.Deserialize<ToolsListResult>(response.Result);
            Assert.That(toolsResult.Tools, Is.Not.Null);
        }

        [Test]
        public void MessageHandler_PingRequest_ReturnsSuccess()
        {
            JsonRpcRequest request = new()
            {
                Id = 1,
                Method = McpMethods.Ping
            };

            string result = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();
            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);
        }

        [Test]
        public void MessageHandler_Notification_ReturnsNull()
        {
            JsonRpcRequest request = new()
            {
                // No Id = notification
                Method = McpMethods.Initialized
            };

            string result = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            Assert.That(result, Is.Null);
        }

        #endregion

        #region Full HTTP Workflow Simulation Tests

        [Test]
        public void FullWorkflow_InitializeThenToolsListThenPing()
        {
            InitializeParams initParams = new()
            {
                ProtocolVersion = McpProtocol.Version,
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            };
            JsonRpcRequest initRequest = new()
            {
                Id = 1,
                Method = McpMethods.Initialize,
                Params = JsonHelper.ParseElement(JsonHelper.Serialize(initParams))
            };
            string initResult = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(initRequest)).GetAwaiter().GetResult();
            Assert.That(initResult, Is.Not.Null);

            JsonRpcRequest toolsRequest = new()
            {
                Id = 2,
                Method = McpMethods.ToolsList
            };
            string toolsResult = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(toolsRequest)).GetAwaiter().GetResult();
            Assert.That(toolsResult, Is.Not.Null);

            JsonRpcRequest pingRequest = new()
            {
                Id = 3,
                Method = McpMethods.Ping
            };
            string pingResult = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(pingRequest)).GetAwaiter().GetResult();
            Assert.That(pingResult, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator FullWorkflow_ExecuteAsyncTool() => UniTask.ToCoroutine(async () =>
        {
            ToolsCallParams toolParams = new()
            {
                Name = "test_async_tool"
            };
            JsonRpcRequest request = new()
            {
                Id = 1,
                Method = McpMethods.ToolsCall,
                Params = JsonHelper.ParseElement(JsonHelper.Serialize(toolParams))
            };

            string result = await _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(request));
            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            ToolsCallResult callResult = JsonHelper.Deserialize<ToolsCallResult>(response.Result);
            Assert.That(callResult.Content, Is.Not.Empty);
            Assert.That(callResult.Content[0].Text, Does.Contain("Test async result"));
        });

        #endregion
    }
}
