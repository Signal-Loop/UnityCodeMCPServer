using NUnit.Framework;
using UnityCodeMcpServer.Resources;
using UnityEngine;
using UnityEngine.TestTools;
using System;
using System.Linq;

namespace UnityCodeMcpServer.Tests
{
    /// <summary>
    /// Comprehensive test suite for UnityConsoleLogsResource.
    /// Tests cover metadata validation, normal operation, edge cases, and error scenarios.
    /// </summary>
    public class UnityConsoleLogsResourceTests
    {
        private UnityConsoleLogsResource resource;

        [SetUp]
        public void Setup()
        {
            resource = new UnityConsoleLogsResource();
        }

        #region Metadata Tests

        [Test]
        public void Resource_Metadata_Name_IsCorrect()
        {
            Assert.AreEqual("Unity Console Logs", resource.Name);
        }

        [Test]
        public void Resource_Metadata_Uri_IsCorrect()
        {
            Assert.AreEqual("unity://console/logs", resource.Uri);
        }

        [Test]
        public void Resource_Metadata_MimeType_IsCorrect()
        {
            Assert.AreEqual("text/plain", resource.MimeType);
        }

        [Test]
        public void Resource_Metadata_Description_IsNotEmpty()
        {
            Assert.IsNotEmpty(resource.Description);
        }

        [Test]
        public void Resource_Metadata_IsConsistent()
        {
            // Verify metadata consistency across multiple calls
            var name1 = resource.Name;
            var uri1 = resource.Uri;
            var mimeType1 = resource.MimeType;
            var desc1 = resource.Description;

            var name2 = resource.Name;
            var uri2 = resource.Uri;
            var mimeType2 = resource.MimeType;
            var desc2 = resource.Description;

            Assert.AreEqual(name1, name2);
            Assert.AreEqual(uri1, uri2);
            Assert.AreEqual(mimeType1, mimeType2);
            Assert.AreEqual(desc1, desc2);
        }

        #endregion

        #region Read Operation Tests

        [Test]
        public void Read_ReturnsValidResult()
        {
            var result = resource.Read();

            Assert.IsNotNull(result, "Result should not be null");
            Assert.IsNotNull(result.Contents, "Contents collection should not be null");
            Assert.GreaterOrEqual(result.Contents.Count, 1, "Result should contain at least one content item");
        }

        [Test]
        public void Read_Returns_ResourceContent_WithCorrectUri()
        {
            var result = resource.Read();

            Assert.AreEqual(1, result.Contents.Count);
            Assert.AreEqual(resource.Uri, result.Contents[0].Uri);
        }

        [Test]
        public void Read_Returns_ResourceContent_WithCorrectMimeType()
        {
            var result = resource.Read();

            Assert.AreEqual(1, result.Contents.Count);
            Assert.AreEqual("text/plain", result.Contents[0].MimeType);
        }

        [Test]
        public void Read_ReturnsText_NotNull()
        {
            var result = resource.Read();

            Assert.IsNotNull(result.Contents[0].Text, "Text content should not be null");
        }

        #endregion

        #region Log Content Tests

        [Test]
        public void Read_CaptureSingleLogMessage()
        {
            var resource = new UnityConsoleLogsResource();

            // Log a unique message
            string uniqueMessage = "Test_Single_Log_" + System.Guid.NewGuid();
            Debug.Log(uniqueMessage);

            var result = resource.Read();

            Assert.IsNotNull(result.Contents[0].Text);
            StringAssert.Contains(uniqueMessage, result.Contents[0].Text,
                "Log content should contain the unique message");
        }

        [Test]
        public void Read_CaptureMultipleLogMessages()
        {
            var resource = new UnityConsoleLogsResource();

            string msg1 = "Test_Multiple_Log_1_" + System.Guid.NewGuid();
            string msg2 = "Test_Multiple_Log_2_" + System.Guid.NewGuid();

            Debug.Log(msg1);
            Debug.Log(msg2);

            var result = resource.Read();
            string content = result.Contents[0].Text;

            StringAssert.Contains(msg1, content, "First log message should be present");
            StringAssert.Contains(msg2, content, "Second log message should be present");
        }

