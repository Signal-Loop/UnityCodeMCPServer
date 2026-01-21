using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Handlers;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Registry;
using UnityCodeMcpServer.Servers.StreamableHttp;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace UnityCodeMcpServer.Tests.StreamableHttp
{
    /// <summary>
    /// Integration tests for the HTTP server components.
    /// These tests verify the interaction between HttpRequestHandler, SessionManager, and McpMessageHandler.
    /// </summary>
    [TestFixture]
    public class HttpRequestHandlerIntegrationTests
    {
        private McpRegistry _registry;
        private McpMessageHandler _messageHandler;
        private SessionManager _sessionManager;
        private HttpRequestHandler _requestHandler;

        [SetUp]
        public void SetUp()
        {
            _registry = new McpRegistry();
            _registry.DiscoverAndRegisterAll();
            _messageHandler = new McpMessageHandler(_registry);
            _sessionManager = new SessionManager(sessionTimeoutSeconds: 3600, cleanupIntervalSeconds: 60);
            _requestHandler = new HttpRequestHandler(_messageHandler, _sessionManager);
        }

        [TearDown]
        public void TearDown()
        {
            _sessionManager?.Dispose();
        }

        #region Constructor Tests

        [Test]
        public void Constructor_NullMessageHandler_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new HttpRequestHandler(null, _sessionManager));
        }

        [Test]
        public void Constructor_NullSessionManager_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new HttpRequestHandler(_messageHandler, null));
        }

        [Test]
        public void Constructor_ValidParameters_CreatesHandler()
        {
            var handler = new HttpRequestHandler(_messageHandler, _sessionManager);
            Assert.That(handler, Is.Not.Null);
        }

        #endregion

        #region Session Workflow Tests

        [Test]
        public void SessionWorkflow_CreateValidateTerminate()
        {
            // Simulate the HTTP workflow without actual HTTP

            // 1. Create session (happens during initialize POST)
            var sessionId = _sessionManager.CreateSession();
            Assert.That(_sessionManager.ValidateSession(sessionId), Is.True);

            // 2. Mark as initialized
            var session = _sessionManager.GetSession(sessionId);
            session.IsInitialized = true;
            Assert.That(session.IsInitialized, Is.True);

            // 3. Touch session (happens on each request)
            _sessionManager.TouchSession(sessionId);

            // 4. Terminate session (DELETE request)
            var terminated = _sessionManager.TerminateSession(sessionId);
            Assert.That(terminated, Is.True);
            Assert.That(_sessionManager.ValidateSession(sessionId), Is.False);
        }

        [Test]
        public void SessionWorkflow_MultipleSessionsIndependent()
        {
            // Create multiple sessions
            var sessionId1 = _sessionManager.CreateSession();
            var sessionId2 = _sessionManager.CreateSession();
            var sessionId3 = _sessionManager.CreateSession();

            // Mark them as initialized
            _sessionManager.GetSession(sessionId1).IsInitialized = true;
            _sessionManager.GetSession(sessionId2).IsInitialized = true;
            _sessionManager.GetSession(sessionId3).IsInitialized = true;

            // Terminate one
            _sessionManager.TerminateSession(sessionId2);

            // Others should still be valid
            Assert.That(_sessionManager.ValidateSession(sessionId1), Is.True);
            Assert.That(_sessionManager.ValidateSession(sessionId2), Is.False);
            Assert.That(_sessionManager.ValidateSession(sessionId3), Is.True);

            Assert.That(_sessionManager.ActiveSessionCount, Is.EqualTo(2));
        }

        #endregion

        #region Message Handler Integration Tests

        [Test]
        public void MessageHandler_InitializeRequest_ReturnsCapabilities()
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

            var result = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();
            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            var initResult = JsonHelper.Deserialize<InitializeResult>(response.Result);
            Assert.That(initResult.ProtocolVersion, Is.EqualTo(McpProtocol.Version));
            Assert.That(initResult.Capabilities.Tools, Is.Not.Null);
        }

        [Test]
        public void MessageHandler_ToolsListRequest_ReturnsList()
        {
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = McpMethods.ToolsList
            };

            var result = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();
            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            var toolsResult = JsonHelper.Deserialize<ToolsListResult>(response.Result);
            Assert.That(toolsResult.Tools, Is.Not.Null);
        }

        [Test]
        public void MessageHandler_PingRequest_ReturnsSuccess()
        {
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = McpMethods.Ping
            };

            var result = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();
            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);
        }

        [Test]
        public void MessageHandler_Notification_ReturnsNull()
        {
            var request = new JsonRpcRequest
            {
                // No Id = notification
                Method = McpMethods.Initialized
            };

            var result = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(request)).GetAwaiter().GetResult();

            Assert.That(result, Is.Null);
        }

        #endregion

        #region Full HTTP Workflow Simulation Tests

        [Test]
        public void FullWorkflow_InitializeThenToolsListThenPing()
        {
            // Step 1: Initialize - creates session
            var sessionId = _sessionManager.CreateSession();
            var session = _sessionManager.GetSession(sessionId);
            session.IsInitialized = true;

            var initParams = new InitializeParams
            {
                ProtocolVersion = McpProtocol.Version,
                ClientInfo = new ClientInfo { Name = "TestClient", Version = "1.0.0" }
            };
            var initRequest = new JsonRpcRequest
            {
                Id = 1,
                Method = McpMethods.Initialize,
                Params = JsonHelper.ParseElement(JsonHelper.Serialize(initParams))
            };
            var initResult = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(initRequest)).GetAwaiter().GetResult();
            Assert.That(initResult, Is.Not.Null);

            // Step 2: tools/list
            _sessionManager.TouchSession(sessionId);
            var toolsRequest = new JsonRpcRequest
            {
                Id = 2,
                Method = McpMethods.ToolsList
            };
            var toolsResult = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(toolsRequest)).GetAwaiter().GetResult();
            Assert.That(toolsResult, Is.Not.Null);

            // Step 3: ping
            _sessionManager.TouchSession(sessionId);
            var pingRequest = new JsonRpcRequest
            {
                Id = 3,
                Method = McpMethods.Ping
            };
            var pingResult = _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(pingRequest)).GetAwaiter().GetResult();
            Assert.That(pingResult, Is.Not.Null);

            // Session should still be valid
            Assert.That(_sessionManager.ValidateSession(sessionId), Is.True);
        }

        [UnityTest]
        public IEnumerator FullWorkflow_ExecuteAsyncTool() => UniTask.ToCoroutine(async () =>
        {
            // Create and initialize session
            var sessionId = _sessionManager.CreateSession();
            var session = _sessionManager.GetSession(sessionId);
            session.IsInitialized = true;

            // Execute async tool
            var toolParams = new ToolsCallParams
            {
                Name = "test_async_tool"
            };
            var request = new JsonRpcRequest
            {
                Id = 1,
                Method = McpMethods.ToolsCall,
                Params = JsonHelper.ParseElement(JsonHelper.Serialize(toolParams))
            };

            var result = await _messageHandler.ProcessMessageAsync(JsonHelper.Serialize(request));
            var response = JsonHelper.Deserialize<JsonRpcResponse>(result);

            Assert.That(response.Error, Is.Null);
            Assert.That(response.Result, Is.Not.Null);

            var callResult = JsonHelper.Deserialize<ToolsCallResult>(response.Result);
            Assert.That(callResult.Content, Is.Not.Empty);
            Assert.That(callResult.Content[0].Text, Does.Contain("Test async result"));
        });

        #endregion
    }
}
