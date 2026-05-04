using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Cysharp.Threading.Tasks;
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
        public async System.Threading.Tasks.Task ProcessNextRequestAsync_WritesJsonRpcResponseFile()
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

            UniTask<bool> task = (UniTask<bool>)method.Invoke(
                null,
                new object[] { store, handler, CancellationToken.None });
            bool processed = await task;

            string responsePath = Path.Combine(messagesDirectory, "20260504120000001_response_client-a.json");

            Assert.That(processed, Is.True);
            Assert.That(File.Exists(responsePath), Is.True);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(responsePath));
            Assert.That(document.RootElement.TryGetProperty("result", out JsonElement result), Is.True);
            Assert.That(result.TryGetProperty("tools", out _), Is.True);
        }

        [Test]
        public async System.Threading.Tasks.Task WriteAllTextAtomicallyAsync_WritesFinalFileWithoutLeavingTempFiles()
        {
            string messagesDirectory = Path.Combine(_projectRoot, ".unityCodeMcpServer", "messages");
            Directory.CreateDirectory(messagesDirectory);
            string responsePath = Path.Combine(messagesDirectory, "20260504120000001_response_client-a.json");

            MethodInfo method = typeof(UnityCodeMcpFileServer).GetMethod(
                "WriteAllTextAtomicallyAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            UniTask task = (UniTask)method.Invoke(
                null,
                new object[] { responsePath, "{\"jsonrpc\":\"2.0\"}", CancellationToken.None });
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

            UniTask task = (UniTask)method.Invoke(
                null,
                new object[] { responsePath, "{\"jsonrpc\":\"2.0\"}", cts.Token });

            Assert.ThrowsAsync<System.Threading.Tasks.TaskCanceledException>(async () => await task);
            Assert.That(File.Exists(responsePath), Is.False);
            Assert.That(Directory.GetFiles(messagesDirectory, "*.tmp"), Is.Empty);
        }

        [Test]
        public async System.Threading.Tasks.Task ProcessRequestJsonAsync_WhenInvokedOffMainThread_StillReturnsJsonRpcResponse()
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

            string responseJson = await UniTask.RunOnThreadPool(async () =>
            {
                UniTask<string> task = (UniTask<string>)method.Invoke(
                    null,
                    new object[] { handler, JsonHelper.Serialize(request), CancellationToken.None });
                return await task;
            });

            using JsonDocument document = JsonDocument.Parse(responseJson);
            Assert.That(document.RootElement.TryGetProperty("result", out JsonElement result), Is.True);
            Assert.That(result.TryGetProperty("tools", out JsonElement tools), Is.True);
            Assert.That(tools.ValueKind, Is.EqualTo(JsonValueKind.Array));
        }
    }
}
