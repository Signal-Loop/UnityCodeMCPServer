using System.Text.Json;
using LoopMcpServer.Handlers;
using LoopMcpServer.Protocol;
using LoopMcpServer.Registry;
using NUnit.Framework;

namespace LoopMcpServer.Tests
{
    [TestFixture]
    public class McpMessageHandlerTests
    {
        private McpRegistry _registry;
        private McpMessageHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _registry = new McpRegistry();
            _handler = new McpMessageHandler(_registry);
        }

        [Test]
        public void ProcessMessage_ParseError_ReturnsParseErrorResponse()
        {
            var result = _handler.ProcessMessageAsync("invalid json {{{").GetAwaiter().GetResult();

            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Not.Null);
            Assert.That(response.Error.Code, Is.EqualTo(JsonRpcErrorCodes.ParseError));
        }

        [Test]
        public void ProcessMessage_InvalidRequest_ReturnsInvalidRequestResponse()
        {
            var result = _handler.ProcessMessageAsync("{}").GetAwaiter().GetResult();

            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Not.Null);
            Assert.That(response.Error.Code, Is.EqualTo(JsonRpcErrorCodes.InvalidRequest));
        }

        [Test]
        public void ProcessMessage_MethodNotFound_ReturnsMethodNotFoundResponse()
        {
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = "nonexistent/method"
            };

            var result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Not.Null);
            Assert.That(response.Error.Code, Is.EqualTo(JsonRpcErrorCodes.MethodNotFound));
        }

        [Test]
        public void ProcessMessage_Initialize_ReturnsServerCapabilities()
        {
            var initParams = new InitializeParams
            {
                ProtocolVersion = McpProtocol.Version,
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            };

            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = McpMethods.Initialize,
                Params = JsonHelper.ParseElement(JsonHelper.Serialize(initParams))
            };

            var result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            var initResult = JsonHelper.Deserialize<InitializeResult>(response.Result);
            Assert.That(initResult.ProtocolVersion, Is.EqualTo(McpProtocol.Version));
            Assert.That(initResult.Capabilities.Tools, Is.Not.Null);
            Assert.That(initResult.Capabilities.Prompts, Is.Not.Null);
            Assert.That(initResult.Capabilities.Resources, Is.Not.Null);
        }

        [Test]
        public void ProcessMessage_Ping_ReturnsEmptyResult()
        {
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = McpMethods.Ping
            };

            var result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);
        }

        [Test]
        public void ProcessMessage_ToolsList_ReturnsToolsList()
        {
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = McpMethods.ToolsList
            };

            var result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            var toolsResult = JsonHelper.Deserialize<ToolsListResult>(response.Result);
            Assert.That(toolsResult.Tools, Is.Not.Null);
        }

        [Test]
        public void ProcessMessage_ToolsCall_MissingName_ReturnsInvalidParams()
        {
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = McpMethods.ToolsCall,
                Params = JsonHelper.ParseElement("{}")
            };

            var result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Not.Null);
            Assert.That(response.Error.Code, Is.EqualTo(JsonRpcErrorCodes.InvalidParams));
        }

        [Test]
        public void ProcessMessage_ToolsCall_ToolNotFound_ReturnsInvalidParams()
        {
            var toolsCallParams = new ToolsCallParams
            {
                Name = "nonexistent_tool"
            };

            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = McpMethods.ToolsCall,
                Params = JsonHelper.ParseElement(JsonHelper.Serialize(toolsCallParams))
            };

            var result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Not.Null);
            Assert.That(response.Error.Code, Is.EqualTo(JsonRpcErrorCodes.InvalidParams));
        }

        [Test]
        public void ProcessMessage_PromptsList_ReturnsPromptsList()
        {
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = McpMethods.PromptsList
            };

            var result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            var promptsResult = JsonHelper.Deserialize<PromptsListResult>(response.Result);
            Assert.That(promptsResult.Prompts, Is.Not.Null);
        }

        [Test]
        public void ProcessMessage_ResourcesList_ReturnsResourcesList()
        {
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = McpMethods.ResourcesList
            };

            var result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            var resourcesResult = JsonHelper.Deserialize<ResourcesListResult>(response.Result);
            Assert.That(resourcesResult.Resources, Is.Not.Null);
        }

        [Test]
        public void ProcessMessage_Notification_ReturnsNull()
        {
            var request = new JsonRpcRequest
            {
                // No Id = notification
                Method = McpMethods.Initialized
            };

            var result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            Assert.That(result, Is.Null);
        }
    }
}