        [Test]
        public void Read_CaptureMultipleLevels_Log_Warning_Error()
        {
            var resource = new UnityConsoleLogsResource();

            string logMsg = "TestLog_" + System.Guid.NewGuid();
            string warnMsg = "TestWarning_" + System.Guid.NewGuid();
            string errMsg = "TestError_" + System.Guid.NewGuid();

            Debug.Log(logMsg);
            Debug.LogWarning(warnMsg);

            // Capture error by wrapping in LogAssert.Expect
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*TestError.*"));
            Debug.LogError(errMsg);

            var result = resource.Read();
            string content = result.Contents[0].Text;

            StringAssert.Contains(logMsg, content, "Log message should be present");
            StringAssert.Contains(warnMsg, content, "Warning message should be present");
            StringAssert.Contains(errMsg, content, "Error message should be present");
        }

        #endregion

        #region Edge Case Tests

        [Test]
        public void Read_HandlesEmptyLogs_Gracefully()
        {
            // This test verifies that the resource handles the case gracefully
            // even if no custom logs have been added
            var result = resource.Read();

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Contents);
            Assert.Greater(result.Contents.Count, 0, "Should return at least one content entry");
            Assert.IsNotNull(result.Contents[0].Text, "Text should not be null even with empty logs");
        }

        [Test]
        public void Read_ContainsNoNull_Text()
        {
            var result = resource.Read();

            foreach (var content in result.Contents)
            {
                Assert.IsNotNull(content.Text, "No content text should be null");
            }
        }

        [Test]
        public void Read_MultipleReadCalls_ReturnValidResults()
        {
            // Verify that multiple reads don't cause state corruption
            var result1 = resource.Read();
            var result2 = resource.Read();

            Assert.IsNotNull(result1.Contents);
            Assert.IsNotNull(result2.Contents);
            Assert.Greater(result1.Contents.Count, 0);
            Assert.Greater(result2.Contents.Count, 0);
        }

        #endregion

        #region Robustness Tests

        [Test]
        public void Read_AlwaysReturnsResourcesReadResult()
        {
            var result = resource.Read();
            Assert.IsNotNull(result, "Read() must always return a non-null ResourcesReadResult");
            Assert.IsInstanceOf<UnityCodeMcpServer.Protocol.ResourcesReadResult>(result);
        }

        [Test]
        public void Read_ContentHas_UriProperty()
        {
            var result = resource.Read();
            var content = result.Contents.FirstOrDefault();

            Assert.IsNotNull(content);
            Assert.IsNotNull(content.Uri);
            Assert.AreEqual(resource.Uri, content.Uri);
        }

        [Test]
        public void Read_ContentHas_MimeTypeProperty()
        {
            var result = resource.Read();
            var content = result.Contents.FirstOrDefault();

            Assert.IsNotNull(content);
            Assert.IsNotNull(content.MimeType);
            Assert.AreEqual("text/plain", content.MimeType);
        }

        [Test]
        public void Read_Handles_LargeMessageContent()
        {
            var resource = new UnityConsoleLogsResource();

            // Create a large message
            string largeMsg = "LargeMessage_" + new string('A', 5000) + "_" + System.Guid.NewGuid();
            Debug.Log(largeMsg);

            var result = resource.Read();

            Assert.IsNotNull(result);
            Assert.Greater(result.Contents.Count, 0);
            StringAssert.Contains("LargeMessage_", result.Contents[0].Text);
        }

        [Test]
        public void Read_Handles_SpecialCharacters()
        {
            var resource = new UnityConsoleLogsResource();

            string specialMsg = "SpecialChars_!@#$%^&*()_+-=[]{}|;':\"<>,.?/_" + System.Guid.NewGuid();
            Debug.Log(specialMsg);

            var result = resource.Read();

            Assert.IsNotNull(result);
            StringAssert.Contains("SpecialChars_", result.Contents[0].Text);
        }

        [Test]
        public void Read_Handles_Newlines_InMessages()
        {
            var resource = new UnityConsoleLogsResource();

            string msgWithNewline = "Message\nWith\nNewlines_" + System.Guid.NewGuid();
            Debug.Log(msgWithNewline);

            var result = resource.Read();

            Assert.IsNotNull(result);
            Assert.Greater(result.Contents[0].Text.Length, 0);
        }

        #endregion

        #region Contract Tests

        [Test]
        public void Resource_Implements_IResource()
        {
            Assert.IsInstanceOf<UnityCodeMcpServer.Interfaces.IResource>(resource);
        }

        [Test]
        public void Read_Result_Contains_ValidContent()
        {
            var result = resource.Read();
            var content = result.Contents.FirstOrDefault();

            Assert.IsNotNull(content);
            Assert.IsNotEmpty(content.Text);
            Assert.AreEqual(resource.Uri, content.Uri);
            Assert.AreEqual(resource.MimeType, content.MimeType);
        }

        #endregion
    }
}
