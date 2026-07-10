using Newtonsoft.Json.Linq;
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

            JObject document = JObject.Parse(json);
            JToken resource = document["resource"];

            Assert.That(resource["uri"]?.Value<string>(), Is.EqualTo("resource://test-file"));
            Assert.That(resource["mimeType"]?.Value<string>(), Is.EqualTo("text/plain"));
            Assert.That(resource["text"]?.Value<string>(), Is.EqualTo("hello world"));
            Assert.That(resource["blob"], Is.Null, "`blob` must not be present in serialized JSON");
        }
    }
}
