using UnityCodeMcpServer.Protocol;
using NUnit.Framework;

namespace UnityCodeMcpServer.Tests.StreamableHttp
{
    [TestFixture]
    public class McpHttpTransportTests
    {
        #region Protocol Constants Tests

        [Test]
        public void ProtocolVersion_IsCorrectFormat()
        {
            Assert.That(McpHttpTransport.ProtocolVersion, Is.EqualTo("2025-03-26"));
        }

        [Test]
        public void SessionIdHeader_IsCorrectValue()
        {
            Assert.That(McpHttpTransport.SessionIdHeader, Is.EqualTo("Mcp-Session-Id"));
        }

        [Test]
        public void ProtocolVersionHeader_IsCorrectValue()
        {
            Assert.That(McpHttpTransport.ProtocolVersionHeader, Is.EqualTo("MCP-Protocol-Version"));
        }

        #endregion

        #region Content Type Constants Tests

        [Test]
        public void ContentTypeJson_IsCorrectValue()
        {
            Assert.That(McpHttpTransport.ContentTypeJson, Is.EqualTo("application/json"));
        }

        [Test]
        public void ContentTypeSse_IsCorrectValue()
        {
            Assert.That(McpHttpTransport.ContentTypeSse, Is.EqualTo("text/event-stream"));
        }

        [Test]
        public void AcceptHeaderValue_ContainsJsonAndSse()
        {
            var accept = McpHttpTransport.AcceptHeaderValue;

            Assert.That(accept, Does.Contain(McpHttpTransport.ContentTypeJson));
            Assert.That(accept, Does.Contain(McpHttpTransport.ContentTypeSse));
        }

        #endregion

        #region SSE Event Constants Tests

        [Test]
        public void SseEventMessage_IsCorrectValue()
        {
            Assert.That(McpHttpTransport.SseEventMessage, Is.EqualTo("message"));
        }

        #endregion

        #region Endpoint Path Tests

        [Test]
        public void EndpointPath_IsCorrectValue()
        {
            Assert.That(McpHttpTransport.EndpointPath, Is.EqualTo("/mcp/"));
        }

        [Test]
        public void EndpointPath_StartsWithSlash()
        {
            Assert.That(McpHttpTransport.EndpointPath, Does.StartWith("/"));
        }

        [Test]
        public void EndpointPath_EndsWithSlash()
        {
            Assert.That(McpHttpTransport.EndpointPath, Does.EndWith("/"));
        }

        #endregion
    }
}
