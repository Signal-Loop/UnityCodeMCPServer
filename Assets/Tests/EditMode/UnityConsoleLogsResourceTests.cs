using System;
using NUnit.Framework;
using UnityCodeMcpServer.McpResources;
using UnityEngine;

namespace UnityCodeMcpServer.Tests.EditMode
{
    public class UnityConsoleLogsResourceTests
    {
        [Test]
        public void Resource_Metadata_IsCorrect()
        {
            var resource = new UnityConsoleLogsResource();

            Assert.AreEqual("Unity Console Logs", resource.Name);
            Assert.AreEqual("unity://console/logs", resource.Uri);
            Assert.AreEqual("text/plain", resource.MimeType);
            Assert.IsNotEmpty(resource.Description);
        }

        [Test]
        public void Read_UsesSharedReaderLimit()
        {
            int capturedLimit = -1;
            var resource = new UnityConsoleLogsResource(limit =>
            {
                capturedLimit = limit;
                return ("tail", false);
            });

            var result = resource.Read();

            Assert.AreEqual(1000, capturedLimit);
            Assert.AreEqual(1, result.Contents.Count);
            Assert.AreEqual(resource.Uri, result.Contents[0].Uri);
            Assert.AreEqual(resource.MimeType, result.Contents[0].MimeType);
            Assert.AreEqual("tail", result.Contents[0].Text);
        }

        [Test]
        public void Read_ReplacesWhitespaceResult_WithPlaceholder()
        {
            var resource = new UnityConsoleLogsResource(_ => ("  ", false));

            var result = resource.Read();

            StringAssert.Contains("(No console logs available)", result.Contents[0].Text);
        }

        [Test]
        public void Read_CapturesCurrentConsoleMessage()
        {
            var resource = new UnityConsoleLogsResource();
            string uniqueMessage = "Resource_Log_" + Guid.NewGuid();
            Debug.Log(uniqueMessage);

            var result = resource.Read();

            StringAssert.Contains(uniqueMessage, result.Contents[0].Text);
        }
    }
}
