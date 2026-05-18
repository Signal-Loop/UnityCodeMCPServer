using NUnit.Framework;
using UnityCodeMcpServer.Handlers;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Registry;

namespace UnityCodeMcpServer.Tests.EditMode
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
            string result = _handler.ProcessMessageAsync("invalid json {{{").GetAwaiter().GetResult();

            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Not.Null);
            Assert.That(response.Error.Code, Is.EqualTo(JsonRpcErrorCodes.ParseError));
        }

        [Test]
        public void ProcessMessage_InvalidRequest_ReturnsInvalidRequestResponse()
        {
            string result = _handler.ProcessMessageAsync("{}").GetAwaiter().GetResult();

            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Not.Null);
            Assert.That(response.Error.Code, Is.EqualTo(JsonRpcErrorCodes.InvalidRequest));
        }

        [Test]
        public void ProcessMessage_MethodNotFound_ReturnsMethodNotFoundResponse()
        {
            JsonRpcRequest request = new()
            {
                Id = 1,
                Method = "nonexistent/method"
            };

            string result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Not.Null);
            Assert.That(response.Error.Code, Is.EqualTo(JsonRpcErrorCodes.MethodNotFound));
        }

        [Test]
        public void ProcessMessage_Initialize_ReturnsServerCapabilities()
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

            string result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            InitializeResult initResult = JsonHelper.Deserialize<InitializeResult>(response.Result);
            Assert.That(initResult.ProtocolVersion, Is.EqualTo(McpProtocol.Version));
            Assert.That(initResult.Capabilities.Tools, Is.Not.Null);
            Assert.That(initResult.Capabilities.Prompts, Is.Not.Null);
            Assert.That(initResult.Capabilities.Resources, Is.Not.Null);
        }

        [Test]
        public void ProcessMessage_Ping_ReturnsEmptyResult()
        {
            JsonRpcRequest request = new()
            {
                Id = 1,
                Method = McpMethods.Ping
            };

            string result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);
        }

        [Test]
        public void ProcessMessage_ToolsList_ReturnsToolsList()
        {
            JsonRpcRequest request = new()
            {
                Id = 1,
                Method = McpMethods.ToolsList
            };

            string result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            ToolsListResult toolsResult = JsonHelper.Deserialize<ToolsListResult>(response.Result);
            Assert.That(toolsResult.Tools, Is.Not.Null);
        }

        [Test]
        public void ProcessMessage_ToolsCall_MissingName_ReturnsInvalidParams()
        {
            JsonRpcRequest request = new()
            {
                Id = 1,
                Method = McpMethods.ToolsCall,
                Params = JsonHelper.ParseElement("{}")
            };

            string result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Not.Null);
            Assert.That(response.Error.Code, Is.EqualTo(JsonRpcErrorCodes.InvalidParams));
        }

        [Test]
        public void ProcessMessage_ToolsCall_ToolNotFound_ReturnsInvalidParams()
        {
            ToolsCallParams toolsCallParams = new()
            {
                Name = "nonexistent_tool"
            };

            JsonRpcRequest request = new()
            {
                Id = 1,
                Method = McpMethods.ToolsCall,
                Params = JsonHelper.ParseElement(JsonHelper.Serialize(toolsCallParams))
            };

            string result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Not.Null);
            Assert.That(response.Error.Code, Is.EqualTo(JsonRpcErrorCodes.InvalidParams));
        }

        [Test]
        public void ProcessMessage_PromptsList_ReturnsPromptsList()
        {
            JsonRpcRequest request = new()
            {
                Id = 1,
                Method = McpMethods.PromptsList
            };

            string result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            PromptsListResult promptsResult = JsonHelper.Deserialize<PromptsListResult>(response.Result);
            Assert.That(promptsResult.Prompts, Is.Not.Null);
        }

        [Test]
        public void ProcessMessage_ResourcesList_ReturnsResourcesList()
        {
            JsonRpcRequest request = new()
            {
                Id = 1,
                Method = McpMethods.ResourcesList
            };

            string result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            JsonRpcResponse response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            ResourcesListResult resourcesResult = JsonHelper.Deserialize<ResourcesListResult>(response.Result);
            Assert.That(resourcesResult.Resources, Is.Not.Null);
        }

        [Test]
        public void ProcessMessage_Notification_ReturnsNull()
        {
            JsonRpcRequest request = new()
            {
                // No Id = notification
                Method = McpMethods.Initialized
            };

            string result = _handler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            Assert.That(result, Is.Null);
        }
    }
}
