using System;
using System.IO;
using NUnit.Framework;
using UnityCodeMcpServer.FileServer;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class FileServerRequestStoreTests
    {
        private string _rootDirectory;
        private string _messagesDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), "UnityCodeMcpServerTests", Guid.NewGuid().ToString("N"));
            _messagesDirectory = Path.Combine(_rootDirectory, ".unityCodeMcpServer", "messages");
            Directory.CreateDirectory(_messagesDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        [Test]
        public void TryGetNextPendingRequest_SelectsOldestRequestWithoutResponse()
        {
            WriteRequest("20260504120000001", "client-a");
            WriteRequest("20260504120000002", "client-a");
            WriteRequest("20260504120000003", "client-b");
            WriteResponse("20260504120000001", "client-a");

            FileServerRequestStore store = new(_messagesDirectory);

            bool found = store.TryGetNextPendingRequest(out FileServerRequestFile request);

            Assert.That(found, Is.True);
            Assert.That(Path.GetFileName(request.RequestPath), Is.EqualTo("20260504120000002_request_client-a.json"));
            Assert.That(Path.GetFileName(request.ResponsePath), Is.EqualTo("20260504120000002_response_client-a.json"));
        }

        [Test]
        public void TryGetNextPendingRequest_RescansDirectoryAfterNewFilesAppear()
        {
            WriteRequest("20260504120000002", "client-a");
            WriteRequest("20260504120000003", "client-a");

            FileServerRequestStore store = new(_messagesDirectory);

            bool foundFirst = store.TryGetNextPendingRequest(out FileServerRequestFile firstRequest);
            Assert.That(foundFirst, Is.True);
            Assert.That(Path.GetFileName(firstRequest.RequestPath), Is.EqualTo("20260504120000002_request_client-a.json"));

            WriteResponse("20260504120000002", "client-a");
            WriteRequest("20260504120000001", "client-b");

            bool foundSecond = store.TryGetNextPendingRequest(out FileServerRequestFile secondRequest);

            Assert.That(foundSecond, Is.True);
            Assert.That(Path.GetFileName(secondRequest.RequestPath), Is.EqualTo("20260504120000001_request_client-b.json"));
        }

        private void WriteRequest(string timestamp, string clientId)
        {
            string path = Path.Combine(_messagesDirectory, $"{timestamp}_request_{clientId}.json");
            File.WriteAllText(path, "{}\n");
        }

        private void WriteResponse(string timestamp, string clientId)
        {
            string path = Path.Combine(_messagesDirectory, $"{timestamp}_response_{clientId}.json");
            File.WriteAllText(path, "{}\n");
        }
    }
}
