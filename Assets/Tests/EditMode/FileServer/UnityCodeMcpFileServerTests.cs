using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCodeMcpServer.FileServer;
using UnityCodeMcpServer.Handlers;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Registry;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class UnityCodeMcpFileServerTests
    {
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "UnityCodeMcpServerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, true);
            }
        }

        [Test]
        public void CreateMessagesDirectory_CreatesExpectedProjectRelativeDirectory()
        {
            MethodInfo method = typeof(UnityCodeMcpFileServer).GetMethod(
                "CreateMessagesDirectory",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            string messagesDirectory = (string)method.Invoke(null, new object[] { _projectRoot });

            Assert.That(messagesDirectory, Is.EqualTo(Path.Combine(_projectRoot, ".unityCodeMcpServer", "messages")));
            Assert.That(Directory.Exists(messagesDirectory), Is.True);
        }

        [Test]
        public void RestartServer_MenuItemIsAvailable()
        {
            MethodInfo method = typeof(UnityCodeMcpFileServer).GetMethod(
                "RestartServer",
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            var attributeData = method.GetCustomAttributesData()
                .FirstOrDefault(attr => attr.AttributeType.FullName == "UnityEditor.MenuItem");

            Assert.That(attributeData, Is.Not.Null);
            Assert.That(attributeData.ConstructorArguments.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(attributeData.ConstructorArguments[0].Value, Is.EqualTo("Tools/UnityCodeMcpServer/Restart Server"));
        }

        [Test]
        public async Task ProcessNextRequestAsync_WritesJsonRpcResponseFile()
        {
            string messagesDirectory = Path.Combine(_projectRoot, ".unityCodeMcpServer", "messages");
            Directory.CreateDirectory(messagesDirectory);

            string requestPath = Path.Combine(messagesDirectory, "20260504120000001_request_client-a.json");
            JsonRpcRequest request = new()
            {
                Id = 1,
                Method = McpMethods.ToolsList,
                Params = JsonHelper.ParseElement("{}")
            };
            File.WriteAllText(requestPath, JsonHelper.Serialize(request));

            FileServerRequestStore store = new(messagesDirectory);
            McpRegistry registry = new();
            McpMessageHandler handler = new(registry);

            MethodInfo method = typeof(UnityCodeMcpFileServer).GetMethod(
                "ProcessNextRequestAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            Task<bool> task = (Task<bool>)method.Invoke(
                null,
                new object[] { store, handler, CancellationToken.None });
            bool processed = await task;

            string responsePath = Path.Combine(messagesDirectory, "20260504120000001_response_client-a.json");

            Assert.That(processed, Is.True);
            Assert.That(File.Exists(responsePath), Is.True);
            Assert.That(File.Exists(requestPath), Is.False);

            JObject document = JObject.Parse(File.ReadAllText(responsePath));
            JToken result = document["result"];
            Assert.That(result, Is.Not.Null);
            Assert.That(result["tools"], Is.Not.Null);
        }

        [Test]
        public async Task ReadRequestAsync_ReturnsJsonWithoutDeletingRequestFile()
        {
            string messagesDirectory = Path.Combine(_projectRoot, ".unityCodeMcpServer", "messages");
            Directory.CreateDirectory(messagesDirectory);

            string requestPath = Path.Combine(messagesDirectory, "20260504120000001_request_client-a.json");
            File.WriteAllText(requestPath, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}");

            MethodInfo method = typeof(UnityCodeMcpFileServer).GetMethod(
                "ReadRequestAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            Task<string> task = (Task<string>)method.Invoke(
                null,
                new object[] { requestPath, CancellationToken.None });
            string requestJson = await task;

            Assert.That(requestJson, Does.Contain("\"tools/list\""));
            Assert.That(File.Exists(requestPath), Is.True);
        }

        [Test]
        public async Task WriteAllTextAtomicallyAsync_WritesFinalFileWithoutLeavingTempFiles()
        {
            string messagesDirectory = Path.Combine(_projectRoot, ".unityCodeMcpServer", "messages");
            Directory.CreateDirectory(messagesDirectory);
            string responsePath = Path.Combine(messagesDirectory, "20260504120000001_response_client-a.json");

            MethodInfo method = typeof(UnityCodeMcpFileServer).GetMethod(
                "WriteAllTextAtomicallyAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            Task task = (Task)method.Invoke(
                null,
                new object[] { responsePath, "{\"jsonrpc\":\"2.0\"}", null, CancellationToken.None });
            await task;

            Assert.That(File.Exists(responsePath), Is.True);
            Assert.That(File.ReadAllText(responsePath), Is.EqualTo("{\"jsonrpc\":\"2.0\"}"));
            Assert.That(Directory.GetFiles(messagesDirectory, "*.tmp"), Is.Empty);
        }

        [Test]
        public void WriteAllTextAtomicallyAsync_WhenCanceled_DeletesTemporaryFile()
        {
            string messagesDirectory = Path.Combine(_projectRoot, ".unityCodeMcpServer", "messages");
            Directory.CreateDirectory(messagesDirectory);
            string responsePath = Path.Combine(messagesDirectory, "20260504120000002_response_client-a.json");

            MethodInfo method = typeof(UnityCodeMcpFileServer).GetMethod(
                "WriteAllTextAtomicallyAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            using CancellationTokenSource cts = new();
            cts.Cancel();

            Task task = (Task)method.Invoke(
                null,
                new object[] { responsePath, "{\"jsonrpc\":\"2.0\"}", null, cts.Token });

            Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
            Assert.That(File.Exists(responsePath), Is.False);
            Assert.That(Directory.GetFiles(messagesDirectory, "*.tmp"), Is.Empty);
        }

        [Test]
        public async Task WriteAllTextAtomicallyAsync_WhenMoveFails_LeavesRequestFileInPlace()
        {
            string messagesDirectory = Path.Combine(_projectRoot, ".unityCodeMcpServer", "messages");
            Directory.CreateDirectory(messagesDirectory);
            string requestPath = Path.Combine(messagesDirectory, "20260504120000003_request_client-a.json");
            string responsePath = Path.Combine(messagesDirectory, "20260504120000003_response_client-a.json");
            File.WriteAllText(requestPath, "{\"jsonrpc\":\"2.0\",\"id\":1}");
            Directory.CreateDirectory(responsePath);

            MethodInfo method = typeof(UnityCodeMcpFileServer).GetMethod(
                "WriteAllTextAtomicallyAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            Task task = (Task)method.Invoke(
                null,
                new object[] { responsePath, "{\"jsonrpc\":\"2.0\"}", requestPath, CancellationToken.None });

            IOException exception = null;
            try
            {
                await task;
            }
            catch (IOException ex)
            {
                exception = ex;
            }

            Assert.That(exception, Is.Not.Null);
            Assert.That(File.Exists(requestPath), Is.True);
            Assert.That(Directory.GetFiles(messagesDirectory, "*.tmp"), Is.Empty);
        }

        [Test]
        public async Task WriteAllTextAtomicallyAsync_DeletesRequestFileAfterResponseIsMoved()
        {
            string messagesDirectory = Path.Combine(_projectRoot, ".unityCodeMcpServer", "messages");
            Directory.CreateDirectory(messagesDirectory);
            string requestPath = Path.Combine(messagesDirectory, "20260504120000003_request_client-a.json");
            string responsePath = Path.Combine(messagesDirectory, "20260504120000003_response_client-a.json");
            File.WriteAllText(requestPath, "{\"jsonrpc\":\"2.0\",\"id\":1}");

            MethodInfo method = typeof(UnityCodeMcpFileServer).GetMethod(
                "WriteAllTextAtomicallyAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            Task task = (Task)method.Invoke(
                null,
                new object[] { responsePath, "{\"jsonrpc\":\"2.0\"}", requestPath, CancellationToken.None });
            await task;

            Assert.That(File.Exists(responsePath), Is.True);
            Assert.That(File.Exists(requestPath), Is.False);
        }

        [Test]
        public async Task ProcessRequestJsonAsync_WhenInvokedOffMainThread_StillReturnsJsonRpcResponse()
        {
            JsonRpcRequest request = new()
            {
                Id = 1,
                Method = McpMethods.ToolsList,
                Params = JsonHelper.ParseElement("{}")
            };
            McpRegistry registry = new();
            McpMessageHandler handler = new(registry);

            MethodInfo method = typeof(UnityCodeMcpFileServer).GetMethod(
                "ProcessRequestJsonAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            string responseJson = await Task.Run(async () =>
            {
                Task<string> task = (Task<string>)method.Invoke(
                    null,
                    new object[] { handler, JsonHelper.Serialize(request), CancellationToken.None });
                return await task;
            });

            JObject document = JObject.Parse(responseJson);
            JToken result = document["result"];
            JToken tools = result?["tools"];
            Assert.That(result, Is.Not.Null);
            Assert.That(tools, Is.Not.Null);
            Assert.That(tools.Type, Is.EqualTo(JTokenType.Array));
        }
    }
}
