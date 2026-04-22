using System.Text.Json;
using NUnit.Framework;
using UnityCodeMcpServer.Protocol;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class McpMessagesResourceContentTests
    {
        [Test]
        public void ResourceTextContent_CreatesResourceTypeWithText()
        {
            ContentItem contentItem = ContentItem.ResourceTextContent("resource://test-file", "text/plain", "hello world");

            Assert.That(contentItem.Type, Is.EqualTo(McpContentTypes.Resource));
            Assert.That(contentItem.Resource, Is.Not.Null);
            Assert.That(contentItem.Resource.Uri, Is.EqualTo("resource://test-file"));
            Assert.That(contentItem.Resource.MimeType, Is.EqualTo("text/plain"));
            Assert.That(contentItem.Resource.Text, Is.EqualTo("hello world"));
        }

        [Test]
        public void ResourceTextContent_Serializes_And_DoesNotContainBlob()
        {
            ContentItem contentItem = ContentItem.ResourceTextContent("resource://test-file", "text/plain", "hello world");
            string json = JsonHelper.Serialize(contentItem);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement resource = document.RootElement.GetProperty("resource");

            Assert.That(resource.GetProperty("uri").GetString(), Is.EqualTo("resource://test-file"));
            Assert.That(resource.GetProperty("mimeType").GetString(), Is.EqualTo("text/plain"));
            Assert.That(resource.GetProperty("text").GetString(), Is.EqualTo("hello world"));
            Assert.That(resource.TryGetProperty("blob", out _), Is.False, "`blob` must not be present in serialized JSON");
        }
    }
}
